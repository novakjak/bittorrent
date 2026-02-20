using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using BitAvalanche.Core;

namespace test.Models;

public class MockNetworkListener : INetworkListener
{
    private readonly Channel<INetworkClient> clients;
    public EndPoint LocalEndpoint { get; private set; }

    private MockNetworkListener(EndPoint localEndPoint)
    {
        LocalEndpoint = localEndPoint;
        clients = Channel.CreateUnbounded<INetworkClient>();
    }
    public static INetworkListener Create(int port)
        => new MockNetworkListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port));

    public async Task<INetworkClient> AcceptNetworkClientAsync(CancellationToken token)
        => await clients.Reader.ReadAsync(token);

    public async Task AddNetworkClient(INetworkClient client, CancellationToken token)
        => await clients.Writer.WriteAsync(client, token);

    public void Start() { }
    public void Stop() => clients.Writer.Complete();
    public void Dispose() { }
}
