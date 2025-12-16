using System.Collections.Generic;
using bittorrent.Models;

namespace bittorrent.Core;

public interface ICtrlMsg
{
	public Peer Peer { get; }
}

public class NewPeer(PeerConnection pc) : ICtrlMsg
{
	public Peer Peer { get; } = pc.Peer;
	public PeerConnection PeerConnection { get; } = pc;
}
public class SupplyPieces(Peer peer, IEnumerable<int> pieces) : ICtrlMsg
{
	public Peer Peer { get; } = peer;
	public IEnumerable<int> Pieces { get; } = pieces;
}
public class DownloadedChunk(Peer peer, Data.Chunk chunk) : ICtrlMsg
{
	public Peer Peer { get; } = peer;
	public Data.Chunk Chunk { get; } = chunk;
}
public class HavePiece(Peer peer, int idx) : ICtrlMsg
{
	public Peer Peer { get; } = peer;
	public int Idx { get; } = idx;
}
public class CloseConnection(Peer peer, List<int> wasDownloading) : ICtrlMsg
{
	public Peer Peer { get; } = peer;
	public List<int> WasDownloading { get; } = wasDownloading;
}
