using System;

public class UnitHealth : IDamageable
{
    private readonly UnitProperty _property;
    
    private int _maxHealth;
    private int _currentHealth;

    public event Action OnUnitDead;
    
    public bool IsAlive { get; private set; }

    public UnitHealth(UnitProperty property)
    {
        _property = property;

        _maxHealth = _property.MaxHealth;

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
            OnUnitDead?.Invoke();

            IsAlive = false;
        }
    }
}