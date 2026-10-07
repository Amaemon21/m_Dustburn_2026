using System;
using R3;

public class UnitHealth : IDamageable, IDisposable
{
    private readonly UnitConfig _config;
    private readonly ReactiveProperty<int> _health;

    public event Action OnUnitDead;

    public DamageTargetKind Kind => DamageTargetKind.Entity;
    public BlockMaterial Material => null;
    public int MaxHealth => _config.MaxHealth;
    public ReadOnlyReactiveProperty<int> Health => _health;
    public bool IsDestroyed => _health.Value <= 0;
    public bool IsAlive => !IsDestroyed;

    public UnitHealth(UnitConfig config)
    {
        _config = config;
        _health = new ReactiveProperty<int>(Math.Max(1, config.MaxHealth));
    }

    public DamageResult TakeDamage(int damage)
    {
        int health = _health.Value;
        if (health <= 0 || damage <= 0)
            return new DamageResult(0, health <= 0);

        int dealt = Math.Min(damage, health);
        _health.Value = health - dealt;
        if (_health.Value <= 0)
            OnUnitDead?.Invoke();

        return new DamageResult(dealt, _health.Value <= 0);
    }

    public void Dispose() => _health.Dispose();
}
