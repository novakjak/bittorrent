using System;
using System.Collections.Generic;
using System.Net;

namespace bittorrent.Models;

public class Peer : IEquatable<Peer>
{
    public IPAddress Ip { get; set; }
    public int Port { get; set; }
    public byte[]? PeerId { get; set; }

    public Peer(IPAddress ip, int port, byte[]? peerId)
    {
        if (peerId is not null && peerId.Length != 20)
        {
            throw new ArgumentException("Peer id is of invalid length.");
        }
        Ip = ip;
        Port = port;
        PeerId = peerId;
    }

    public override string ToString()
    {
        return $"{Ip}:{Port}";
    }

    public bool Equals(Peer? other)
    {
        return other is not null && this.Ip.Equals(other.Ip) && this.Port == other.Port;
    }
}
