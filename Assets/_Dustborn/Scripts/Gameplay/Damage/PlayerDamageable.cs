using System;
using R3;

public sealed class PlayerDamageable : IDamageable, IDisposable
{
    private readonly Vital _health;
    private readonly PlayerEffects _effects;
    private readonly ReactiveProperty<int> _displayHealth;
    private readonly IDisposable _subscription;

    public DamageTargetKind Kind => DamageTargetKind.Player;
    public BlockMaterial Material => null;
    public int MaxHealth => (int)Math.Ceiling(_health.Max.CurrentValue);
    public ReadOnlyReactiveProperty<int> Health => _displayHealth;
    public bool IsDestroyed => _health.IsEmpty;

    public PlayerDamageable(PlayerVitals vitals, PlayerEffects effects)
    {
        _health = vitals.Health;
        _effects = effects;
        _displayHealth = new ReactiveProperty<int>((int)Math.Ceiling(_health.Current.CurrentValue));
        _subscription = _health.Current.Subscribe(value => _displayHealth.Value = (int)Math.Ceiling(value));
    }

    public DamageResult TakeDamage(int damage)
    {
        int before = _displayHealth.Value;
        _health.Remove(damage * (1f - _effects.DamageProtection));
        return new DamageResult(before - _displayHealth.Value, IsDestroyed);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _displayHealth.Dispose();
    }
}
