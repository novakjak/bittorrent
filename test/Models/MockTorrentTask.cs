using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BT = BencodeNET.Torrents;

using BitAvalanche.Core;
using BitAvalanche.Models;

namespace test.Models;

public class MockTorrentTask : ITorrentTask
{
    public BT.Torrent Torrent { get; }
    public byte[] HashId => Torrent.OriginalInfoHashBytes;
    public string PeerId { get; } = Util.GenerateRandomString(20);
    public int PeerCount => _connections.Count();
    public Channel<IPeerCtrlMsg> CtrlChannel { get; } = Channel.CreateUnbounded<IPeerCtrlMsg>();
    public int Uploaded { get; private set; } = 0;
    public int Downloaded { get; internal set; } = 0;
    public int DownloadedValid { get; internal set; } = 0;
    public BitArray DownloadedPieces { get; private set; }
    public bool IsCompleted => DownloadedPieces.HasAllSet();

    private List<PeerConnection> _connections = new();

    public MockTorrentTask(BT.Torrent torrent)
    {
        Torrent = torrent;
        DownloadedPieces = new BitArray(Torrent.NumberOfPieces);
    }

    public void Start()
    { }

    public async Task Stop()
    { }

    public void AddPeer(INetworkClient conn, Peer peer)
    { }
}
