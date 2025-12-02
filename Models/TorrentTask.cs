using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Net;
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
    public int PeerCount { get; private set; } = 0;
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; private set; } = 0;
    public int DownloadedValid { get; private set; } = 0;

    public event EventHandler<(int pieceIdx, double completion)>? DownloadedPiece;

    private Task? _thread;
    private string _peerId = "randompeeridaaaaaaaa";
    private List<Peer> _peers = new();
    private Channel<ICtrlMsg> _mainCtrlChannel;
    private BitArray _downloadedPieces;
    private Dictionary<int, List<Data.Chunk>> _downloadingPieces = new();
    private PieceStorage _storage;
    private CancellationTokenSource _cancallation = new();

    private static readonly HttpClient client = new();

    public void Start()
    {
        _thread ??= Task.Run(this.ManagePeers, _cancallation.Token);
    }

    public TorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
        _downloadedPieces = new BitArray(Torrent.NumberOfPieces);
        _mainCtrlChannel = Channel.CreateUnbounded<ICtrlMsg>();
        _storage = new PieceStorage(torrent);
    }

    private async Task ManagePeers()
    {
        var connections = new List<PeerConnection>();
        await Announce();

        try
        {
        // Main control loop
        var rx = _mainCtrlChannel.Reader;
        while (await rx.WaitToReadAsync(_cancallation.Token))
        {
            var message = await rx.ReadAsync(_cancallation.Token);
            switch (message)
            {
                case NewPeer np: {
                    if (connections.Select(pc => pc.Peer).Contains(np.Peer))
                        break;
                    connections.Add(np.PeerConnection);
                    await SupplyPiecesToPeer(np.PeerConnection, 20);
                    Console.WriteLine($"Added Connection: {np.Peer}");
                    break;
                }
                case DownloadedChunk dc: {
                    var chunks = _downloadingPieces[(int)dc.Chunk.Idx];
                    if (chunks.Any(c => c.Begin == dc.Chunk.Begin))
                        break;
                    chunks.Add(dc.Chunk);
                    var pieceLen = _storage.GetPieceLength((int)dc.Chunk.Idx);
                    var chunksInAPiece = pieceLen / PeerConnection.CHUNK_SIZE;
                    if (pieceLen % PeerConnection.CHUNK_SIZE > 0)
                        chunksInAPiece++;
                    Downloaded += dc.Chunk.Data.Length;
                    if (chunks.Count() == chunksInAPiece)
                    {
                        _downloadingPieces.Remove((int)dc.Chunk.Idx);
                        var pieceBuf = new byte[pieceLen];
                        foreach (var chunk in chunks)
                        {
                            chunk.Data.CopyTo(pieceBuf, chunk.Begin);
                        }
                        var hash = System.Security.Cryptography.SHA1.HashData(pieceBuf);
                        var pieceHash = new ArraySegment<byte>(Torrent.Pieces, (int)dc.Chunk.Idx * 20, 20);
                        if (!hash.SequenceEqual(pieceHash))
                        {
                            break;
                        }

                        var piece = new Data.Piece((int)dc.Chunk.Idx, pieceBuf);
                        try {
                            await _storage.StorePieceAsync(piece);
                        }
                        catch (IOException e)
                        {
                            Console.WriteLine($"Could not write to file: {e.Message}");
                            return;
                        }
                        DownloadedValid += pieceBuf.Length;
                        var args = (piece.Idx, (double)DownloadedValid / (double)Torrent.TotalSize);
                        DownloadedPiece?.Invoke(this, args);
                        _downloadedPieces[(int)dc.Chunk.Idx] = true;

                        var peer = connections.First(c => c.Peer == dc.Peer);
                        await SupplyPiecesToPeer(peer, 1);
                    }
                    break;
                }
            }
        }
        }
        catch (Exception e)
        {
            Console.WriteLine("Thrown on storing piece " + e.Message);
        }
    }

    private async Task SupplyPiecesToPeer(PeerConnection pc, int count)
    {
        var pieces = RandomNotDownloadedPieces(pc.PeerHas, count);
        if (pieces.Count() > 0)
        {
            foreach (var piece in pieces)
            {
                _downloadingPieces.Add(piece, new List<Data.Chunk>());
            }
            var msg = new SupplyPieces(pc.Peer, pieces);
            await pc.PeerChannel.Writer.WriteAsync(msg, _cancallation.Token);
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
            .Select(p => p.Item1).ToArray();
        var rng = new Random();
        rng.Shuffle(pieces);
        return pieces.Take(20);
    }

    private async Task Announce()
    {
        // TODO: use more trackers
        var peerId = Encoding.ASCII.GetBytes(_peerId);
        var tracker = Torrent.Trackers[0][0];
        var urlencoded = System.Web.HttpUtility.UrlEncode(Torrent.OriginalInfoHashBytes);
        var query = $"info_hash={urlencoded}";
        query += $"&peer_id={_peerId}";
        query += $"&port=8085";
        query += $"&downloaded={Downloaded}";
        query += $"&uploaded={Uploaded}";
        query += $"&left={Torrent.TotalSize - DownloadedValid}";

        var message = new HttpRequestMessage(HttpMethod.Get, $"{tracker}?{query}");

        BDictionary body;
        try
        {
            using var response = await client.SendAsync(message, _cancallation.Token);
            var parser = new BencodeParser();
            body = parser.Parse<BDictionary>(await response.Content.ReadAsStreamAsync(_cancallation.Token));
            BString failure;
            if ((failure = body.Get<BString>(new BString("failure reason"))) is not null)
            {
                Console.WriteLine($"Communication with tracker failed: {failure.ToString()}");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("bad :(");
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
            var peerChannel = Channel.CreateUnbounded<ICtrlMsg>();
            _ = Task.Run(async () => {
                var pc = await PeerConnection
                    .CreateAsync(peer, Torrent, peerId,
                        _mainCtrlChannel, peerChannel, _downloadedPieces);
                await _mainCtrlChannel.Writer.WriteAsync(new NewPeer(pc), _cancallation.Token);
            });
        }
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
        _cancallation.Cancel();
        client.Dispose();
        _mainCtrlChannel.Writer.Complete();
        _thread?.Dispose();
    }
}
