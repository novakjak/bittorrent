using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BencodeNET.Objects;
using BencodeNET.Parsing;

using BitAvalanche.Core;

using BT = BencodeNET.Torrents;

namespace BitAvalanche.Models;

public class TrackerAnnouncer
{
    public const int DEFAULT_TIMEOUT = 15 * 60 * 1000;
    public const int DEFAULT_MIN_TIMEOUT = 3 * 60 * 1000;

    public TorrentTask Task { get; set; }

    public event EventHandler<IEnumerable<Peer>>? ReceivedPeers;

    private string? _usedTracker;
    private byte[]? _trackerId;
    private Timer _timer;
    private CancellationTokenSource _cancellation = new();
    private readonly HttpClient client = new();
    private long? _interval = null;
    private long? _minInterval = null;
    private bool _sentStarted = false;
    private bool _sentCompleted = false;
    private bool _usingMinTimeout = false;
    private DateTime _lastAnnounce = DateTime.UnixEpoch;

    public TrackerAnnouncer(TorrentTask task)
    {
        Task = task;
        Task.PeerCountChanged += (_, _) => ReconfigureTimer();
        _timer = new Timer(async (_) => await Announce(), null, Timeout.Infinite, _interval ?? DEFAULT_TIMEOUT);
    }

    public void Start()
    {
        _cancellation = new CancellationTokenSource();
        _timer.Change(0, _interval ?? DEFAULT_TIMEOUT);
    }

    public async Task Stop()
    {
        if (_usedTracker is not null)
        {
            var query = BuildStopQuery();
            var message = new HttpRequestMessage(HttpMethod.Get, $"{_usedTracker}?{query}");
            using var response = await client.SendAsync(message, _cancellation.Token);
        }
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task Announce()
    {
        _lastAnnounce = DateTime.Now;
        var query = BuildQuery();
        if (_usedTracker is not null)
        {
            if (await AnnounceToTracker(_usedTracker, query))
                return;
            _usedTracker = null;
        }

        foreach (var group in Task.Torrent.Trackers)
        {
            foreach (var trackerUrl in group)
            {
                if (!await AnnounceToTracker(trackerUrl, query))
                    continue;
                _usedTracker = trackerUrl;
                return;
            }
        }
    }

    private async Task<bool> AnnounceToTracker(string trackerUrl, string query)
    {
        try
        {
            var message = new HttpRequestMessage(HttpMethod.Get, $"{trackerUrl}?{query}");

            using var response = await client.SendAsync(message, _cancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Error($"Could not get peers from tracker: {trackerUrl}");
                return false;
            }
            var parser = new BencodeParser();
            BDictionary body = parser.Parse<BDictionary>(await response.Content.ReadAsStreamAsync(_cancellation.Token));
            BString failure = body.Get<BString>("failure reason");
            if (failure is not null)
            {
                Logger.Error($"Communication with tracker failed: {failure.ToString()}");
                return false;
            }
            BNumber? minInterval = body.Get<BNumber>("min interval");
            _minInterval ??= minInterval?.Value * 1000;
            BNumber? newInterval = body.Get<BNumber>("interval");
            if (_interval is null && newInterval is null)
            {
                Logger.Error("Did not get interval from tracker");
                return false;
            }
            else if (newInterval is not null)
            {
                _interval = newInterval.Value * 1000;
                ReconfigureTimer();
            }
            BString? trackerId = body.Get<BString>("tracker id");
            if (trackerId is not null)
            {
                _trackerId = trackerId.Value.ToArray();
            }
            var peers = ParsePeers(body["peers"]);
            ReceivedPeers?.Invoke(this, peers);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            return false;
        }
    }
 
    private void ReconfigureTimer()
    {
        long millisSinceAnnounce = DateTime.Now.Subtract(_lastAnnounce).Milliseconds;
        if (Task.PeerCount > 20 && _usingMinTimeout)
        {
            var timeout = Math.Max((_interval ?? 0) - millisSinceAnnounce, 0);
            _timer.Change(timeout, _interval ?? DEFAULT_TIMEOUT);
            _usingMinTimeout = false;
        }
        else if (Task.PeerCount <= 20 && !_usingMinTimeout && !Task.IsCompleted)
        {
            var timeout = Math.Max((_minInterval ?? 0) - millisSinceAnnounce, 0);
            _timer.Change(timeout, _minInterval ?? DEFAULT_MIN_TIMEOUT);
            _usingMinTimeout = true;
        }
        else
        {
            var default_interval = _usingMinTimeout ? DEFAULT_MIN_TIMEOUT : DEFAULT_TIMEOUT;
            var interval = (_usingMinTimeout ? _minInterval : _interval) ?? default_interval;
            var timeout = Math.Max(interval - millisSinceAnnounce, 0);
            _timer.Change(timeout, interval);
        }
    }

    private string BuildBaseQuery()
    {
        var peerId = Encoding.ASCII.GetBytes(Task.PeerId);
        var urlencodedHash = System.Web.HttpUtility.UrlEncode(Task.Torrent.OriginalInfoHashBytes);
        var query = $"info_hash={urlencodedHash}";
        query += $"&peer_id={Task.PeerId}";
        query += $"&port={Config.Get().DefaultPort}";
        query += $"&downloaded={Task.Downloaded}";
        query += $"&uploaded={Task.Uploaded}";
        query += $"&left={Task.Torrent.TotalSize - Task.DownloadedValid}";
        if (_trackerId is not null)
        {
            var encodedId = System.Web.HttpUtility.UrlEncode(_trackerId);
            query += $"&trackerid={encodedId}";
        }
        return query;
    }
    private string BuildQuery()
    {
        var query = BuildBaseQuery();
        query += $"&compact=1";
        if (!_sentStarted)
        {
            query += "&event=started";
        }
        else if (!_sentCompleted && Task.IsCompleted)
        {
            query += "&event=completed";
        }
        return query;
    }
    private string BuildStopQuery()
    {
        var query = BuildBaseQuery();
        query += "&event=stopped";
        return query;
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

    ~TrackerAnnouncer()
    {
        _cancellation.Cancel();
        Stop().Wait(200);
        client.Dispose();
        _timer.Dispose();
    }
}
