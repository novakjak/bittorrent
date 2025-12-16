using System;
using System.Net;

namespace bittorrent.Core;

public static class Util
{
    const string ALPHANUM = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string GenerateRandomString(int length)
    {
        if (length < 0)
        {
            throw new ArgumentException("Length cannot be negative");
        }

        Random r = new Random();
        string s = "";
        for (int i = 0; i < length; i++)
        {
            s += ALPHANUM[r.Next(ALPHANUM.Length)];
        }
        return s;
    }

	public static byte[] GetNetworkOrderBytes(UInt32 number)
	{
		var bytes = BitConverter.GetBytes(number);
		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);
		return bytes;
	}
	public static UInt32 FromNetworkOrderBytes(byte[] buffer, int offset) =>
		(UInt32)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer, offset));

	public static byte BitReverse(byte b)
	{
		byte res = 0;
		res |= (byte)((b & 1 << 0) << 7);
		res |= (byte)((b & 1 << 1) << 5);
		res |= (byte)((b & 1 << 2) << 3);
		res |= (byte)((b & 1 << 3) << 1);
		res |= (byte)((b & 1 << 4) >> 1);
		res |= (byte)((b & 1 << 5) >> 3);
		res |= (byte)((b & 1 << 6) >> 5);
		res |= (byte)((b & 1 << 7) >> 7);
		return res;
	}
}
