using System;

public readonly struct StatModifier : IEquatable<StatModifier>
{
    public StatModifierType Type { get; }
    public float Value { get; }
    public object Source { get; }

    public StatModifier(StatModifierType type, float value, object source)
    {
        Type = type;
        Value = value;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool Equals(StatModifier other)
        => Type == other.Type && Value.Equals(other.Value) && ReferenceEquals(Source, other.Source);

    public override bool Equals(object obj) => obj is StatModifier other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Type, Value, Source);
}
