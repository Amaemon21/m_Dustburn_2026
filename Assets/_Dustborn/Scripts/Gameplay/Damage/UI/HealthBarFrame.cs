using System;

public readonly struct HealthBarFrame : IEquatable<HealthBarFrame>
{
    public int Health { get; }
    public int MaxHealth { get; }
    public int Target { get; }
    public float Fill => MaxHealth <= 0 ? 0f : Math.Clamp((float)Health / MaxHealth, 0f, 1f);

    public HealthBarFrame(int health, int maxHealth, int target)
    {
        Health = health;
        MaxHealth = maxHealth;
        Target = target;
    }

    public bool Equals(HealthBarFrame other) => Health == other.Health && MaxHealth == other.MaxHealth && Target == other.Target;
    public override bool Equals(object obj) => obj is HealthBarFrame other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Health, MaxHealth, Target);
}
