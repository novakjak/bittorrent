using System;
using System.Linq;
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

namespace bittorrent.Models;

public class TorrentTask
{
    public readonly BT.Torrent Torrent;
    public int PeerCount { get; private set; } = 0;
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; private set; } = 0;
    public int DownloadedValid { get; private set; } = 0;

    private Task? _thread;
    private string _peerId = "randompeeridaaaaaaaa";
    private List<Peer> _peers = new();
    private Channel<ICtrlMsg> _mainCtrlChannel;
    private BitArray _downloadedPieces;
    private BitArray _downloadingPieces;

    private static readonly HttpClient client = new();

    public void Start()
    {
        _thread ??= Task.Run(this.ManagePeers);
    }

    public TorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
        _downloadedPieces = new BitArray(Torrent.NumberOfPieces);
        _downloadingPieces = new BitArray(Torrent.NumberOfPieces);
        _mainCtrlChannel = Channel.CreateUnbounded<ICtrlMsg>();
    }

    private async Task ManagePeers()
    {
        var connections = new List<PeerConnection>();
        await Announce();

        // Main control loop
        var rx = _mainCtrlChannel.Reader;
        while (await rx.WaitToReadAsync())
        {
            var message = await rx.ReadAsync();
            switch (message)
            {
                case NewPeer np: {
                    if (connections.Select(pc => pc.Peer).Contains(np.Peer))
                        break;
                    connections.Add(np.PeerConnection);
                    Console.WriteLine($"Added Connection: {np.Peer}");
                    break;
                }
                case RequestPieces rp: {
                    Console.WriteLine("reques gotten");
                    // Enumerate all pieces that have yet not been fully downloaded.
                    var notDownloaded = new BitArray(_downloadedPieces);
                    notDownloaded.Not();

                    // Enumerate all pieces that are not being downloaded right now.
                    var notDownloading = new BitArray(_downloadingPieces);
                    notDownloading.Not();

                    // Create a result of what is available to be downloaded.
                    var availableToDownload = notDownloaded.And(notDownloading);

                    // Limit to only those that the peer can supply.
                    availableToDownload.And(rp.PeerConnection.PeerHas);

                    var pieces = availableToDownload.OfType<bool>()
                        .Index()
                        .Where(p => p.Item2)
                        .Select(p => p.Item1).Take(20).ToArray();

                    if (pieces.Length == 0)
                    {
                        Console.WriteLine("no pieces to send");
                        break;
                    }

                    foreach (var piece in pieces)
                    {
                        _downloadingPieces[piece] = true;
                    }

                    var rng = new Random();
                    rng.Shuffle(pieces);
                    var conn = connections.First(c => c.Peer == rp.Peer);
                    var msg = new SupplyPieces(rp.Peer, pieces);
                    await conn.PeerChannel.Writer.WriteAsync(msg);
                    break;
                }
            }
        }
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
            using var response = await client.SendAsync(message);
            var parser = new BencodeParser();
            body = parser.Parse<BDictionary>(await response.Content.ReadAsStreamAsync());
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
                    .CreateAsync(peer, Torrent,
                                 peerId, _mainCtrlChannel, peerChannel);
                await _mainCtrlChannel.Writer.WriteAsync(new NewPeer(pc));
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
}
