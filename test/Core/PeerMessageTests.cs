using System.Collections;

using BitAvalanche.Core;

namespace test.Core;

public class PeerMessageTests
{
    [Fact]
    public void ParseKeepAlive()
    {
        var data = new byte[] { 0, 0, 0, 0 };
        var msg = PeerMessageParser.Parse(data);

        Assert.NotNull(msg);
        Assert.IsType<KeepAlive>(msg);
    }
    [Fact]
    public void ParseChoke()
    {
        var data = new byte[] { 0, 0, 0, 1, 0 };
        var msg = PeerMessageParser.Parse(data);

        Assert.NotNull(msg);
        Assert.IsType<Choke>(msg);
    }
    [Fact]
    public void ParseUnchoke()
    {
        var data = new byte[] { 0, 0, 0, 1, 1 };
        var msg = PeerMessageParser.Parse(data);

        Assert.NotNull(msg);
        Assert.IsType<Unchoke>(msg);
    }
    [Fact]
    public void ParseInterested()
    {
        var data = new byte[] { 0, 0, 0, 1, 2 };
        var msg = PeerMessageParser.Parse(data);

        Assert.NotNull(msg);
        Assert.IsType<Interested>(msg);
    }
    [Fact]
    public void ParseNotInterested()
    {
        var data = new byte[] { 0, 0, 0, 1, 3 };
        var msg = PeerMessageParser.Parse(data);

        Assert.NotNull(msg);
        Assert.IsType<NotInterested>(msg);
    }
    [Fact]
    public void ParseHave()
    {
        var data = new byte[] { 0, 0, 0, 5, 4, 0, 0, 0, 1 };
        var msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Have>(msg);
        Assert.Equal(1u, (msg as Have)!.Piece);

        data = new byte[] { 0, 0, 0, 5, 4, 0xab, 0xcd, 0xef, 0x01 };
        msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Have>(msg);
        Assert.Equal(0xabcdef01, (msg as Have)!.Piece);

        data = new byte[] { 0, 0, 0, 1, 4 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 3, 4, 0, 0 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 6, 4, 1, 2, 3, 4, 5 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 5, 4, 1, 2 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 5, 4, 1, 2, 3, 4, 5 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
    }
    [Fact]
    public void ParseBitfield()
    {
        // Check parsing *bits* in the correct order.
        var data = new byte[] { 0, 0, 0, 2, 5, 0b10010110 };
        var msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Bitfield>(msg);
        var bitfield = (msg as Bitfield)!.Data;
        Assert.Equal(8, bitfield.Length);
        Assert.True(bitfield[0]);
        Assert.False(bitfield[1]);
        Assert.False(bitfield[2]);
        Assert.True(bitfield[3]);
        Assert.False(bitfield[4]);
        Assert.True(bitfield[5]);
        Assert.True(bitfield[6]);
        Assert.False(bitfield[7]);

        // Check parsing *bytes* in the correct order.
        data = new byte[] { 0, 0, 0, 5, 5, 1, 2, 4, 8 };
        msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Bitfield>(msg);
        bitfield = (msg as Bitfield)!.Data;
        Assert.Equal(4 * 8, bitfield.Length);
        var correctBitIdxs = new List<int>
        {
            1 * 8 - 1,
            2 * 8 - 2,
            3 * 8 - 3,
            4 * 8 - 4,
        };
        for (int i = 0; i < bitfield.Length; i++)
        {
            if (correctBitIdxs.Contains(i))
            {
                Assert.True(bitfield[i]);
            }
            else
            {
                Assert.False(bitfield[i]);
            }
        }

        data = new byte[] { 0, 0, 0, 1, 5 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 5, 5 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[] { 0, 0, 0, 1, 5, 0, 0 };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
    }
    [Fact]
    public void ParseRequest()
    {
        var data = new byte[]
        {
            0, 0, 0, 13,
            6,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        var msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Request>(msg);
        Assert.Equal(1u, (msg as Request)!.Idx);
        Assert.Equal(2u, (msg as Request)!.Begin);
        Assert.Equal(3u, (msg as Request)!.Length);

        data = new byte[]
        {
            0, 0, 0, 13,
            6,
            0, 0, 0, 1,
            0, 0, 0, 2,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 13,
            6,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
            0, 0, 0, 4
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 5,
            6,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 25,
            6,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
    }
    [Fact]
    public void ParsePiece()
    {
        var data = new byte[]
        {
            0, 0, 0, 10,
            7,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0xAB,
        };
        var msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Piece>(msg);
        Assert.Equal(1u, (msg as Piece)!.Chunk.Idx);
        Assert.Equal(2u, (msg as Piece)!.Chunk.Begin);
        Assert.Equal(new byte[] { 0xAB }, (msg as Piece)!.Chunk.Data);

        data = new byte[]
        {
            0, 0, 0, 13,
            7,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0xAB, 0xBC, 0xCD, 0xDE,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Piece>(msg);
        Assert.Equal(1u, (msg as Piece)!.Chunk.Idx);
        Assert.Equal(2u, (msg as Piece)!.Chunk.Begin);
        Assert.Equal(new byte[] { 0xAB, 0xBC, 0xCD, 0xDE }, (msg as Piece)!.Chunk.Data);

        data = new byte[]
        {
            0, 0, 0, 9,
            7,
            0, 0, 0, 1,
            0, 0, 0, 2,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 1,
            7,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 0xFF,
            7,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0xAB, 0xCD, 0xEF, 0x01,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
    }
    [Fact]
    public void ParseCancel()
    {
        var data = new byte[]
        {
            0, 0, 0, 13,
            8,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        var msg = PeerMessageParser.Parse(data);
        Assert.NotNull(msg);
        Assert.IsType<Cancel>(msg);
        Assert.Equal(1u, (msg as Cancel)!.Idx);
        Assert.Equal(2u, (msg as Cancel)!.Begin);
        Assert.Equal(3u, (msg as Cancel)!.Length);

        data = new byte[]
        {
            0, 0, 0, 13,
            8,
            0, 0, 0, 1,
            0, 0, 0, 2,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 13,
            8,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
            0, 0, 0, 4
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 5,
            8,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
        data = new byte[]
        {
            0, 0, 0, 25,
            8,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3,
        };
        msg = PeerMessageParser.Parse(data);
        Assert.Null(msg);
    }


    public static IEnumerable<object[]> GetMessages()
    {
        yield return new object[] { new KeepAlive(), new byte[] { 0, 0, 0, 0 } };
        yield return new object[] { new Choke(), new byte[] { 0, 0, 0, 1, 0 } };
        yield return new object[] { new Unchoke(), new byte[] { 0, 0, 0, 1, 1 } };
        yield return new object[] { new Interested(), new byte[] { 0, 0, 0, 1, 2 } };
        yield return new object[] { new NotInterested(), new byte[] { 0, 0, 0, 1, 3 } };
        yield return new object[] { new Have(13), new byte[] { 0, 0, 0, 5, 4, 0, 0, 0, 13 } };
        var bitfield = new Bitfield(new BitArray(new bool[] { true, false, true }));
        yield return new object[] { bitfield, new byte[] { 0, 0, 0, 2, 5, 0b10100000 } };
        yield return new object[]
        {
            new Request(1, 2, 3),
            new byte[]
            {
                0, 0, 0, 13,
                6,
                0, 0, 0, 1,
                0, 0, 0, 2,
                0, 0, 0, 3,
            }
        };
        yield return new object[]
        {
            new Piece(1, 2, new byte[] { 0x12, 0x34, 0x56, 0x78 }),
            new byte[]
            {
                0, 0, 0, 13,
                7,
                0, 0, 0, 1,
                0, 0, 0, 2,
                0x12, 0x34, 0x56, 0x78,
            }
        };
        yield return new object[]
        {
            new Cancel(1, 2, 3),
            new byte[]
            {
                0, 0, 0, 13,
                8,
                0, 0, 0, 1,
                0, 0, 0, 2,
                0, 0, 0, 3,
            }
        };
    }
    [Theory]
    [MemberData(nameof(GetMessages))]
    public void MessagesToBytes(IPeerMessage message, byte[] expected)
    {
        var got = message.ToBytes();
        Assert.Equal(expected, got);
    }
}
