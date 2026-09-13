using System;

public class UnitHealth : IDamageable
{
    private readonly UnitConfig _config;
    
    private int _maxHealth;
    private int _currentHealth;

    public event Action OnUnitDead;
    
    public bool IsAlive { get; private set; }

    public UnitHealth(UnitConfig config)
    {
        _config = config;

        _maxHealth = _config.MaxHealth;
        _currentHealth = _maxHealth;

        IsAlive = true;
    }

    public void TakeDamage(int value)
    {
        if (value > 0)
        {
            _currentHealth -= value;
        }

        if (_currentHealth <= 0)
        {
            IsAlive = false;
            OnUnitDead?.Invoke();
        }
    }
}