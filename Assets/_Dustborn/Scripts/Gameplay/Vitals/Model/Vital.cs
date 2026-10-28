using System;
using R3;

public sealed class Vital : IReadOnlyVital, IDisposable
{
    private readonly ReactiveProperty<float> _current;
    private readonly IReadOnlyStat _max;
    private readonly IDisposable _maxSubscription;

    public VitalType Type { get; }
    public ReadOnlyReactiveProperty<float> Current => _current;
    public ReadOnlyReactiveProperty<float> Max => _max.Value;
    public float Fraction => MaxValue <= 0f ? 0f : _current.Value / MaxValue;
    public bool IsEmpty => _current.Value <= 0f;
    public bool IsFull => _current.Value >= MaxValue;

    private float MaxValue => _max.Value.CurrentValue;

    public Vital(VitalType type, IReadOnlyStat max, float current)
    {
        Type = type;
        _max = max ?? throw new ArgumentNullException(nameof(max));
        _current = new ReactiveProperty<float>(Clamp(current));
        _maxSubscription = _max.Value.Subscribe(_ => _current.Value = Clamp(_current.Value));
    }

    public void Set(float value) => _current.Value = Clamp(value);

    public float Add(float amount)
    {
        if (amount <= 0f)
            return 0f;

        float before = _current.Value;
        _current.Value = Clamp(before + amount);
        return _current.Value - before;
    }

    public float Remove(float amount)
    {
        if (amount <= 0f)
            return 0f;

        float before = _current.Value;
        _current.Value = Clamp(before - amount);
        return before - _current.Value;
    }

    public void Change(float delta)
    {
        if (delta >= 0f)
            Add(delta);
        else
            Remove(-delta);
    }

    private float Clamp(float value) => Math.Clamp(value, 0f, Math.Max(0f, MaxValue));

    public void Dispose()
    {
        _maxSubscription.Dispose();
        _current.Dispose();
    }
}
