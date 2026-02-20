using System;

namespace BitAvalanche.Core.Data;

public sealed class Piece
{
    public int Idx { get; }
    public byte[] Data { get; }
    public Piece(int idx, byte[] data)
    {
        Idx = idx;
        Data = data;
    }
}
