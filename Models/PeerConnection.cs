using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BencodeNET.Torrents;

using bittorrent.Core;

namespace bittorrent.Models;

public class PeerConnection
{
    public const int CHUNK_SIZE = 16384; // 2^14 aka 16 kiB

    public Peer Peer { get; }
    public Torrent Torrent { get; }
    public byte[] PeerId { get; }
    public Channel<ICtrlMsg> PeerChannel { get; }
    public BitArray PeerHas { get; private set; }
    public BitArray Have { get; set; }

    private bool _amChoked = true;
    private bool _isChoked = true;
    private bool _amInterested = false;
    private bool _isInterested = false;
    private TcpClient _client = new();
    private List<IPeerMessage> _queuedMsgs = new();
    private Lock _queueLock = new();
    private List<int> _piecesToDownload = new();
    private Task? _listenerTask;
    private Task? _controlTask;
    private CancellationTokenSource _cancellation = new();

    private readonly Channel<ICtrlMsg> _ctrlChannel;

    private PeerConnection(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<ICtrlMsg> ctrlChannel,
        Channel<ICtrlMsg> peerChannel,
        BitArray have
    )
    {
        if (peerId.Length != 20)
        {
            throw new ArgumentException("Peer id is of incorrect length.");
        }

        Peer = peer;
        Torrent = torrent;
        PeerId = peerId;
        PeerChannel = peerChannel;
        PeerHas = new BitArray(Torrent.NumberOfPieces);
        Have = have;
        _ctrlChannel = ctrlChannel;
    }

    public static async Task<PeerConnection> CreateAsync(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<ICtrlMsg> ctrlChannel,
        Channel<ICtrlMsg> peerChannel,
        BitArray have
    )
    {
        var pc = new PeerConnection(peer, torrent, peerId, ctrlChannel, peerChannel, have);
        await pc.Start();
        return pc;
    }

    public async Task Start()
    {
        _client.Close();
        _client = new();
        await _client.ConnectAsync(Peer.Ip, Peer.Port, _cancellation.Token);
        await HandShake();
        _listenerTask = Task.Run(ListenOnMessages, _cancellation.Token).ContinueWith(StopOnException);
        _controlTask = Task.Run(Control, _cancellation.Token).ContinueWith(StopOnException);
        await _ctrlChannel.Writer.WriteAsync(new NewPeer(this), _cancellation.Token);
    }

    public async Task Stop()
    {
        _cancellation.Cancel();
        _listenerTask = null;
        _controlTask = null;
        _client.Close();
        await _ctrlChannel.Writer.WriteAsync(new CloseConnection(Peer, _piecesToDownload), _cancellation.Token);

    }

    private async Task StopOnException(Task task)
    {
        if (task.IsFaulted)
        {
            await Stop();
            return;
        }
        await task;
    }

