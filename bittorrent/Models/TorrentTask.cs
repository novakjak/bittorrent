using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BencodeNET.Objects;
using BencodeNET.Parsing;

using bittorrent.Core;

using BT = BencodeNET.Torrents;
using Data = bittorrent.Core.Data;

namespace bittorrent.Models;

public class TorrentTask
{
    public BT.Torrent Torrent { get; }
    public byte[] HashId => Torrent.OriginalInfoHashBytes;
    public int PeerCount => _connections.Count();
    public Channel<IPeerCtrlMsg> CtrlChannel { get; } = Channel.CreateUnbounded<IPeerCtrlMsg>();
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; internal set; } = 0;
    public int DownloadedValid { get; internal set; } = 0;
    public string PeerId { get; private set; } = Util.GenerateRandomString(20);
    public BitArray DownloadedPieces { get; private set; }
    public bool IsCompleted => DownloadedPieces.HasAllSet();

    public event EventHandler<(int pieceIdx, double completion)>? DownloadedPiece;
    public event EventHandler<int>? PeerCountChanged;

    private Task? _thread;
    private readonly List<PeerConnection> _connections = new();
    internal Dictionary<int, List<Data.Chunk>> _downloadingPieces = new();
    internal PieceStorage _storage;
    internal CancellationTokenSource _cancellation = new();
    private TrackerAnnouncer _announcer;

    public TorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
        DownloadedPieces = new BitArray(Torrent.NumberOfPieces);
        _storage = new PieceStorage(torrent);
        _announcer = new TrackerAnnouncer(this);
        _announcer.ReceivedPeers += (_, peers) => AddPeers(peers);
    }

    public void Start()
    {
        _announcer.Start();
        _thread ??= Task
            .Run(this.ManagePeers, _cancellation.Token)
            .ContinueWith(LogException);
    }

    public async Task Stop()
    {
        await _announcer.Stop();
        _thread = null;
        foreach (var conn in _connections)
            await conn.PeerChannel.Writer.WriteAsync(new FinishConnection(), _cancellation.Token);
        _connections.Clear();
        RaisePeerCountChanged();
        _cancellation.Cancel();
    }

    public void AddPeer(INetworkClient conn, Peer peer)
    {
        Task.Run(async () =>
        {
            var peerChannel = Channel.CreateUnbounded<ITaskCtrlMsg>();
            var peerId = Encoding.ASCII.GetBytes(PeerId);
            var peerTokenSource = new CancellationTokenSource();
            await PeerConnection.CreateAndFinishHandshakeAsync(
                conn, peer, Torrent, peerId, CtrlChannel,
                peerChannel, DownloadedPieces, peerTokenSource);
        }).ContinueWith(LogException);
    }

    public void RaisePeerCountChanged()
        => PeerCountChanged?.Invoke(this, PeerCount);

    private async Task ManagePeers()
    {
        // Main control loop
        var rx = CtrlChannel.Reader;
        while (await rx.WaitToReadAsync(_cancellation.Token))
        {
            try
            {
                var message = await rx.ReadAsync(_cancellation.Token);
                await message.Handle(this, _connections);
            }
            catch (Exception e)
            {
                Logger.Error($"Handling peer failed: {e.Message}");
            }
        }
    }

    internal async Task SupplyPiecesToPeer(PeerConnection pc, int count)
    {
        var pieces = RandomNotDownloadedPieces(pc.PeerHas, count);
        if (pieces.Count() > 0)
        {
            var msg = new SupplyPieces(pieces);
            await pc.PeerChannel.Writer.WriteAsync(msg, _cancellation.Token);
        }
    }

    private IEnumerable<int> RandomNotDownloadedPieces(BitArray peerHas, int count)
    {
        var availableToDownload = new BitArray(peerHas);

        // Enumerate all pieces that have yet not been fully downloaded.
        var notDownloaded = new BitArray(DownloadedPieces);
        notDownloaded.Not();
        availableToDownload.And(notDownloaded);

        // Remove all pieces that are being downloaded right now unless
        // we're close to finishing, then once we get to the _endgame_
        // we try to finish the torrent as quickly as possible, even
        // though we might download some redundant data.
        var endgameThreshold = Torrent.TotalSize * 9 / 10;
        if (DownloadedValid < endgameThreshold)
        {
            foreach (KeyValuePair<int, List<Data.Chunk>> kvp in _downloadingPieces)
                availableToDownload[kvp.Key] = false;
        }

        var pieces = availableToDownload.OfType<bool>()
            .Index()
            .Where(p => p.Item2)
            .Select(p => p.Item1)
            .ToArray();
        var rng = new Random();
        rng.Shuffle(pieces);
        return pieces.Take(count);
    }

    private void AddPeers(IEnumerable<Peer> peers)
    {
        foreach (var peer in peers)
        {
            var peerChannel = Channel.CreateUnbounded<ITaskCtrlMsg>();
            var peerTokenSource = new CancellationTokenSource();
            _ = Task.Run(async () => await PeerConnection.CreateAsync(
                peer, Torrent, Encoding.UTF8.GetBytes(PeerId),
                CtrlChannel, peerChannel,
                DownloadedPieces, peerTokenSource
            ));
        }
    }

    internal async Task AnnounceDownloadedPiece(Data.Piece piece, Peer fromPeer)
    {
        var args = (piece.Idx, (double)DownloadedValid / (double)Torrent.TotalSize);
        DownloadedPiece?.Invoke(this, args);
        DownloadedPieces[(int)piece.Idx] = true;

        foreach (var conn in _connections)
            await conn.PeerChannel.Writer
                .WriteAsync(new HavePiece(piece.Idx), _cancellation.Token);
    }

    private static async Task LogException(Task task)
    {
        if (task.IsFaulted)
        {
            Logger.Warn(task.Exception.Message);
            return;
        }
        await task;
    }

    ~TorrentTask()
    {
        Logger.Debug("Disposed task");
        _cancellation.Cancel();
        CtrlChannel.Writer.TryComplete();
        _thread?.Dispose();
    }
}

