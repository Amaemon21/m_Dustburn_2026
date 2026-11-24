using System;
using R3;

public sealed class HudPlayerEffectsViewModel : ViewModel
{
    private const double PERCENT = 100d;
    private const int SECONDS_PER_MINUTE = 60;
    private readonly PlayerEffects _effects;

    public HudPlayerEffectsViewModel(PlayerEffects effects)
    {
        _effects = effects;
    }

    public Observable<string> ObserveLabel(PlayerEffectType type)
    {
        return _effects.Changed.Select(_ => ReadValue(type))
            .Prepend(ReadValue(type))
            .DistinctUntilChanged()
            .Select(value => Format(type, value));
    }

    private int ReadValue(PlayerEffectType type)
    {
        if (type == PlayerEffectType.Infection)
        {
            double percent = _effects.InfectionFraction * PERCENT;
            return percent > 0d ? Math.Max(1, (int)Math.Round(percent, MidpointRounding.AwayFromZero)) : 0;
        }

        return (int)Math.Min(Math.Ceiling(_effects.Remaining(type)), int.MaxValue);
    }

    private static string Format(PlayerEffectType type, int value)
    {
        if (value <= 0)
            return string.Empty;

        return type == PlayerEffectType.Infection ? $"{value}%" : $"{value / SECONDS_PER_MINUTE:00}:{value % SECONDS_PER_MINUTE:00}";
    }
}
