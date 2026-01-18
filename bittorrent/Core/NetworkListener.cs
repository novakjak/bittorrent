using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace bittorrent.Core;

public interface INetworkListener : IDisposable
{
    public EndPoint LocalEndpoint { get; }
    static abstract INetworkListener Create(int port);
    Task<INetworkClient> AcceptNetworkClientAsync(CancellationToken token);
    void Start();
    void Stop();
}

public class NetworkListener : INetworkListener, IDisposable
{
    private TcpListener _listener;

    public EndPoint LocalEndpoint { get => _listener.LocalEndpoint; }
    
    private NetworkListener(TcpListener listener) => _listener = listener;

    public static INetworkListener Create(int port)
    {
        var listener = TcpListener.Create(port);
        return new NetworkListener(listener);
    }

    public async Task<INetworkClient> AcceptNetworkClientAsync(CancellationToken token)
        => new NetworkClient(await _listener.AcceptTcpClientAsync(token));

    public void Start()
    {
        _listener.Start();
        Logger.Info($"Started listening on port {(LocalEndpoint as IPEndPoint).Port}");
    }
    public void Stop() => _listener.Stop();
    public void Dispose() => _listener.Dispose();
}
