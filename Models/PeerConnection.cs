using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using BencodeNET.Torrents;

namespace bittorrent.Models;

public class PeerConnection
{
    public Peer Peer { get; }
    public Torrent Torrent { get; }
    public byte[] PeerId { get; }
    public Channel<ICtrlMsg> PeerChannel { get; }
    public BitArray PeerHas { get; private set; }

    private bool _amChoked = true;
    private bool _isChoked = true;
    private bool _amInterested = false;
    private bool _isInterested = false;
    private List<IPeerMessage> _queuedMsgs = new();
    private List<int> _piecesToDownload = new();
    private Task? _listenerTask;
    private Task? _downloaderTask;

    private readonly Channel<ICtrlMsg> _ctrlChannel;
    private readonly TcpClient _client;

    private PeerConnection(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<ICtrlMsg> ctrlChannel,
        Channel<ICtrlMsg> peerChannel
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
        _ctrlChannel = ctrlChannel;
        _client = new TcpClient();
    }

    public async Task ListenOnMessages()
    {
        var stream = _client.GetStream();
        while (true)
        {
            var lenBuf = new byte[4];
            await stream.ReadExactlyAsync(lenBuf, 0, 4);
            var len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf));
            var msgBuf = new byte[4 + len];
            lenBuf.CopyTo(msgBuf, 0);
            await stream.ReadExactlyAsync(msgBuf, 4, len);
            var msg = PeerMessageParser.Parse(msgBuf);
            if (msg is null) continue;
            await HandleMessage(msg);
        }
    }

    public async Task SendMessages(List<IPeerMessage> messages)
    {
        _queuedMsgs.AddRange(messages);
        if (_amChoked)
            return;
        var stream = _client.GetStream();
        var buf = new List<Byte>();
        foreach (var message in _queuedMsgs)
        {
            buf.AddRange(message.ToBytes());
            switch (message)
            {
                case Interested i:
                    _isInterested = true;
                    break;
                case NotInterested ni:
                    _isInterested = false;
                    break;
                case Choke c:
                    _isChoked = true;
                    break;
                case Unchoke uc:
                    _isChoked = false;
                    break;
            }
        }
        await stream.WriteAsync(buf.ToArray());
    }

    async Task DownloadPieces()
    {
        while (!_ctrlChannel.Reader.Completion.IsCompleted)
        {
            if (_piecesToDownload.Count == 0)
            {
                Console.WriteLine("request pieces");
                await _ctrlChannel.Writer.WriteAsync(new RequestPieces(this));
                ICtrlMsg msg;
                while (true)
                {
                    msg = await PeerChannel.Reader.ReadAsync();
                    if (msg is SupplyPieces)
                        break;
                    // add back to the end of channel
                    await PeerChannel.Writer.WriteAsync(msg);
                }
                Console.WriteLine("got pieces");
                _piecesToDownload.AddRange((msg as SupplyPieces)!.Pieces);
                Console.WriteLine(_piecesToDownload.Count);
            }
            foreach (var pieceIdx in _piecesToDownload)
            {
                Console.WriteLine($"Downloading {pieceIdx}");
            }
        }
    }

    public static async Task<PeerConnection> CreateAsync(
        Peer peer, Torrent torrent, byte[] peerId,
        Channel<ICtrlMsg> ctrlChannel,
        Channel<ICtrlMsg> peerChannel
    )
    {
        var pc = new PeerConnection(peer, torrent, peerId, ctrlChannel, peerChannel);
        await pc._client.ConnectAsync(pc.Peer.Ip, pc.Peer.Port);
        await pc.HandShake();
        pc._listenerTask = Task.Run(pc.ListenOnMessages);
        return pc;
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
                await _ctrlChannel.Writer.WriteAsync(new HavePiece(Peer, (int)h.Piece));
                    break;
            }
            case Bitfield b: {
                b.Data.Length = Torrent.NumberOfPieces;
                PeerHas = b.Data;
                _downloaderTask = Task.Run(DownloadPieces);
                break;
            }
            case Request r: {
                // TODO: implement
                break;
            }
            case Piece p: {
                await _ctrlChannel.Writer.WriteAsync(new DownloadedChunk(Peer, p.Chunk));
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

        await stream.WriteAsync(buffer.ToArray());
        await stream.FlushAsync();

        var messageBuf = new Byte[49 + len];
        var handshake = new Memory<byte>(messageBuf);
        await stream.ReadExactlyAsync(messageBuf, 0, messageBuf.Length);

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
    }

    ~PeerConnection()
    {
        _client.Dispose();
        _listenerTask?.Dispose();
        _downloaderTask?.Dispose();
    }
}
