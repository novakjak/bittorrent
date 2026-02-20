using System;

namespace BitAvalanche.Core;

public sealed class ParseException : Exception
{
    public ParseException(string message) : base(message) { }
}
