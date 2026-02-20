using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BencodeNET.Torrents;

using BitAvalanche.Core;

namespace BitAvalanche.Models;

public class PeerConnection
{
    public const int CHUNK_SIZE = 16384; // 2^14 aka 16 kiB
    public const int MAXIMUM_CHUNK_SIZE = 131072; // 2^17 aka 128 kiB
    public const int MAX_DOWNLOADING_PIECES = 20;
    public const int TIMEOUT = 2 * 60 * 1000;

    public ITorrentTask Parent { get; }
    public Peer Peer { get; }
    public Channel<ITaskCtrlMsg> PeerChannel { get; }
    public BitArray PeerHas { get; private set; }
    public bool AmChoked { get; private set; } = true;
    public bool IsChoked { get; private set; } = true;
    public bool AmInterested { get; private set; } = false;
    public bool IsInterested { get; private set; } = false;
    public bool IsStarted { get => _listenerTask is not null && _controlTask is not null; }

    private INetworkClient _client = new NetworkClient();
    private readonly List<IPeerMessage> _queuedMsgs = new();
    private readonly Lock _queueLock = new();
    private readonly List<int> _piecesToDownload = new();
    private readonly List<Request> _requestedChunks = new();
    private readonly List<Request> _peerRequestedChunks = new();
    private Task? _listenerTask;
    private Task? _controlTask;
    private readonly CancellationTokenSource _cancellation = new();


    private PeerConnection(
        Peer peer, ITorrentTask task,
        Channel<ITaskCtrlMsg> peerChannel
    )
    {
        Peer = peer;
        Parent = task;
        PeerChannel = peerChannel;
        PeerHas = new BitArray(Parent.Torrent.NumberOfPieces);
    }

    public static async Task<PeerConnection> CreateAsync(
        Peer peer, ITorrentTask task,
        Channel<ITaskCtrlMsg> peerChannel
    )
    {
        return await PeerConnection.CreateWithClientAsync(
            new NetworkClient(),
            peer, task,
            peerChannel
        );
    }

    public static async Task<PeerConnection> CreateWithClientAsync(
        INetworkClient client, Peer peer, ITorrentTask task,
        Channel<ITaskCtrlMsg> peerChannel
    )
    {
        var pc = new PeerConnection(peer, task, peerChannel);
        pc._client = client;
        await pc._client.ConnectAsync(pc.Peer.Ip, pc.Peer.Port, pc._cancellation.Token);
        await pc.PerformHandshake();
        await pc.Start();
        return pc;
    }

    public static async Task<PeerConnection> CreateAndFinishHandshakeAsync(
        INetworkClient client, Peer peer, ITorrentTask task,
        Channel<ITaskCtrlMsg> peerChannel
    )
    {
        var pc = new PeerConnection(peer, task, peerChannel);
        pc._client = client;
        await pc.SendHandshake();
        await pc.Start();
        return pc;
    }

    public async Task Start()
    {
        _listenerTask = Task.Run(ListenOnMessages, _cancellation.Token).ContinueWith(StopOnException);
        _controlTask = Task.Run(Control, _cancellation.Token).ContinueWith(StopOnException);
        await Parent.CtrlChannel.Writer
            .WriteAsync(new NewPeer(this), _cancellation.Token)
            .AsTask()
            .ContinueWith(StopOnException);
    }

    public void Stop()
    {
        _listenerTask = null;
        _controlTask = null;
        _client.Dispose();
        try
        {
            var toDownload = new List<int>();
            ITaskCtrlMsg? msg;
            while (PeerChannel.Reader.TryRead(out msg))
            {
                if (msg is SupplyPieces pcs)
                    toDownload.AddRange(pcs.Pieces);
            }
            List<int> reclaimed = [.. _piecesToDownload, .. toDownload];
            if (reclaimed.Count() > 0)
                Parent.CtrlChannel.Writer.TryWrite(new ReclaimPieces(Peer, reclaimed));
            Parent.CtrlChannel.Writer.TryWrite(new CloseConnection(Peer));
        }
        catch { } // Do nothing if it's not possible to write
        _piecesToDownload.Clear();
        PeerChannel.Writer.TryComplete();
        _cancellation.Cancel();
    }

