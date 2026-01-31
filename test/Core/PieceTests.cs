namespace test.Core.Data;

using bittorrent.Core.Data;

public class PieceTests
{
    [Fact]
    public void PieceTest()
    {
        var data = new byte[] { 0, 1, 2, 3, 4 };
        var idx = 123;
        var piece = new Piece(idx, data);

        Assert.Equal(piece.Idx, idx);
        Assert.Equal(piece.Data, data);
    }
}
