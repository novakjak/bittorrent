using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BencodeNET.Parsing;
using BencodeNET.Torrents;

using bittorrent.Core;
using bittorrent.Models;

using Moq;

namespace test.Models;

public class ConnectionListenerTests
{
    readonly Torrent torrent;
    readonly byte[] peerId;
    readonly Peer peer;
    readonly IPEndPoint endpoint;
    readonly Stream dataStream;
    readonly INetworkClient mockClient;
    readonly CancellationTokenSource tokenSource = new();
    readonly MockNetworkListener networkListener;
    readonly ConnectionListener listener;
    readonly EventWaitHandle waitHandle;
    INetworkClient? gotClient;
    Peer? gotPeer;

    public ConnectionListenerTests()
    {
        var parser = new BencodeParser();
        torrent = parser.Parse<Torrent>("Resources/torrentData.txt.torrent");
        peerId = Encoding.ASCII.GetBytes(Util.GenerateRandomString(20));
        var ipAddr = IPAddress.Parse("127.0.0.1");
        var port = 1234;
        peer = new Peer(ipAddr, port, peerId);
        endpoint = new IPEndPoint(ipAddr, port);
        dataStream = new MockNetworkStream();
        tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(5000);
        waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

        networkListener = (MockNetworkListener)MockNetworkListener.Create(8080);
        listener = new ConnectionListener(networkListener);
        listener.NewPeer += ValidateNewPeer;

        var mock = new Mock<INetworkClient>();
        mock.Setup(m => m.GetStream()).Returns(() => dataStream);
        mock.Setup(m => m.IPEndPoint).Returns(endpoint);
        mockClient = mock.Object;
    }

    [Fact]
    public async Task AcceptConnection()
    {
        dataStream.Write(new Handshake(torrent.OriginalInfoHashBytes, peerId).ToBytes());
        listener.Start();
        await networkListener.AddNetworkClient(mockClient, tokenSource.Token);
        Assert.True(waitHandle.WaitOne(1000));
    }

    private void ValidateNewPeer(object? sender, (INetworkClient Client, Peer Peer, byte[] InfoHash) args)
    {
        Assert.Equal(torrent.OriginalInfoHashBytes, args.InfoHash);
        Assert.Equal(peer, args.Peer);
        gotClient = args.Client;
        gotPeer = args.Peer;
        waitHandle.Set();
    }
}
