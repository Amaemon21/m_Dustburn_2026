using System;
using R3;

public sealed class MetabolismSystem : IPlayerVitalsSystem, IDisposable
{
    private const float SECONDS_PER_MINUTE = 60f;

    private readonly MetabolismSettings _settings;
    private readonly PlayerVitals _vitals;
    private readonly Stat _foodDecay;
    private readonly Stat _waterDecay;
    private readonly StatModifierSwitch _hungry;
    private readonly StatModifierSwitch _thirsty;
    private readonly IDisposable _exertion;

    public bool IsHungry => _hungry.IsActive;
    public bool IsThirsty => _thirsty.IsActive;

    public MetabolismSystem(MetabolismSettings settings, PlayerVitals vitals, PlayerStatSheet stats, IPlayerStamina stamina)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
        _foodDecay = stats.Get(PlayerStatType.FoodDecay);
        _waterDecay = stats.Get(PlayerStatType.WaterDecay);

        Stat staminaRegen = stats.Get(PlayerStatType.StaminaRegen);
        _hungry = new StatModifierSwitch(staminaRegen,
            new StatModifier(StatModifierType.Multiplier, settings.LowSupplyStaminaRegen, vitals.Food));
        _thirsty = new StatModifierSwitch(staminaRegen,
            new StatModifier(StatModifierType.Multiplier, settings.LowSupplyStaminaRegen, vitals.Water));
        _exertion = stamina.Spent.Subscribe(Exert);
        RefreshConditions();
    }

    public void Tick(float deltaTime)
    {
        _vitals.Food.Remove(_foodDecay.Value.CurrentValue / SECONDS_PER_MINUTE * deltaTime);
        _vitals.Water.Remove(_waterDecay.Value.CurrentValue / SECONDS_PER_MINUTE * deltaTime);
        RefreshConditions();

        if (_vitals.Food.IsEmpty)
            _vitals.Health.Remove(_settings.StarvationDamagePerSecond * deltaTime);
        if (_vitals.Water.IsEmpty)
            _vitals.Health.Remove(_settings.DehydrationDamagePerSecond * deltaTime);
    }

    private void Exert(float stamina)
    {
        _vitals.Food.Remove(stamina * _settings.FoodPerStamina);
        _vitals.Water.Remove(stamina * _settings.WaterPerStamina);
        RefreshConditions();
    }

    private void RefreshConditions()
    {
        _hungry.Set(_vitals.Food.Fraction < _settings.HungryThreshold);
        _thirsty.Set(_vitals.Water.Fraction < _settings.ThirstyThreshold);
    }

    public void Dispose() => _exertion.Dispose();
}
