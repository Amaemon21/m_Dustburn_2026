using System;

public readonly struct DecorKey : IEquatable<DecorKey>
{
    public int Layer { get; }
    public int CellX { get; }
    public int CellY { get; }

    public DecorKey(int layer, int cellX, int cellY)
    {
        Layer = layer;
        CellX = cellX;
        CellY = cellY;
    }

    public bool Equals(DecorKey other) => Layer == other.Layer && CellX == other.CellX && CellY == other.CellY;
    public override bool Equals(object obj) => obj is DecorKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Layer, CellX, CellY);
    public override string ToString() => $"decor/{Layer}/{CellX}/{CellY}";
}
