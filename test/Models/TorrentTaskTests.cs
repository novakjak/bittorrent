using System;
using System.Collections;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Moq;

using BencodeNET.Parsing;
using BencodeNET.Torrents;

using bittorrent.Models;
using bittorrent.Core;
using Data=bittorrent.Core.Data;

namespace test.Models;

public class TorrentTaskTests
{

    Torrent torrent;
    byte[] torrentData;
    TorrentTask task;

    BitArray haveAll;
    
    public TorrentTaskTests()
    {
        var parser = new BencodeParser();
        torrent = parser.Parse<Torrent>("Resources/torrentData.txt.torrent");
        torrentData = File.ReadAllBytes("Resources/torrentData_source_file.txt");
        task = new TorrentTask(torrent);
        haveAll = new BitArray(torrent.NumberOfPieces, true);
    }

    [Fact]
    public async Task AddPeer()
    {
        task.Start();
        AddTestPeer();
        await task.Stop();
    }

    [Fact]
    public async Task ReceivePieceRequests()
    {
        task.Start();
        var stream = AddTestPeer();

        var msgBuf = new byte[17];
        var nread = stream.Read(msgBuf, 0, msgBuf.Length);
        Assert.Equal(msgBuf.Length, nread);
        var msg = PeerMessageParser.Parse(msgBuf);
        Assert.IsType<Request>(msg);
        var request = (Request)msg;
        Assert.Equal(0u, request.Idx);
        Assert.Equal(0u, request.Begin);
        Assert.Equal(torrent.TotalSize, (long)request.Length);
        await task.Stop();
    }

    [Fact]
    public async Task StorePiece()
    {
        bool didDownload = false;

        task.Start();
        task.DownloadedPiece += (_, _) => didDownload = true;
        var stream = AddTestPeer();

        var msgBuf = new byte[17];
        stream.ReadExactly(msgBuf, 0, msgBuf.Length);
        var msg = PeerMessageParser.Parse(msgBuf);
        Assert.IsType<Request>(msg);

        var piece = new Piece(new Data.Chunk(0, 0, torrentData));
        stream.Write(piece.ToBytes());

        msgBuf = new byte[9];
        stream.ReadExactly(msgBuf, 0, msgBuf.Length);
        msg = PeerMessageParser.Parse(msgBuf);
        Assert.IsType<Have>(msg);
        Assert.Equal(0u, (msg as Have)!.Piece);

        Assert.True(didDownload);
        await task.Stop();
    }

    private Stream AddTestPeer()
    {
        var peerId = Encoding.ASCII.GetBytes(Util.GenerateRandomString(20));
        var ipAddr = IPAddress.Parse("127.0.0.1");
        var port = 1234;
        var peer = new Peer(ipAddr, port, peerId);
        var dataStream = new MockNetworkStream();

        dataStream.Write(new Unchoke().ToBytes());
        dataStream.Write(new Bitfield(haveAll).ToBytes());

        var mock = new Mock<INetworkClient>();
        mock.Setup(nc => nc.GetStream()).Returns(dataStream);
        var mockClient = mock.Object;

        task.AddPeer(mockClient, peer);

        var handshakeBuf = new byte[Handshake.MessageLength];
        var nread = dataStream.Read(handshakeBuf, 0, handshakeBuf.Length);
        Assert.Equal(Handshake.MessageLength, nread);
        var handshakeReceived = Handshake.Parse(handshakeBuf);
        Assert.Equal(handshakeReceived.PeerId, Encoding.ASCII.GetBytes(task.PeerId));
        return dataStream;
    }
}
