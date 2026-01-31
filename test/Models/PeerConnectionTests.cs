using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BencodeNET.Parsing;
using BencodeNET.Torrents;

using bittorrent.Core;
using bittorrent.Models;

using Moq;

using Xunit;
using Xunit.v3;

[assembly: CaptureConsole]

namespace test.Models;

public class PeerConnectionTests
{
    readonly Torrent torrent;
    readonly byte[] testPeerId;
    readonly byte[] externalPeerId;
    readonly Channel<IPeerCtrlMsg> ctrlChannel;
    readonly Channel<ITaskCtrlMsg> peerChannel;
    readonly BitArray haveNone;
    readonly BitArray haveAll;
    readonly Peer peer;
    readonly IPEndPoint endpoint;
    readonly Stream dataStream;
    readonly INetworkClient mockClient;
    readonly CancellationTokenSource tokenSource = new();
    readonly CancellationTokenSource peerTokenSource = new();

    public PeerConnectionTests()
    {
        var parser = new BencodeParser();
        torrent = parser.Parse<Torrent>("Resources/torrentData.txt.torrent");
        testPeerId = Encoding.ASCII.GetBytes(Util.GenerateRandomString(20));
        externalPeerId = Encoding.ASCII.GetBytes(Util.GenerateRandomString(20));
        ctrlChannel = Channel.CreateUnbounded<IPeerCtrlMsg>();
        peerChannel = Channel.CreateUnbounded<ITaskCtrlMsg>();
        haveNone = new BitArray(torrent.NumberOfPieces, false);
        haveAll = new BitArray(torrent.NumberOfPieces, true);
        peer = new Peer(IPAddress.Parse("127.0.0.1"), 1234, externalPeerId);
        endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234);
        dataStream = new MockNetworkStream();
        var mock = new Mock<INetworkClient>();
        mock.Setup(m => m.ConnectAsync(
            peer.Ip,
            peer.Port,
            It.IsAny<CancellationToken>()
        ));
        mock.Setup(m => m.GetStream()).Returns(() => dataStream);
        mock.Setup(m => m.Close());
        mock.Setup(m => m.IPEndPoint).Returns(endpoint);
        mockClient = mock.Object;
        tokenSource.CancelAfter(5000);
        peerTokenSource.CancelAfter(5000);
        var handshake = new Handshake(torrent.OriginalInfoHashBytes, peer.PeerId!);
        dataStream.Write(handshake.ToBytes());
    }

    [Fact]
    public async Task AcceptHandshake()
    {
        var (conn, _) = await ConnectAsync();
        conn.Stop();
    }

    [Fact]
    public async Task ReceiveMessages()
    {

        var bitfield = new BitArray(new bool[] { true });
        var messages = new IPeerMessage[]
        {
            new Unchoke(),
            new Interested(),
            new Bitfield(bitfield),
            new Have(0),
            new Piece(1, 2, new byte[] { 0, 1, 2, 3 }),
        };
        foreach (var msg in messages)
        {
            dataStream.Write(msg.ToBytes());
        }

        var (conn, handshake) = await ConnectAsync();

        var newpeer = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<NewPeer>(newpeer);

        // once from the bitfield message
        var requestpieces = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<RequestPieces>(requestpieces);
        Assert.Equal(PeerConnection.MAX_DOWNLOADING_PIECES, ((RequestPieces)requestpieces).Count);
        Assert.Equal(peer, requestpieces.Peer);

        // second time from the have message
        requestpieces = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<RequestPieces>(requestpieces);
        Assert.Equal(PeerConnection.MAX_DOWNLOADING_PIECES, ((RequestPieces)requestpieces).Count);
        Assert.Equal(peer, requestpieces.Peer);

        var request = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<RequestPieces>(request);
        var chunk = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<DownloadedChunk>(chunk);
        Assert.Equal(peer, chunk.Peer);
        var chunkMsg = (DownloadedChunk)chunk;
        Assert.Equal(1u, chunkMsg.Chunk.Idx);
        Assert.Equal(2u, chunkMsg.Chunk.Begin);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, chunkMsg.Chunk.Data);

        Assert.False(conn.AmChoked);
        Assert.True(conn.IsInterested);
        conn.Stop();
    }

    [Fact]
    public async Task SendMessages()
    {
        var messages = new List<IPeerMessage>
        {
            new KeepAlive(),
            new Unchoke(),
            new Choke(),
            new Interested(),
            new NotInterested(),
            new Have(42),
            new Bitfield(new BitArray(new bool[] {true, false, true})),
            new Request(1, 2, 3),
            new Piece(4, 5, new byte[] { 6, 7, 8, 9 }),
            new Cancel(1, 2, 3),
        };
        dataStream.Write(new Unchoke().ToBytes());
        var (conn, handshake) = await ConnectAsync();
        await conn.SendMessages(messages);
        var msgs = ReadMessages(10);
        Assert.IsType<KeepAlive>(msgs[0]);
        Assert.IsType<Unchoke>(msgs[1]);
        Assert.IsType<Choke>(msgs[2]);
        Assert.IsType<Interested>(msgs[3]);
        Assert.IsType<NotInterested>(msgs[4]);
        Assert.IsType<Have>(msgs[5]);
        Assert.IsType<Bitfield>(msgs[6]);
        Assert.IsType<Request>(msgs[7]);
        Assert.IsType<Piece>(msgs[8]);
        Assert.IsType<Cancel>(msgs[9]);
        conn.Stop();
    }

    [Fact]
    public async Task CloseConnection()
    {
        dataStream.ReadTimeout = 0;
        var (conn, handshake) = await ConnectAsync();
        dataStream.Close();
        var newpeer = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<NewPeer>(newpeer);
        var request = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<RequestPieces>(request);
        var closeconn = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<CloseConnection>(closeconn);
        conn.Stop();
    }

    [Fact]
    public async Task FinishConnection()
    {
        var (conn, handshake) = await ConnectAsync();
        await peerChannel.Writer.WriteAsync(new FinishConnection(), tokenSource.Token);
        await peerChannel.Reader.Completion.WaitAsync(tokenSource.Token);
        Assert.False(conn.IsStarted);
        conn.Stop();
    }

    [Fact]
    public async Task CloseCtrlChannel()
    {
        dataStream.ReadTimeout = 0;
        var (conn, handshake) = await ConnectAsync();
        ctrlChannel.Writer.Complete();

        while (ctrlChannel.Reader.Count > 0)
        {
            await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        }

        await ctrlChannel.Reader.Completion.WaitAsync(tokenSource.Token);
        await Task.Delay(200, tokenSource.Token); // Wait for PeerConnection to try and read from ctrlChannel
        Assert.False(conn.IsStarted);
        conn.Stop();
    }

    [Fact]
    public async Task ClosePeerChannel()
    {
        dataStream.ReadTimeout = 0;
        peerChannel.Writer.Complete();
        var (conn, handshake) = await ConnectAsync();
        var newpeer = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<NewPeer>(newpeer);

        await peerChannel.Reader.Completion.WaitAsync(tokenSource.Token);

        var request = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<RequestPieces>(request);
        var closeconn = await ctrlChannel.Reader.ReadAsync(tokenSource.Token);
        Assert.IsType<CloseConnection>(closeconn);

        Assert.False(conn.IsStarted);
        conn.Stop();
    }

    private async Task<(PeerConnection, Handshake)> ConnectAsync()
    {
        var conn = await PeerConnection.CreateWithClientAsync(
            mockClient, peer, torrent, testPeerId,
            ctrlChannel,
            peerChannel,
            haveNone,
            peerTokenSource
        );
        var handshakeBuf = new byte[Handshake.MessageLength];
        var nread = dataStream.Read(handshakeBuf, 0, handshakeBuf.Length);
        Assert.Equal(Handshake.MessageLength, nread);
        var handshakeReceived = Handshake.Parse(handshakeBuf);
        Assert.Equal(handshakeReceived.PeerId, testPeerId);
        return (conn, handshakeReceived);
    }

    private List<IPeerMessage> ReadMessages(int maxCount = Int32.MaxValue)
    {
        var msgs = new List<IPeerMessage>();
        while (msgs.Count < maxCount)
        {
            var lenBuf = new byte[4];
            try
            {
                dataStream.ReadExactly(lenBuf, 0, 4);
            }
            catch
            {
                break;
            }
            var len = (int)Util.FromNetworkOrderBytes(lenBuf, 0);
            var msgBuf = new byte[4 + len];
            lenBuf.CopyTo(msgBuf, 0);
            dataStream.ReadExactly(msgBuf, 4, len);
            var msg = PeerMessageParser.Parse(msgBuf);
            if (msg is null) throw new ParseException("Could not parse message");
            msgs.Add(msg);
        }
        return msgs;
    }
}
