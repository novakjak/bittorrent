using System;
using System.Collections;

using bittorrent.Core.Data;

namespace bittorrent.Core;

public static class PeerMessageParser
{
	public static IPeerMessage? Parse(byte[] message)
	{
		if (message.Length < 4)
			return null;
        var len = Util.FromNetworkOrderBytes(message, 0);
		if (len == 0)
			return new KeepAlive();
		if (message.Length != len + 4)
			return null;
		var type = message[4];
		switch (type)
		{
			case 0: {
				if (len != 1) return null;
				return new Choke();
			}
			case 1: {
				if (len != 1) return null;
				return new Unchoke();
			}
			case 2: {
				if (len != 1) return null;
				return new Interested();
			}
			case 3: {
				if (len != 1) return null;
				return new NotInterested();
			}
			case 4: {
				if (len != 5)
					return null;
		        var idx = Util.FromNetworkOrderBytes(message, 5);
				return new Have(idx);
			}
			case 5: {
				if (len <= 1) return null;
				var buf = new byte[len - 1];
				Array.Copy(message, 5, buf, 0, buf.Length);
				// Since BitArray stores indexes the bits in a byte
				// from LSB to MSB it is required to reverse the bits.
				for (int i = 0; i < buf.Length; i++)
				{
					buf[i] = Util.BitReverse(buf[i]);
				}
				var bitfield = new BitArray(buf);

				return new Bitfield(bitfield);
			}
			case 6: {
				if (len != 13) return null;
		        var idx = Util.FromNetworkOrderBytes(message, 5);
		        var begin = Util.FromNetworkOrderBytes(message, 9);
		        var length = Util.FromNetworkOrderBytes(message, 13);
				return new Request(idx, begin, length);
			}
			case 7: {
				if (len <= 9) return null;
		        var idx = Util.FromNetworkOrderBytes(message, 5);
		        var begin = Util.FromNetworkOrderBytes(message, 9);
				if (message.Length != len + 4) return null;
				var chunkLen = len - 9;
				var chunk = new byte[chunkLen];
				Array.Copy(message, 13, chunk, 0, chunkLen);
				return new Piece(idx, begin, chunk);
			}
			case 8: {
				if (len != 13) return null;
		        var idx = Util.FromNetworkOrderBytes(message, 5);
		        var begin = Util.FromNetworkOrderBytes(message, 9);
		        var length = Util.FromNetworkOrderBytes(message, 13);
				return new Cancel(idx, begin, length);
			}
		}
		return null;
	}
}

public interface IPeerMessage
{
	public byte[] ToBytes();
	// TODO: implement HandleMessage so we can use dependency injection
}

public class KeepAlive : IPeerMessage
{
	public byte[] ToBytes() => [0, 0, 0, 0];
}
public class Choke : IPeerMessage
{
	public byte[] ToBytes() => [0, 0, 0, 1, 0];
}
public class Unchoke : IPeerMessage
{
	public byte[] ToBytes() => [0, 0, 0, 1, 1];
}
public class Interested : IPeerMessage
{
	public byte[] ToBytes() => [0, 0, 0, 1, 2];
}
public class NotInterested : IPeerMessage
{
	public byte[] ToBytes() => [0, 0, 0, 1, 3];
}
public class Have(UInt32 piece) : IPeerMessage
{
	public UInt32 Piece { get; set; } = piece;
	public byte[] ToBytes()
	{
		var buf = new byte[9];
		byte[] header = [0, 0, 0, 5, 4];
		header.CopyTo(buf, 0);
		Util.GetNetworkOrderBytes(Piece).CopyTo(buf, 5);
		return buf;
	}
}
public class Bitfield(BitArray bitfield) : IPeerMessage
{
	public BitArray Data { get; set; } = bitfield;
	public byte[] ToBytes()
	{
		var bitfieldLen = Data.Count / 8;
		if (Data.Count % 8 != 0)
			bitfieldLen += 1;
		var buf = new byte[5 + bitfieldLen];
		Util.GetNetworkOrderBytes((UInt32)(bitfieldLen + 1)).CopyTo(buf, 0);
		buf[4] = 5;
		Data.CopyTo(buf, 5);
		// Since BitArray.Copy fills bytes from the least significant bit
		// it is needed to reverse all of the bytes of the bitfield.
		for (int i = 5; i < buf.Length; i++)
		{
			buf[i] = Util.BitReverse(buf[i]);
		}
		return buf;
	}
}
public class Request(UInt32 idx, UInt32 begin, UInt32 length) : IPeerMessage
{
	public UInt32 Idx { get; set; } = idx;
	public UInt32 Begin { get; set; } = begin;
	public UInt32 Length { get; set; } = length;
	public byte[] ToBytes()
	{
		var buf = new byte[17];
		byte[] header = [0, 0, 0, 13, 6];
		header.CopyTo(buf, 0);
		Util.GetNetworkOrderBytes(Idx).CopyTo(buf, 5);
		Util.GetNetworkOrderBytes(Begin).CopyTo(buf, 9);
		Util.GetNetworkOrderBytes(Length).CopyTo(buf, 13);
		return buf;
	}
}
public class Piece(Data.Chunk chunk) : IPeerMessage {
	public Data.Chunk Chunk { get; set; } = chunk;
	public byte[] ToBytes()
	{
		var buf = new byte[13 + Chunk.Data.Length];
		Util.GetNetworkOrderBytes(9 + (UInt32)Chunk.Data.Length).CopyTo(buf, 0);
		buf[4] = 7;
		Util.GetNetworkOrderBytes(Chunk.Idx).CopyTo(buf, 5);
		Util.GetNetworkOrderBytes(Chunk.Begin).CopyTo(buf, 9);
		Chunk.Data.CopyTo(buf, 13);
		return buf;
	}
	public Piece(UInt32 idx, UInt32 begin, byte[] data) : this(new Chunk(idx, begin, data))
	{ }
}
public class Cancel(UInt32 idx, UInt32 begin, UInt32 length) : IPeerMessage
{
	public UInt32 Idx { get; set; } = idx;
	public UInt32 Begin { get; set; } = begin;
	public UInt32 Length { get; set; } = length;
	public byte[] ToBytes()
	{
		var buf = new byte[17];
		byte[] header = [0, 0, 0, 13, 8];
		header.CopyTo(buf, 0);
		Util.GetNetworkOrderBytes(Idx).CopyTo(buf, 5);
		Util.GetNetworkOrderBytes(Begin).CopyTo(buf, 9);
		Util.GetNetworkOrderBytes(Length).CopyTo(buf, 13);
		return buf;
	}
}
