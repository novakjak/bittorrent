using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Security.Cryptography;
using BencodeNET.Parsing;
using BencodeNET.Objects;
using BT = BencodeNET.Torrents;

using bittorrent.Core;
using Data=bittorrent.Core.Data;

namespace bittorrent.Models;

public class TorrentTask
{
    public readonly BT.Torrent Torrent;
    public byte[] HashId { get => Torrent.OriginalInfoHashBytes; }
    public int PeerCount { get; private set; } = 0;
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; internal set; } = 0;
    public int DownloadedValid { get; internal set; } = 0;
    public string PeerId { get; private set; } = Util.GenerateRandomString(20);

    public event EventHandler<(int pieceIdx, double completion)>? DownloadedPiece;

    private Task? _thread;
    private List<Peer> _peers = new();
    private List<PeerConnection> _connections = new();
    private Channel<IPeerCtrlMsg> _mainCtrlChannel;
    internal BitArray _downloadedPieces;
    internal Dictionary<int, List<Data.Chunk>> _downloadingPieces = new();
    internal PieceStorage _storage;
    internal CancellationTokenSource _cancellation = new();

    private static readonly HttpClient client = new();

    public TorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
        _downloadedPieces = new BitArray(Torrent.NumberOfPieces);
        _mainCtrlChannel = Channel.CreateUnbounded<IPeerCtrlMsg>();
        _storage = new PieceStorage(torrent);
    }

    public void Start()
    {
        _thread ??= Task
            .Run(this.ManagePeers, _cancellation.Token)
            .ContinueWith(LogException);
    }

    public async Task Stop()
    {
        _thread = null;
        foreach (var conn in _connections)
            await conn.PeerChannel.Writer.WriteAsync(new FinishConnection(), _cancellation.Token);
        _cancellation.Cancel();
    }

    public void AddPeer(INetworkClient conn, Peer peer)
    {
        Task.Run(async () => {
            var peerChannel = Channel.CreateUnbounded<ITaskCtrlMsg>();
            var peerId = Encoding.ASCII.GetBytes(PeerId);
            var peerTokenSource = new CancellationTokenSource();
            await PeerConnection.CreateAndFinishHandshakeAsync(
                conn, peer, Torrent, peerId, _mainCtrlChannel,
                peerChannel, _downloadedPieces, peerTokenSource);
        }).ContinueWith(LogException);
    }

    private async Task ManagePeers()
    {
        await Announce();

        // Main control loop
        var rx = _mainCtrlChannel.Reader;
        while (await rx.WaitToReadAsync(_cancellation.Token))
        {
            var message = await rx.ReadAsync(_cancellation.Token);
            await message.Handle(this, _connections);
        }
    }

    internal async Task SupplyPiecesToPeer(PeerConnection pc, int count)
    {
        var pieces = RandomNotDownloadedPieces(pc.PeerHas, count);
        if (pieces.Count() > 0)
        {
            foreach (var piece in pieces)
            {
                _downloadingPieces.Add(piece, new List<Data.Chunk>());
            }
            var msg = new SupplyPieces(pieces);
            await pc.PeerChannel.Writer.WriteAsync(msg, _cancellation.Token);
        }
    }

    private IEnumerable<int> RandomNotDownloadedPieces(BitArray peerHas, int count)
    {
        var availableToDownload = new BitArray(peerHas);

        // Enumerate all pieces that have yet not been fully downloaded.
        var notDownloaded = new BitArray(_downloadedPieces);
        notDownloaded.Not();
        availableToDownload.And(notDownloaded);

        // Remove all pieces that are being downloaded right now.
        foreach (KeyValuePair<int, List<Data.Chunk>> kvp in _downloadingPieces)
        {
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

    private async Task Announce()
    {
        // TODO: use more trackers
        var peerId = Encoding.ASCII.GetBytes(PeerId);
        var tracker = Torrent.Trackers[0][0];
        var urlencoded = System.Web.HttpUtility.UrlEncode(Torrent.OriginalInfoHashBytes);
        var query = $"info_hash={urlencoded}";
        query += $"&peer_id={PeerId}";
        query += $"&port=8085";
        query += $"&downloaded={Downloaded}";
        query += $"&uploaded={Uploaded}";
        query += $"&left={Torrent.TotalSize - DownloadedValid}";

        var message = new HttpRequestMessage(HttpMethod.Get, $"{tracker}?{query}");

        BDictionary body;
        try
        {
            using var response = await client.SendAsync(message, _cancellation.Token);
            var parser = new BencodeParser();
            body = parser.Parse<BDictionary>(await response.Content.ReadAsStreamAsync(_cancellation.Token));
            BString failure;
            if ((failure = body.Get<BString>(new BString("failure reason"))) is not null)
            {
                Console.WriteLine($"Communication with tracker failed: {failure.ToString()}");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Could not get peers from tracker");
                return;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return;
        }
        foreach (var peer in ParsePeers(body["peers"]))
        {
            var peerChannel = Channel.CreateUnbounded<ITaskCtrlMsg>();
            var peerTokenSource = new CancellationTokenSource();
            _ = Task.Run(async () => await PeerConnection.CreateAsync(
                peer, Torrent, peerId,
                _mainCtrlChannel, peerChannel,
                _downloadedPieces, peerTokenSource
            ));
        }
    }

    internal async Task AnnounceDownloadedPiece(Data.Piece piece, Peer fromPeer)
    {
        var args = (piece.Idx, (double)DownloadedValid / (double)Torrent.TotalSize);
        DownloadedPiece?.Invoke(this, args);
        _downloadedPieces[(int)piece.Idx] = true;

        foreach (var conn in _connections)
            await conn.PeerChannel.Writer.WriteAsync(new HavePiece(piece.Idx), _cancellation.Token);
    }

    private static async Task LogException(Task task)
    {
        if (task.IsFaulted)
        {
            Console.WriteLine(task.Exception);
            return;
        }
        await task;
    }

    private static IEnumerable<Peer> ParsePeers(IBObject peers)
    {
        if (peers is BString s)
            return ParsePeers(s);
        if (peers is BList l)
            return ParsePeers(l);
        throw new ParseException("Peers object must be either a dictionary or a list.");
    }

    private static IEnumerable<Peer> ParsePeers(BString peers)
    {
        var res = new List<Peer>();
        var buf = peers.Value;
        for (int i = 0; i < peers.Length / 6; i++)
        {
            var addr = new IPAddress(buf.Slice(i * 6, 4).ToArray());
            var port = (int)(UInt16)IPAddress.NetworkToHostOrder(
                BitConverter.ToInt16(buf.Slice(i * 6 + 4, 2).ToArray(), 0)
            );
            res.Add(new Peer(addr, port, null));
        }
        return res;
    }

    private static IEnumerable<Peer> ParsePeers(BList peersList)
    {
        if (peersList is null) return new List<Peer>();

        List<Peer> peers = new();
        foreach (var peerDict in peersList.Value)
        {
            if (peerDict is not BDictionary peer)
                continue;
            var ip = new IPAddress(peer.Get<BString>("ip").Value.ToArray());
            var port = (int)peer.Get<BNumber>("port").Value;
            var id = peer.Get<BString>("id").Value.ToArray();
            peers.Add(new Peer(ip, port, id));
        }
        return peers;
    }

    ~TorrentTask()
    {
        Console.WriteLine("Disposed task");
        _cancellation.Cancel();
        client.Dispose();
        _mainCtrlChannel.Writer.TryComplete();
        _thread?.Dispose();
    }
}

file static class CtrlMessageExtensions {
    internal static async Task Handle(this NewPeer msg, TorrentTask task, List<PeerConnection> connections)
    {
        if (connections.Select(pc => pc.Peer).Contains(msg.Peer))
            return;
        connections.Add(msg.PeerConnection);
        await task.SupplyPiecesToPeer(msg.PeerConnection, PeerConnection.MAX_DOWNLOADING_PIECES);
        Console.WriteLine($"Added Connection: {msg.Peer}");
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
        if (request.Idx >= task._downloadedPieces.Count || !task._downloadedPieces[(int)request.Idx])
            return;
        var piece = await task._storage.GetPieceAsync((int)request.Idx);
        var buf = new byte[request.Length]; 
        Array.Copy(piece.Data, (int)request.Begin, buf, 0, (int)request.Length);
        var chunk = new Data.Chunk(request.Idx, request.Begin, buf);
        await conn.PeerChannel.Writer.WriteAsync(new SupplyChunk(chunk), task._cancellation.Token);
    }
    internal static async Task Handle(this DownloadedChunk msg, TorrentTask task, List<PeerConnection> connections)
    {
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
            Console.WriteLine($"Could not write to file: {e.Message}");
            return;
        }
        task.DownloadedValid += pieceBuf.Length;

        await task.AnnounceDownloadedPiece(piece, msg.Peer);

        var peer = connections.First(c => c.Peer == msg.Peer);
        await task.SupplyPiecesToPeer(peer, 1);
    }
    internal static async Task Handle(this CloseConnection msg, TorrentTask task, List<PeerConnection> connections)
    {
        foreach (var idx in msg.WasDownloading)
        {
            task._downloadingPieces.Remove(idx);
        }
        connections.RemoveAll(conn => conn.Peer == msg.Peer);
        Console.WriteLine($"closed connection with {msg.Peer}");
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
