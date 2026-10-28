using System;

public sealed class HealthRegenSystem : IPlayerVitalsSystem
{
    private readonly HealthSettings _settings;
    private readonly PlayerVitals _vitals;
    private readonly Stat _regen;

    public bool CanRegenerate =>
        !_vitals.Health.IsEmpty && !_vitals.Health.IsFull &&
        _vitals.Food.Fraction >= _settings.RegenFoodThreshold &&
        _vitals.Water.Fraction >= _settings.RegenWaterThreshold;

    public HealthRegenSystem(HealthSettings settings, PlayerVitals vitals, PlayerStatSheet stats)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
        _regen = stats.Get(PlayerStatType.HealthRegen);
    }

    public void Tick(float deltaTime)
    {
        if (CanRegenerate)
            _vitals.Health.Add(_regen.Value.CurrentValue * deltaTime);
    }
}
