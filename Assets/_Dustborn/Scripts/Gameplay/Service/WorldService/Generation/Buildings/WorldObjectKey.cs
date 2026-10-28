using System;

public readonly struct WorldObjectKey : IEquatable<WorldObjectKey>
{
    public const string POI = "poi";
    public const string PROP = "prop";

    public string Kind { get; }
    public int Index { get; }

    public WorldObjectKey(string kind, int index)
    {
        Kind = kind;
        Index = index;
    }

    public static WorldObjectKey Poi(int placement) => new(POI, placement);
    public static WorldObjectKey Prop(int placement) => new(PROP, placement);

    public bool Equals(WorldObjectKey other) => Kind == other.Kind && Index == other.Index;
    public override bool Equals(object obj) => obj is WorldObjectKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Index);
    public override string ToString() => $"{Kind}/{Index}";
}