    private async Task ListenOnMessages()
    {
        var stream = _client.GetStream();
        while (true)
        {
            var lenBuf = new byte[4];
            await stream.ReadExactlyAsync(lenBuf, 0, 4, _cancellation.Token);
            var len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf));
            var msgBuf = new byte[4 + len];
            lenBuf.CopyTo(msgBuf, 0);
            await stream.ReadExactlyAsync(msgBuf, 4, len, _cancellation.Token);
            var msg = PeerMessageParser.Parse(msgBuf);
            if (msg is null) continue;
            await HandleMessage(msg);
        }
    }

    private async Task SendMessages(List<IPeerMessage> messages)
    {
        var stream = _client.GetStream();
        var buf = new List<Byte>();
        lock (_queueLock)
        {
            _queuedMsgs.AddRange(messages);
            var unsentMsgs = new List<IPeerMessage>();
            foreach (var message in _queuedMsgs)
            {
                switch (message)
                {
                    case Interested i:
                        _amInterested = true;
                        break;
                    case NotInterested ni:
                        _amInterested = false;
                        break;
                    case Choke c:
                        _isChoked = true;
                        break;
                    case Unchoke uc:
                        _isChoked = false;
                        break;
                    case Request rc:
                        if (_amChoked)
                        {
                            unsentMsgs.Add(message);
                            continue;
                        }
                        break;
                }
                buf.AddRange(message.ToBytes());
            }
            _queuedMsgs.Clear();
            _queuedMsgs.AddRange(unsentMsgs);
        }
        await stream.WriteAsync(buf.ToArray(), _cancellation.Token);
    }

    private async Task Control()
    {
        while (!_ctrlChannel.Reader.Completion.IsCompleted)
        {
            var msg = await PeerChannel.Reader.ReadAsync(_cancellation.Token);
            switch (msg)
            {
                case SupplyPieces sp: {
                    foreach (var pieceIdx in sp.Pieces)
                    {
                        await RequestPiece(pieceIdx);
                    }
                    break;
                }
                case HavePiece hp: {
                    await SendMessages([new Have((UInt32)hp.Idx)]);
                    break;
                }
            }
        }
    }

    private async Task RequestPiece(int piece)
    {
        var size = PieceStorage.GetPieceLength(Torrent, piece);
        var msgs = new List<IPeerMessage>();
        for (int off = 0; off < size; off += CHUNK_SIZE)
        {
            // Account for the last chunk in a file being possibly smaller.
            var chunkLen = Math.Min(size - off, CHUNK_SIZE);
            var msg = new Request((UInt32)piece, (UInt32)off, (UInt32)chunkLen);
            msgs.Add(msg);
        }
        await SendMessages(msgs);
    }

    private async Task HandleMessage(IPeerMessage msg)
    {
        switch (msg)
        {
            case KeepAlive ka: {
                break;
            }
            case Choke c: {
                _amChoked = true;
                break;
            }
            case Unchoke uc: {
                _amChoked = false;
                // Attempt to clear queued request messages.
                await SendMessages(new());
                break;
            }
            case Interested i: {
                _isInterested = true;
                break;
            }
            case NotInterested ni: {
                _isInterested = false;
                break;
            }
            case Have h: {
                PeerHas[(int)h.Piece] = true;
                await _ctrlChannel.Writer.WriteAsync(new HavePiece(Peer, (int)h.Piece), _cancellation.Token);
                break;
            }
            case Bitfield b: {
                b.Data.Length = Torrent.NumberOfPieces;
                PeerHas = b.Data;
                break;
            }
            case Request r: {
                // TODO: implement
                break;
            }
            case Piece p: {
                await _ctrlChannel.Writer.WriteAsync(new DownloadedChunk(Peer, p.Chunk), _cancellation.Token);
                break;
            }
            case Cancel cancel: {
                // TODO: implement
                break;
            }
        }
    }

    private async Task HandShake()
    {
        var stream = _client.GetStream();
        string protocolName = "BitTorrent protocol";
        var len = protocolName.Length;
        byte[] reserved = new byte[8];

        List<byte> buffer = new();
        buffer.Add((byte)protocolName.Length);
        buffer.AddRange(Encoding.ASCII.GetBytes(protocolName));
        buffer.AddRange(reserved);
        buffer.AddRange(Torrent.OriginalInfoHashBytes);
        buffer.AddRange(PeerId);

        await stream.WriteAsync(buffer.ToArray(), _cancellation.Token);
        await stream.FlushAsync(_cancellation.Token);

        var messageBuf = new Byte[49 + len];
        var handshake = new Memory<byte>(messageBuf);
        await stream.ReadExactlyAsync(messageBuf, 0, messageBuf.Length, _cancellation.Token);

        if (handshake.Span[0] != len)
            throw new HandShakeException("Recieved invalid protocol name length from peer.");
        var protocolRecieved = Encoding.ASCII.GetString(handshake.Slice(1, len).ToArray());
        if (protocolRecieved != protocolName)
            throw new HandShakeException($"Recieved invalid protocol name from peer: {protocolRecieved}");
        // Skip checking reserved bytes (20..27)
        if (!handshake.Slice(1 + len + 8, 20).ToArray().SequenceEqual(Torrent.OriginalInfoHashBytes))
            throw new HandShakeException("Info hash recieved from peer differs from the one sent.");
        if (Peer.PeerId is not null && !handshake.Slice(1 + len + 8 + 20, 20).ToArray().SequenceEqual(Peer.PeerId))
            throw new HandShakeException("Peer's id mismatched.");

        // TODO: send bitfield and interested if the whole torrent is not downloaded yet
    }

    ~PeerConnection()
    {
        Console.WriteLine($"Disposed peer: {Peer}");
        _client.Close();
        PeerChannel.Writer.Complete();
        _cancellation.Cancel();
        _listenerTask?.Dispose();
        _controlTask?.Dispose();
    }
}
