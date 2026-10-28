using System;

public readonly struct VoxelColumnKey : IEquatable<VoxelColumnKey>
{
    public const int FACE_MIN_X = 1;
    public const int FACE_MAX_X = 2;
    public const int FACE_MIN_Z = 4;
    public const int FACE_MAX_Z = 8;
    public const int FACE_ALL = FACE_MIN_X | FACE_MAX_X | FACE_MIN_Z | FACE_MAX_Z;

    public const int MORPH_MIN_X = 1;
    public const int MORPH_MAX_X = 2;
    public const int MORPH_MIN_Z = 4;
    public const int MORPH_MAX_Z = 8;
    public const int MORPH_CORNERS = 256;

    public int X { get; }
    public int Z { get; }
    public int Lod { get; }

    public int Seams { get; }
    public int Morph { get; }

    public VoxelColumnKey(int x, int z, int lod, int seams = 0, int morph = 0)
    {
        X = x;
        Z = z;
        Lod = lod;
        Seams = seams;
        Morph = morph;
    }

    public bool Equals(VoxelColumnKey other)
    {
        return X == other.X && Z == other.Z && Lod == other.Lod
            && Seams == other.Seams && Morph == other.Morph;
    }

    public override bool Equals(object other)
    {
        return other is VoxelColumnKey key && Equals(key);
    }

    public override int GetHashCode()
    {
        return (X * 73856093) ^ (Z * 19349663) ^ (Lod * 83492791)
            ^ (Seams * 2654435761u).GetHashCode() ^ (Morph * 40503u).GetHashCode();
    }

    public override string ToString()
    {
        return $"({X}, {Z}) lod {Lod}";
    }
}
