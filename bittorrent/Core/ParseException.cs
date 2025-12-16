using System;

namespace bittorrent.Core;

public sealed class ParseException : Exception
{
    public ParseException(string message) : base(message) {}
}
