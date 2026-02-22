using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using BitAvalanche.Core.Data;

namespace BitAvalanche.Core;

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
            case 0:
            {
                if (len != 1) return null;
                return new Choke();
            }
            case 1:
            {
                if (len != 1) return null;
                return new Unchoke();
            }
            case 2:
            {
                if (len != 1) return null;
                return new Interested();
            }
            case 3:
            {
                if (len != 1) return null;
                return new NotInterested();
            }
            case 4:
            {
                if (len != 5)
                    return null;
                var idx = Util.FromNetworkOrderBytes(message, 5);
                return new Have(idx);
            }
            case 5:
            {
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
            case 6:
            {
                if (len != 13) return null;
                var idx = Util.FromNetworkOrderBytes(message, 5);
                var begin = Util.FromNetworkOrderBytes(message, 9);
                var length = Util.FromNetworkOrderBytes(message, 13);
                return new Request(idx, begin, length);
            }
            case 7:
            {
                if (len <= 9) return null;
                var idx = Util.FromNetworkOrderBytes(message, 5);
                var begin = Util.FromNetworkOrderBytes(message, 9);
                if (message.Length != len + 4) return null;
                var chunkLen = len - 9;
                var chunk = new byte[chunkLen];
                Array.Copy(message, 13, chunk, 0, chunkLen);
                return new Piece(idx, begin, chunk);
            }
            case 8:
            {
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
public class Request(UInt32 idx, UInt32 begin, UInt32 length) : IPeerMessage, IEquatable<Request>, IEquatable<Cancel>
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
    public bool Equals(Request? other)
    {
        return other is not null
            && Idx == other.Idx
            && Begin == other.Begin
            && Length == other.Length;
    }
    public bool Equals(Cancel? other)
    {
        return other is not null
            && Idx == other.Idx
            && Begin == other.Begin
            && Length == other.Length;
    }
}
public class Piece(Data.Chunk chunk) : IPeerMessage
{
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
public class Cancel(UInt32 idx, UInt32 begin, UInt32 length) : IPeerMessage, IEquatable<Cancel>, IEquatable<Request>
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
    public bool Equals(Cancel? other)
    {
        return other is not null
            && Idx == other.Idx
            && Begin == other.Begin
            && Length == other.Length;
    }
    public bool Equals(Request? other)
    {
        return other is not null
            && Idx == other.Idx
            && Begin == other.Begin
            && Length == other.Length;
    }
}

// This class does *not* implement IPeerMessage because it is not supposed
// to be sent during already established communication.
public class Handshake
{
    public static string DefaultProtocol = "BitTorrent protocol";
    public static int MessageLength = 49 + DefaultProtocol.Length;

    public string ProtocolName { get; set; }
    public byte[] ProtocolExtensions { get; set; }
    public byte[] InfoHash { get; set; }
    public byte[] PeerId { get; set; }

    public Handshake(byte[] infoHash, byte[] peerId)
    {
        if (infoHash.Length != 20)
            throw new ArgumentException("Info hash is not 20 bytes long.");
        if (peerId.Length != 20)
            throw new ArgumentException("Peer ID is not 20 bytes long.");
        InfoHash = infoHash;
        PeerId = peerId;
        ProtocolExtensions = new byte[8];
        ProtocolName = DefaultProtocol;
    }
    public Handshake(byte[] infoHash, byte[] peerId, byte[] extensions) : this(infoHash, peerId)
    {
        if (extensions.Length != 8)
            throw new ArgumentException("Extensions are not 8 bytes long");
        ProtocolExtensions = extensions;
    }

    public byte[] ToBytes()
    {
        List<byte> buffer = new();
        buffer.Add((byte)ProtocolName.Length);
        buffer.AddRange(Encoding.ASCII.GetBytes(ProtocolName));
        buffer.AddRange(ProtocolExtensions);
        buffer.AddRange(InfoHash);
        buffer.AddRange(PeerId);
        return buffer.ToArray();
    }

    public static Handshake Parse(Memory<byte> handshake)
    {
        int pLen = DefaultProtocol.Length;
        if (handshake.Length != Handshake.MessageLength)
            throw new HandShakeException("Handshake length is invalid");

        if (handshake.Span[0] != pLen)
            throw new HandShakeException("Recieved invalid protocol name length from peer.");
        var protocolRecieved = Encoding.ASCII.GetString(handshake.Slice(1, pLen).ToArray());
        if (protocolRecieved != DefaultProtocol)
            throw new HandShakeException($"Recieved invalid protocol name from peer: {protocolRecieved}");
        // Skip checking reserved bytes (20..27)
        var extensions = handshake.Slice(1 + pLen, 8);
        var infoHash = handshake.Slice(1 + pLen + 8, 20).ToArray();
        var peerId = handshake.Slice(1 + pLen + 8 + 20, 20).ToArray();
        return new Handshake(infoHash, peerId);
    }
}
