using BitAvalanche.Core;

namespace Core;

public class UtilTests
{
    [Fact]
    public void GenerateRandomStringTest()
    {
        var len = 20;
        var str1 = Util.GenerateRandomString(len);
        var str2 = Util.GenerateRandomString(len);
        Assert.Equal(len, str1.Length);
        Assert.Equal(len, str2.Length);
        Assert.NotEqual(str1, str2);
    }
    [Fact]
    public void NetworkOrderByteParseTest()
    {
        var bytes = new byte[] { 0, 0, 0, 255 };
        UInt32 num = Util.FromNetworkOrderBytes(bytes, 0);
        Assert.Equal(255u, num);

        bytes = new byte[] { 1, 2, 3, 4 };
        num = Util.FromNetworkOrderBytes(bytes, 0);
        UInt32 res = 1 << 24 | 2 << 16 | 3 << 8 | 4;
        Assert.Equal(res, num);
    }
    [Fact]
    public void BitReverseTest()
    {
        byte b = 0b01010101;
        byte rev = Util.BitReverse(b);
        Assert.Equal(0b10101010, rev);

        b = 0b00000001;
        rev = Util.BitReverse(b);
        Assert.Equal(0b10000000, rev);
    }
}
