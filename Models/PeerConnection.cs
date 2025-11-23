using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace bittorrent.Models;

public class PeerConnection
{
    public Peer Peer { get; private set; }
    public byte[] InfoHash { get; private set; }
    public byte[] PeerId { get; private set; }

    private readonly TcpClient _client;

    private PeerConnection(Peer peer, byte[] infoHash, byte[] peerId)
    {
        if (infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash is of incorrect length.");
        }
        if (peerId.Length != 20)
        {
            throw new ArgumentException("Peer id is of incorrect length.");
        }

        Peer = peer;
        InfoHash = infoHash;
        PeerId = peerId;
        _client = new TcpClient();
    }

    public static async Task<PeerConnection> CreateAsync(Peer peer, byte[] infoHash, byte[] peerId)
    {
        var pc = new PeerConnection(peer, infoHash, peerId);
        await pc._client.ConnectAsync(pc.Peer.Ip, pc.Peer.Port);
        await pc.HandShake();
        return pc;
    }

    private async Task HandShake()
    {
        var stream = _client.GetStream();
        byte[] protocolName = "BitTorrent protocol"u8.ToArray();
        var len = protocolName.Length;
        byte[] reserved = new byte[8];

        List<byte> buffer = new();
        buffer.Add((byte)protocolName.Length);
        buffer.AddRange(protocolName);
        buffer.AddRange(reserved);
        buffer.AddRange(InfoHash);
        buffer.AddRange(PeerId);

        await stream.WriteAsync(buffer.ToArray());

        var handshake = new Memory<byte>(new Byte[49 + len]);
        await stream.ReadExactlyAsync(handshake);

        if (handshake.Span[0] != len)
            throw new HandShakeException("Recieved invalid protocol name length from peer.");
        if (handshake.Slice(1, len).ToArray() != protocolName)
            throw new HandShakeException("Recieved invalid protocol name from peer.");
        // Skip checking reserved bytes (20..28)
        if (handshake.Slice(1 + len + 8, 20).ToArray() != InfoHash)
            throw new HandShakeException("Info hash recieved from peer differs from the one sent.");
        if (Peer.PeerId is not null && handshake.Slice(1 + len + 8 + 20, 20).ToArray() != Peer.PeerId)
            throw new HandShakeException("Peer's id mismatched.");
        Console.WriteLine($"Got peer {Peer.PeerId}");
    }

    ~PeerConnection()
    {
        _client.Dispose();
    }
}
