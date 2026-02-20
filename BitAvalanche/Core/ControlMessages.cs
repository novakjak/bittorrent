using System.Collections.Generic;

using BitAvalanche.Models;

namespace BitAvalanche.Core;

public interface IPeerCtrlMsg
{
    public Peer Peer { get; }
}
public interface ITaskCtrlMsg { }

public class NewPeer(PeerConnection pc) : IPeerCtrlMsg
{
    public Peer Peer { get; } = pc.Peer;
    public PeerConnection PeerConnection { get; } = pc;
}
public class DownloadedChunk(Peer peer, Data.Chunk chunk) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
    public Data.Chunk Chunk { get; } = chunk;
}
public class CloseConnection(Peer peer) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
}
public class ReclaimPieces(Peer peer, List<int> wasDownloading) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
    public List<int> WasDownloading { get; } = wasDownloading;
}
public class RequestPieces(Peer peer, int count) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
    public int Count { get; } = count;
}
public class RequestChunk(Peer peer, Request request) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
    public Request Request { get; } = request;
}
public class Uploaded(Peer peer, int amount) : IPeerCtrlMsg
{
    public Peer Peer { get; } = peer;
    public int Amount { get; } = amount;
}

public class SupplyPieces(IEnumerable<int> pieces) : ITaskCtrlMsg
{
    public IEnumerable<int> Pieces { get; } = pieces;
}
public class SupplyChunk(Data.Chunk chunk) : ITaskCtrlMsg
{
    public Data.Chunk Chunk { get; } = chunk;
}
public class FinishConnection : ITaskCtrlMsg { }
public class HavePiece(int idx) : ITaskCtrlMsg
{
    public int Idx { get; } = idx;
}
