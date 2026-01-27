using System;
using System.Net;
using Avalonia.Data.Converters;

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

	public static string SizeToString(long byteCount)
	{
		if (byteCount == 0)
			return "0B";
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		var scale = Math.Floor(Math.Log((double)byteCount, 1000.0));
		if (scale > units.Length)
			scale = units.Length;
		var size = (double)byteCount / (Math.Pow(1000.0, scale));
		return $"{size:0.##}{units[(int)scale]}";
	}
	public static FuncValueConverter<int?, string?> SizeConverter { get; } =
		new FuncValueConverter<int?, string?>(value =>
		{
			if (value is null)
				value = 0;
			return Util.SizeToString((long)value);
		});
	public static FuncValueConverter<long?, string?> LongSizeConverter { get; } =
		new FuncValueConverter<long?, string?>(value =>
		{
			if (value is null)
				value = 0;
			return Util.SizeToString((long)value);
		});
}