file static class CtrlMessageExtensions
{
    internal static async Task Handle(this NewPeer msg, TorrentTask task, List<PeerConnection> connections)
    {
        if (connections.Select(pc => pc.Peer).Contains(msg.Peer))
            return;
        connections.Add(msg.PeerConnection);
        Logger.Debug($"Added Connection: {msg.Peer}");
        task.RaisePeerCountChanged();
    }
    internal static async Task Handle(this RequestPieces msg, TorrentTask task, List<PeerConnection> connections)
    {
        var conn = connections.First(c => c.Peer == msg.Peer);
        await task.SupplyPiecesToPeer(conn, msg.Count);
    }
    internal static async Task Handle(this RequestChunk msg, TorrentTask task, List<PeerConnection> connections)
    {
        var conn = connections.First(c => c.Peer == msg.Peer);
        var request = msg.Request;
        if (request.Idx >= task.DownloadedPieces.Count || !task.DownloadedPieces[(int)request.Idx])
            return;
        var piece = await task._storage.GetPieceAsync((int)request.Idx);
        var buf = new byte[request.Length];
        Array.Copy(piece.Data, (int)request.Begin, buf, 0, (int)request.Length);
        var chunk = new Data.Chunk(request.Idx, request.Begin, buf);
        await conn.PeerChannel.Writer.WriteAsync(new SupplyChunk(chunk), task._cancellation.Token);
    }
    internal static async Task Handle(this DownloadedChunk msg, TorrentTask task, List<PeerConnection> connections)
    {
        if (task.DownloadedPieces[(int)msg.Chunk.Idx])
            return;
        if (!task._downloadingPieces.ContainsKey((int)msg.Chunk.Idx))
            task._downloadingPieces.Add((int)msg.Chunk.Idx, new List<Data.Chunk>());

        var chunks = task._downloadingPieces[(int)msg.Chunk.Idx];
        if (chunks.Any(c => c.Begin == msg.Chunk.Begin))
            return;
        chunks.Add(msg.Chunk);
        var pieceLen = task._storage.GetPieceLength((int)msg.Chunk.Idx);
        var chunksInAPiece = pieceLen / PeerConnection.CHUNK_SIZE;
        if (pieceLen % PeerConnection.CHUNK_SIZE > 0)
            chunksInAPiece++;
        task.Downloaded += msg.Chunk.Data.Length;

        if (chunks.Count() != chunksInAPiece)
            return;

        // Store piece to disk.
        task._downloadingPieces.Remove((int)msg.Chunk.Idx);
        var pieceBuf = new byte[pieceLen];
        foreach (var chunk in chunks)
            chunk.Data.CopyTo(pieceBuf, chunk.Begin);
        var hash = System.Security.Cryptography.SHA1.HashData(pieceBuf);
        var pieceHash = new ArraySegment<byte>(task.Torrent.Pieces, (int)msg.Chunk.Idx * 20, 20);
        if (!hash.SequenceEqual(pieceHash))
            return;

        var piece = new Data.Piece((int)msg.Chunk.Idx, pieceBuf);
        try
        {
            await task._storage.StorePieceAsync(piece);
        }
        catch (IOException e)
        {
            Logger.Warn($"Could not write to file: {e.Message}");
            return;
        }
        task.DownloadedValid += pieceBuf.Length;

        await task.AnnounceDownloadedPiece(piece, msg.Peer);

        var peer = connections.First(c => c.Peer == msg.Peer);
    }
    internal static async Task Handle(this CloseConnection msg, TorrentTask task, List<PeerConnection> connections)
    {
        foreach (var idx in msg.WasDownloading)
            task._downloadingPieces.Remove(idx);
        connections.RemoveAll(conn => conn.Peer == msg.Peer);
        task.RaisePeerCountChanged();
        Logger.Debug($"closed connection with {msg.Peer}");
    }
    internal static async Task Handle(this IPeerCtrlMsg msg, TorrentTask task, List<PeerConnection> connections)
    {
        switch (msg)
        {
            case NewPeer np:
                await np.Handle(task, connections);
                break;
            case RequestPieces rp:
                await rp.Handle(task, connections);
                break;
            case DownloadedChunk dc:
                await dc.Handle(task, connections);
                break;
            case CloseConnection cc:
                await cc.Handle(task, connections);
                break;
            default:
                throw new NotImplementedException($"Handling of peer message {msg} is not implemented");
        }
    }
}
