using System;
using System.Net;
using System.Collections.Generic;

namespace bittorrent.Models;

public class Peer
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
}
