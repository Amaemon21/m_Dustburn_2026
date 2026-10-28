using System;
using R3;

public sealed class StaminaSystem : IPlayerStamina, IPlayerMovementModifier, IPlayerVitalsSystem, IDisposable
{
    private const float MIN_EXHAUSTION_RECOVERY = 0.1f;

    private readonly StaminaSettings _settings;
    private readonly Vital _stamina;
    private readonly Stat _regen;
    private readonly Stat _cost;
    private readonly ReactiveProperty<bool> _exhausted = new(false);
    private readonly Subject<float> _spent = new();
    private float _sinceSpent = float.MaxValue;
    private bool _spentSinceTick;

    public ReadOnlyReactiveProperty<bool> IsExhausted => _exhausted;
    public Observable<float> Spent => _spent;

    public StaminaSystem(StaminaSettings settings, PlayerVitals vitals, PlayerStatSheet stats)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _stamina = vitals?.Stamina ?? throw new ArgumentNullException(nameof(vitals));
        _regen = stats.Get(PlayerStatType.StaminaRegen);
        _cost = stats.Get(PlayerStatType.StaminaCost);
    }

    public bool CanSpend(float amount) => amount <= 0f || _stamina.Current.CurrentValue >= Scaled(amount);

    public void Spend(float amount)
    {
        if (amount <= 0f)
            return;

        _sinceSpent = 0f;
        _spentSinceTick = true;
        float spent = _stamina.Remove(Scaled(amount));
        if (_stamina.IsEmpty)
            _exhausted.Value = true;
        if (spent > 0f)
            _spent.OnNext(spent);
    }

    public void Modify(ref PlayerMovementRequest request)
    {
        if (request.Sprint && (_exhausted.Value || _stamina.IsEmpty))
            request.Sprint = false;
        if (request.Jump && !CanSpend(_settings.JumpCost))
            request.Jump = false;
    }

    public void Apply(in PlayerMovementResult result, float deltaTime)
    {
        if (result.Jumped)
            Spend(_settings.JumpCost);
        if (result.IsSprinting && result.IsMoving)
            Spend(_settings.SprintCostPerSecond * deltaTime);
    }

    public void Tick(float deltaTime)
    {
        bool resting = !_spentSinceTick;
        _spentSinceTick = false;
        _sinceSpent += deltaTime;

        if (resting && _sinceSpent >= _settings.RegenDelay)
            _stamina.Add(_regen.Value.CurrentValue * deltaTime);

        if (_exhausted.Value && _stamina.Fraction >= RecoveryShare)
            _exhausted.Value = false;
    }

    private float RecoveryShare => Math.Max(_settings.ExhaustionRecovery, MIN_EXHAUSTION_RECOVERY);

    private float Scaled(float amount) => amount * _cost.Value.CurrentValue;

    public void Dispose()
    {
        _exhausted.Dispose();
        _spent.Dispose();
    }
}
