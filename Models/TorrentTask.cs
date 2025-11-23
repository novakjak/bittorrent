using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using BencodeNET.Parsing;
using BencodeNET.Objects;
using BT = BencodeNET.Torrents;

namespace bittorrent.Models;

public class TorrentTask
{
    private static readonly HttpClient client = new();

    public BT.Torrent Torrent { get; private set; }
    public int PeerCount { get; private set; } = 0;
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; private set; } = 0;
    public int DownloadedValid { get; private set; } = 0;


    private Thread? _thread;
    private string _peerId = Util.GenerateRandomString(20);

    public void Start()
    {
        _thread ??= new Thread(new ThreadStart(this.Work));
        _thread.Start();
    }

    public TorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
    }

    private async void Work()
    {
        var tracker = Torrent.Trackers[0][0];
        var urlencoded = System.Web.HttpUtility.UrlEncode(Torrent.OriginalInfoHashBytes);

        var query = $"info_hash={urlencoded}";
        query += $"&peer_id={_peerId}";
        query += $"&port=8085";
        query += $"&downloaded={Downloaded}";
        query += $"&uploaded={Uploaded}";
        query += $"&left={Torrent.TotalSize - DownloadedValid}";

        var message = new HttpRequestMessage(HttpMethod.Get, $"{tracker}?{query}");
        
        // TODO: exception handling
        BDictionary body;
        try
        {
            using var response = await client.SendAsync(message);
            var parser = new BencodeParser();
            body = parser.Parse<BDictionary>(await response.Content.ReadAsStreamAsync());
            BString failure;
            if ((failure = body.Get<BString>(new BString("failure reason"))) is not null)
            {
                Console.WriteLine(failure.ToString());
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
        var peers = ParsePeers(body["peers"]);
        if (peers.Count() == 0)
            Console.WriteLine("no peers");
        foreach (var p in peers)
            Console.WriteLine(p);

    }

    private static IEnumerable<Peer> ParsePeers(IBObject peers)
    {
        if (peers is BString s)
            return ParsePeerString(s);
        if (peers is BList l)
            return ParsePeerList(l);
        throw new ParseException("Peers object must be either a dictionary or a list.");
    }

    private static IEnumerable<Peer> ParsePeerString(BString peers)
    {
        var res = new List<Peer>();
        var buf = peers.Value;
        for (int i = 0; i < peers.Length / 6; i++)
        {
            var addr = new IPAddress(buf.Slice(i * 6, 4).ToArray());
            var port = BitConverter.ToInt16(buf.Slice(i * 6 + 4, 2).ToArray(), 0);
            res.Add(new Peer(addr, port, null));
        }
        return res;
    }

    private static IEnumerable<Peer> ParsePeerList(BList peersList)
    {
        if (peersList is null)
        {
            Console.WriteLine("no peers");
            return new List<Peer>();
        }

        List<Peer> peers = new();
        foreach (var peerDict in peersList.Value)
        {
            if (peerDict is not BDictionary peer)
                continue;
            var ip = new IPAddress(peer.Get<BString>("ip").Value.ToArray());
            var port = (int)peer.Get<BNumber>("port").Value;
            var id = peer.Get<BString>("id").Value.ToArray();
            peers.Add(new Peer(ip, port, id));
            Console.WriteLine(id);
        }
        return peers;
    }
}