    private async Task StopOnException(Task task)
    {
        if (task.IsFaulted)
        {
            Stop();
            return;
        }
        await task;
    }

    private async Task ListenOnMessages()
    {
        var stream = _client.GetStream();
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        while (true)
        {
            timeout.CancelAfter(PeerConnection.TIMEOUT); // Reset timeout
            var lenBuf = new byte[4];
            await stream.ReadExactlyAsync(lenBuf, 0, 4, timeout.Token);
            var len = (int)Util.FromNetworkOrderBytes(lenBuf, 0);
            var msgBuf = new byte[4 + len];
            lenBuf.CopyTo(msgBuf, 0);
            await stream.ReadExactlyAsync(msgBuf, 4, len, timeout.Token);
            var msg = PeerMessageParser.Parse(msgBuf);
            if (msg is null) continue;
            await HandleMessage(msg);
        }
    }

    public async Task SendMessages(IEnumerable<IPeerMessage> messages)
    {
        var stream = _client.GetStream();
        var buf = new List<Byte>();
        int uploaded = 0;
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
                    case Piece p:
                        uploaded += p.Chunk.Data.Length;
                        break;
                }
                buf.AddRange(message.ToBytes());
            }
            _queuedMsgs.Clear();
            _queuedMsgs.AddRange(unsentMsgs);
        }
        await stream.WriteAsync(buf.ToArray(), _cancellation.Token);
        if (uploaded > 0)
        {
            await Parent.CtrlChannel.Writer
                .WriteAsync(new Uploaded(Peer, uploaded));
        }
    }

    private async Task Control()
    {
        await AskForPieces();
        while (await PeerChannel.Reader.WaitToReadAsync(_cancellation.Token))
        {
            var msg = await PeerChannel.Reader.ReadAsync(_cancellation.Token);
            switch (msg)
            {
                case SupplyPieces sp:
                    if (sp.Pieces.Count() > 0)
                        await SendInterested();
                    foreach (var pieceIdx in sp.Pieces)
                    {
                        if (_piecesToDownload.Contains(pieceIdx))
                            continue;
                        _piecesToDownload.Add(pieceIdx);
                        await RequestPiece(pieceIdx);
                    }
                    break;
                case SupplyChunk sc:
                {
                    Predicate<Request> isRequested = c =>
                        c.Idx == sc.Chunk.Idx
                        && c.Begin == sc.Chunk.Begin
                        && c.Length == sc.Chunk.Data.Count();
                    var requested = _peerRequestedChunks.RemoveAll(isRequested);
                    // Skip sending if chunk request was cancelled
                    if (requested == 0)
                        break;
                    await SendMessages([new Piece(sc.Chunk)]);
                    break;
                }
                case HavePiece hp:
                {
                    var msgs = new List<IPeerMessage> { new Have((UInt32)hp.Idx) };
                    foreach (var c in _requestedChunks)
                    {
                        if (hp.Idx == c.Idx)
                            msgs.Add(new Cancel(c.Idx, c.Begin, c.Length));
                    }
                    await SendMessages(msgs);

                    _requestedChunks.RemoveAll(c => c.Idx == hp.Idx);
                    var cancelledCount = _piecesToDownload.RemoveAll(p => p == hp.Idx);
                    await Parent.CtrlChannel.Writer.WriteAsync(new RequestPieces(Peer, cancelledCount), _cancellation.Token);
                    break;
                }
                case FinishConnection fc:
                    Stop();
                    return;
            }
        }
        Stop();
    }

    private async Task RequestPiece(int piece)
    {
        var rng = new Random();
        var size = PieceStorage.GetPieceLength(Parent.Torrent, piece);
        var msgs = new List<IPeerMessage>();
        for (int off = 0; off < size; off += CHUNK_SIZE)
        {
            // Account for the last chunk in a file being possibly smaller.
            var chunkLen = Math.Min(size - off, CHUNK_SIZE);
            var msg = new Request((UInt32)piece, (UInt32)off, (UInt32)chunkLen);
            _requestedChunks.Add(msg);
            msgs.Add(msg);
        }
        // If we're in endgame randomizing the order the chunks of a piece will
        // be downloaded in will give us better chances at not downloading
        // redundant chunks.
        var arr = msgs.ToArray();
        rng.Shuffle(arr);
        await SendMessages(arr);
    }

    private async Task HandleMessage(IPeerMessage msg)
    {
        switch (msg)
        {
            case KeepAlive ka:
                break;
            case Choke c:
                // Make pieces available to download for other peers
                await Parent.CtrlChannel.Writer
                    .WriteAsync(new ReclaimPieces(Peer, new List<int>(_piecesToDownload)));
                _piecesToDownload.Clear();
                AmChoked = true;
                break;
            case Unchoke uc:
                AmChoked = false;
                // Attempt to clear queued request messages.
                if (_queuedMsgs.Count > 0)
                {
                    await SendMessages(new List<IPeerMessage>());
                }
                await AskForPieces();
                break;
            case Interested i:
                if (IsChoked)
                    await SendMessages([new Unchoke()]);
                IsInterested = true;
                break;
            case NotInterested ni:
                _peerRequestedChunks.Clear();
                IsInterested = false;
                break;
            case Have h:
                if (h.Piece >= PeerHas.Length)
                    break;
                PeerHas[(int)h.Piece] = true;
                await AskForPieces();
                break;
            case Bitfield b:
                b.Data.Length = Parent.Torrent.NumberOfPieces;
                PeerHas = b.Data;
                await AskForPieces();
                break;
            case Request r:
                if (r.Length > PeerConnection.MAXIMUM_CHUNK_SIZE)
                    break;
                if (_peerRequestedChunks.Any(c => c.Equals(r)))
                    break;
                _peerRequestedChunks.Add(r);
                await Parent.CtrlChannel.Writer.WriteAsync(new RequestChunk(Peer, r), _cancellation.Token);
                break;
            case Piece p:
                _requestedChunks.RemoveAll(c => c.Idx == p.Chunk.Idx && c.Begin == p.Chunk.Begin);
                await Parent.CtrlChannel.Writer.WriteAsync(new DownloadedChunk(Peer, p.Chunk), _cancellation.Token);
                break;
            case Cancel cancel:
                _peerRequestedChunks.RemoveAll(c => c.Equals(cancel));
                break;
        }
    }

    private async Task AskForPieces()
    {
        if (_piecesToDownload.Count() >= PeerConnection.MAX_DOWNLOADING_PIECES)
            return;
        await Parent.CtrlChannel.Writer.WriteAsync(
            new RequestPieces(Peer, PeerConnection.MAX_DOWNLOADING_PIECES - _piecesToDownload.Count()),
            _cancellation.Token
        );
    }

    private async Task PerformHandshake()
    {
        await SendHandshake();
        await ReceiveHandshake();

        if (Parent.DownloadedPieces.HasAnySet())
            await SendMessages([new Bitfield(Parent.DownloadedPieces)]);
    }

    private async Task SendInterested()
    {
        if (!AmInterested && !Parent.IsCompleted)
            await SendMessages([new Interested()]);
    }

    private async Task SendHandshake()
    {
        var stream = _client.GetStream();
        var handshake = new Handshake(
            Parent.Torrent.OriginalInfoHashBytes,
            Encoding.UTF8.GetBytes(Parent.PeerId)
        );
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

        if (!handshake.InfoHash.SequenceEqual(Parent.Torrent.OriginalInfoHashBytes))
            throw new HandShakeException("Info hash received from peer differs from the one sent.");
        if (Peer.PeerId is not null && !handshake.PeerId.SequenceEqual(Peer.PeerId))
            throw new HandShakeException("Peer's id mismatched.");
    }

    ~PeerConnection()
    {
        Stop();
    }
}
