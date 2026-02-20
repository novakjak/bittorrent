using System;

namespace BitAvalanche.Core;

public class HandShakeException : Exception
{
    public HandShakeException(string message) : base(message) { }
}
