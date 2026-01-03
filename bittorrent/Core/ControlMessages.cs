using System.Collections.Generic;
using bittorrent.Models;

namespace bittorrent.Core;

public interface IPeerCtrlMsg
{
	public Peer Peer { get; }
}
public interface ITaskCtrlMsg {}

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
public class CloseConnection(Peer peer, List<int> wasDownloading) : IPeerCtrlMsg
{
	public Peer Peer { get; } = peer;
	public List<int> WasDownloading { get; } = wasDownloading;
}
public class HavePiece(Peer peer, int idx) : IPeerCtrlMsg
{
	public Peer Peer { get; } = peer;
	public int Idx { get; } = idx;
}
public class RequestPieces(Peer peer, int count) : IPeerCtrlMsg
{
	public Peer Peer { get; } = peer;
	public int Count { get; } = count;
}

public class SupplyPieces(IEnumerable<int> pieces) : ITaskCtrlMsg
{
	public IEnumerable<int> Pieces { get; } = pieces;
}
public class FinishConnection : ITaskCtrlMsg {}
