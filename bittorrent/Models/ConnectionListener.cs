using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using bittorrent.Core;
using bittorrent.Models;

namespace bittorrent.Models;

public class ConnectionListener
{
    public event EventHandler<(INetworkClient, Peer, byte[] InfoHash)>? NewPeer;

    public int Port { get; set; }

    private readonly INetworkListener _listener;
    private Task? _listenerTask;
    private readonly CancellationTokenSource _cancellation = new();

    public ConnectionListener(int port)
    {
        Port = port;
        _listener = NetworkListener.Create(port);
    }

    public ConnectionListener(INetworkListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Start()
    {
        _listenerTask ??= Task.Run(async () => await Listen(), _cancellation.Token);
    }

    public async Task Listen()
    {
        _listener.Start();
        while (!_cancellation.IsCancellationRequested)
        {
            var client = await _listener.AcceptNetworkClientAsync(_cancellation.Token);
            var messageBuf = new Byte[Handshake.MessageLength];
            var messageMem = new Memory<byte>(messageBuf);
            await client
                .GetStream()
                .ReadExactlyAsync(messageBuf, 0, messageBuf.Length, _cancellation.Token);
            var handshake = Handshake.Parse(messageMem);
            var clientEndPoint = client.IPEndPoint;
            var peer = new Peer(clientEndPoint.Address, clientEndPoint.Port, handshake.PeerId);
            var args = (client, peer, handshake.InfoHash);
            NewPeer?.Invoke(this, args);
        }
        _listener.Stop();
    }

    ~ConnectionListener()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _listener.Dispose();
    }
}
