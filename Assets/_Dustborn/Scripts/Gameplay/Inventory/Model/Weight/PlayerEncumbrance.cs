using System;
using System.Collections.Generic;
using R3;

public sealed class PlayerEncumbrance : IPlayerMovementModifier, IGameplayActivatable, IDisposable
{
    private readonly IReadOnlyList<CarryZone> _zones;
    private readonly Stat _staminaCost;
    private readonly Stat _staminaRegen;
    private readonly PlayerMovementModifiers _movement;
    private readonly ReactiveProperty<CarryPenalty> _penalty = new(CarryPenalty.None);
    private readonly IDisposable _subscription;

    public ReadOnlyReactiveProperty<CarryPenalty> Penalty => _penalty;

    public PlayerEncumbrance(IReadOnlyInventoryWeight weight, IReadOnlyList<CarryZone> zones, PlayerStatSheet stats,
        PlayerMovementModifiers movement)
    {
        if (weight == null)
            throw new ArgumentNullException(nameof(weight));
        _zones = zones;
        _staminaCost = stats.Get(PlayerStatType.StaminaCost);
        _staminaRegen = stats.Get(PlayerStatType.StaminaRegen);
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _subscription = weight.Load.Subscribe(Refresh);
    }

    public void Activate() => _movement.Add(this);

    public void Modify(ref PlayerMovementRequest request)
    {
        CarryPenalty penalty = _penalty.Value;
        if (!penalty.CanSprint)
            request.Sprint = false;
        if (!penalty.CanJump)
            request.Jump = false;
        request.SpeedMultiplier *= request.Sprint ? penalty.SprintSpeed : penalty.WalkSpeed;
    }

    public void Apply(in PlayerMovementResult result, float deltaTime)
    {
    }

    private void Refresh(float load)
    {
        CarryPenalty penalty = CarryPenalty.At(_zones, load);
        _penalty.Value = penalty;
        SetMultiplier(_staminaCost, penalty.StaminaCost);
        SetMultiplier(_staminaRegen, penalty.StaminaRegen);
    }

    private void SetMultiplier(Stat stat, float value)
    {
        stat.RemoveModifiersFrom(this);
        if (value != 1f)
            stat.AddModifier(new StatModifier(StatModifierType.Multiplier, value, this));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _movement.Remove(this);
        _staminaCost.RemoveModifiersFrom(this);
        _staminaRegen.RemoveModifiersFrom(this);
        _penalty.Dispose();
    }
}
