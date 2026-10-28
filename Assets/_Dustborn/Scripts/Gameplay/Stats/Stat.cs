using System;
using System.Collections.Generic;
using R3;

public sealed class Stat : IReadOnlyStat, IDisposable
{
    private readonly List<StatModifier> _modifiers = new();
    private readonly ReactiveProperty<float> _value;
    private readonly float _minValue;
    private float _baseValue;

    public ReadOnlyReactiveProperty<float> Value => _value;
    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    public float BaseValue
    {
        get => _baseValue;
        set
        {
            _baseValue = value;
            Recalculate();
        }
    }

    public Stat(float baseValue, float minValue = float.MinValue)
    {
        _baseValue = baseValue;
        _minValue = minValue;
        _value = new ReactiveProperty<float>(Math.Max(minValue, baseValue));
    }

    public void AddModifier(StatModifier modifier)
    {
        _modifiers.Add(modifier);
        Recalculate();
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        if (!_modifiers.Remove(modifier))
            return false;
        Recalculate();
        return true;
    }

    public int RemoveModifiersFrom(object source)
    {
        int removed = _modifiers.RemoveAll(modifier => ReferenceEquals(modifier.Source, source));
        if (removed > 0)
            Recalculate();
        return removed;
    }

    private void Recalculate()
    {
        float flat = 0f;
        float percent = 0f;
        float multiplier = 1f;
        foreach (StatModifier modifier in _modifiers)
        {
            switch (modifier.Type)
            {
                case StatModifierType.Flat:
                    flat += modifier.Value;
                    break;
                case StatModifierType.Percent:
                    percent += modifier.Value;
                    break;
                case StatModifierType.Multiplier:
                    multiplier *= modifier.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifier), modifier.Type, "Unknown stat modifier type");
            }
        }

        _value.Value = Math.Max(_minValue, (_baseValue + flat) * (1f + percent) * multiplier);
    }

    public void Dispose() => _value.Dispose();
}
