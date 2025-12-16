using System;

namespace bittorrent.Core.Data;

public sealed class Chunk
{
	public const int DEFAULT_CHUNK_SIZE = 16384; // 2^14 or 16 kiB
	public UInt32 Idx { get; }
	public UInt32 Begin { get; }
	public byte[] Data { get; }

	public Chunk(UInt32 idx, UInt32 begin, byte[] data)
	{
		Idx = idx;
		Begin = begin;
		Data = data;
	}
}
