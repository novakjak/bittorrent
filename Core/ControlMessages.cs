using System.Collections.Generic;
using bittorrent.Models;

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
public class RequestPieces : ICtrlMsg
{
	public Peer Peer { get; }
	public PeerConnection PeerConnection { get; }
	public RequestPieces(PeerConnection conn)
	{
		PeerConnection = conn;
		Peer = PeerConnection.Peer;
	}
}
public class SupplyPieces : ICtrlMsg
{
	public Peer Peer { get; }
	public List<int> Pieces { get; }
	public SupplyPieces(Peer peer, List<int> pieces)
	{
		Peer = peer;
		Pieces = pieces;
	}
}
public class DownloadedChunk : ICtrlMsg
{
	public Peer Peer { get; }
	public Chunk Chunk { get; }
	public DownloadedChunk(Peer peer, Chunk chunk)
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
