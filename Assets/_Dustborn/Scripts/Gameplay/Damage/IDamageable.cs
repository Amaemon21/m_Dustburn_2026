using R3;

public interface IDamageable
{
    DamageTargetKind Kind { get; }
    BlockMaterial Material { get; }
    int MaxHealth { get; }
    ReadOnlyReactiveProperty<int> Health { get; }
    bool IsDestroyed { get; }

    DamageResult TakeDamage(int damage);
}
