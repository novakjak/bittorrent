using System.Collections.Generic;
using bittorrent.Models;

namespace bittorrent.Core;

public interface ICtrlMsg
{
	public Peer Peer { get; }
}

public class NewPeer : ICtrlMsg
{
	public Peer Peer { get; }
	public PeerConnection PeerConnection { get; }
	public NewPeer(PeerConnection pc)
	{
		Peer = pc.Peer;
		PeerConnection = pc;
	}
}
public class SupplyPieces : ICtrlMsg
{
	public Peer Peer { get; }
	public IEnumerable<int> Pieces { get; }
	public SupplyPieces(Peer peer, IEnumerable<int> pieces)
	{
		Peer = peer;
		Pieces = pieces;
	}
}
public class DownloadedChunk : ICtrlMsg
{
	public Peer Peer { get; }
	public Data.Chunk Chunk { get; }
	public DownloadedChunk(Peer peer, Data.Chunk chunk)
	{
		Peer = peer;
		Chunk = chunk;
	}
}
public class HavePiece : ICtrlMsg
{
	public Peer Peer { get; }
	public int Idx { get; }
	public HavePiece(Peer peer, int idx)
	{
		Peer = peer;
		Idx = idx;
	}
}
