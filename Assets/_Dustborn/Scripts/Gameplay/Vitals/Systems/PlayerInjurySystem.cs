using System;
using UnityEngine;

public sealed class PlayerInjurySystem : IPlayerMovementModifier
{
    private readonly PlayerEffectSettings _settings;
    private readonly PlayerEffects _effects;
    private readonly PlayerDamageable _player;
    private readonly DamageService _damage;

    public PlayerInjurySystem(PlayerEffectSettings settings, PlayerEffects effects, PlayerDamageable player, DamageService damage)
    {
        _settings = settings;
        _effects = effects;
        _player = player;
        _damage = damage;
    }

    public void Modify(ref PlayerMovementRequest request)
    {
        request.SpeedMultiplier *= 1f - _effects.FractureSlowdown;
    }

    public void Apply(in PlayerMovementResult result, float deltaTime)
    {
        float speed = result.LandingSpeed;

        if (!result.Landed || deltaTime <= 0f || !float.IsFinite(speed) || speed <= _settings.SafeLandingSpeed || _player.IsDestroyed)
            return;

        int damage = Mathf.RoundToInt((speed - _settings.SafeLandingSpeed) * _settings.FallDamagePerSpeed);
        DamageResult impact = _damage.Apply(_player, new DamageHit(damage, Array.Empty<MaterialBonus>(), Vector3.zero, Vector3.up));

        if (impact.Destroyed)
            return;

        if (speed >= _settings.BleedingLandingSpeed)
            _effects.ApplyBleeding(_settings.BleedingDamagePerSecond, _settings.BleedingDuration);

        if (speed >= _settings.FractureLandingSpeed)
            _effects.ApplyFracture(_settings.FractureSlowdown, _settings.FractureDuration);
    }
}
