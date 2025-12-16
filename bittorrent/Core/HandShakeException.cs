using System;

namespace bittorrent.Core;

public class HandShakeException : Exception
{
    public HandShakeException(string message) : base(message) { }
}
