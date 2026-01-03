using System;
using System.Linq;
using System.Text;
using System.IO;
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
    public const int MAX_DOWNLOADING_PIECES = 20;

    public Peer Peer { get; }
    public Torrent Torrent { get; }
    public byte[] PeerId { get; }
    public Channel<ITaskCtrlMsg> PeerChannel { get; }
    public BitArray PeerHas { get; private set; }
    public BitArray Have { get; set; }
    public bool AmChoked { get; private set; } = true;
    public bool IsChoked { get; private set; } = true;
    public bool AmInterested { get; private set; } = false;
    public bool IsInterested { get; private set; } = false;
    public bool IsStarted { get => _listenerTask is not null && _controlTask is not null; }

    private INetworkClient _client = new NetworkClient();
    private List<IPeerMessage> _queuedMsgs = new();
    private Lock _queueLock = new();
    private List<int> _piecesToDownload = new();
    private Task? _listenerTask;
    private Task? _controlTask;
    private CancellationTokenSource _cancellation;
    private Channel<IPeerCtrlMsg> _ctrlChannel;


    private PeerConnection(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<IPeerCtrlMsg> ctrlChannel,
        Channel<ITaskCtrlMsg> peerChannel,
        BitArray have, CancellationTokenSource cancellationSource
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
        _cancellation = cancellationSource;
    }

    public static async Task<PeerConnection> CreateAsync(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<IPeerCtrlMsg> ctrlChannel,
        Channel<ITaskCtrlMsg> peerChannel,
        BitArray have,
        CancellationTokenSource cancellationSource
    )
    {
        return await PeerConnection.CreateWithClientAsync(
            new NetworkClient(),
            peer, torrent, peerId,
            ctrlChannel,
            peerChannel,
            have,
            cancellationSource
        );
    }

    public static async Task<PeerConnection> CreateWithClientAsync(
        INetworkClient client, Peer peer, Torrent torrent, byte[] peerId,
        Channel<IPeerCtrlMsg> ctrlChannel,
        Channel<ITaskCtrlMsg> peerChannel,
        BitArray have, CancellationTokenSource cancellationSource
    )
    {
        var pc = new PeerConnection(peer, torrent, peerId, ctrlChannel, peerChannel, have, cancellationSource);
        pc._client = client;
        await pc._client.ConnectAsync(pc.Peer.Ip, pc.Peer.Port, pc._cancellation.Token);
        await pc.PerformHandshake();
        pc.Start();
        return pc;
    }

    public static async Task<PeerConnection> CreateAndFinishHandshakeAsync(
        INetworkClient client, Peer peer, Torrent torrent, byte[] peerId,
        Channel<IPeerCtrlMsg> ctrlChannel,
        Channel<ITaskCtrlMsg> peerChannel,
        BitArray have,
        CancellationTokenSource cancellationSource
    )
    {
        var pc = new PeerConnection(peer, torrent, peerId, ctrlChannel, peerChannel, have, cancellationSource);
        pc._client = client;
        await pc.SendHandshake();
        pc.Start();
        return pc;
    }

    public void Start()
    {
        _listenerTask = Task.Run(ListenOnMessages, _cancellation.Token).ContinueWith(StopOnException);
        _controlTask = Task.Run(Control, _cancellation.Token).ContinueWith(StopOnException);
        _ctrlChannel.Writer
            .WriteAsync(new NewPeer(this), _cancellation.Token)
            .AsTask()
            .ContinueWith(StopOnException);
    }

    public async Task Stop()
    {
        Console.WriteLine("stopping");
        _listenerTask = null;
        _controlTask = null;
        _client.Close();
        try
        {
            await _ctrlChannel.Writer.WriteAsync(new CloseConnection(Peer, _piecesToDownload), _cancellation.Token);
        }
        catch { } // Do nothing if it's not possible to write
        PeerChannel.Writer.TryComplete();
        _cancellation.Cancel();
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
            var len = (int)Util.FromNetworkOrderBytes(lenBuf, 0);
            var msgBuf = new byte[4 + len];
            lenBuf.CopyTo(msgBuf, 0);
            await stream.ReadExactlyAsync(msgBuf, 4, len, _cancellation.Token);
            var msg = PeerMessageParser.Parse(msgBuf);
            if (msg is null) continue;
            Console.WriteLine(msg);
            await HandleMessage(msg);
        }
    }

    public async Task SendMessages(List<IPeerMessage> messages)
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
                        AmInterested = true;
                        break;
                    case NotInterested ni:
                        AmInterested = false;
                        break;
                    case Choke c:
                        IsChoked = true;
                        break;
                    case Unchoke uc:
                        IsChoked = false;
                        break;
                    case Request rc:
                        if (AmChoked)
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
        while (await PeerChannel.Reader.WaitToReadAsync(_cancellation.Token))
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
                case FinishConnection fc: {
                    await Stop();
                    return;
                }
            }
        }
        await Stop();
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
                AmChoked = true;
                break;
            }
            case Unchoke uc: {
                AmChoked = false;
                // Attempt to clear queued request messages.
                if (_queuedMsgs.Count > 0)
                {
                    await SendMessages(new());
                }
                break;
            }
            case Interested i: {
                IsInterested = true;
                break;
            }
            case NotInterested ni: {
                IsInterested = false;
                break;
            }
            case Have h: {
                if (h.Piece >= PeerHas.Length)
                {
                    break;
                }
                PeerHas[(int)h.Piece] = true;
                await _ctrlChannel.Writer.WriteAsync(new HavePiece(Peer, (int)h.Piece), _cancellation.Token);
                break;
            }
            case Bitfield b: {
                b.Data.Length = Torrent.NumberOfPieces;
                PeerHas = b.Data;
                if (_piecesToDownload.Count() < PeerConnection.MAX_DOWNLOADING_PIECES)
                {
                    await _ctrlChannel.Writer.WriteAsync(
                        new RequestPieces(Peer, PeerConnection.MAX_DOWNLOADING_PIECES - _piecesToDownload.Count()),
                        _cancellation.Token
                    );
                }
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

    private async Task PerformHandshake()
    {
        await SendHandshake();
        await ReceiveHandshake();
        // TODO: send bitfield and interested if the whole torrent is not downloaded yet
    }

    private async Task SendHandshake()
    {
        var stream = _client.GetStream();
        var handshake = new Handshake(Torrent.OriginalInfoHashBytes, PeerId);
        await stream.WriteAsync(handshake.ToBytes(), _cancellation.Token);
        await stream.FlushAsync(_cancellation.Token);
    }

    private async Task ReceiveHandshake()
    {
        var messageBuf = new Byte[Handshake.MessageLength];
        var messageMem = new Memory<byte>(messageBuf);
        await _client
            .GetStream()
            .ReadExactlyAsync(messageBuf, 0, messageBuf.Length, _cancellation.Token);
        var handshake = Handshake.Parse(messageMem);

        if (!handshake.InfoHash.SequenceEqual(Torrent.OriginalInfoHashBytes))
            throw new HandShakeException("Info hash received from peer differs from the one sent.");
        if (Peer.PeerId is not null && !handshake.PeerId.SequenceEqual(Peer.PeerId))
            throw new HandShakeException("Peer's id mismatched.");
    }

    ~PeerConnection()
    {
        Console.WriteLine($"Disposed peer: {Peer}");
        _client.Dispose();
        PeerChannel.Writer.TryComplete();
        _cancellation.Cancel();
        _listenerTask?.Dispose();
        _controlTask?.Dispose();
    }
}
