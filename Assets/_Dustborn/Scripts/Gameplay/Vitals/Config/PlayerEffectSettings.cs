using System;
using UnityEngine;

[Serializable]
public sealed class PlayerEffectSettings
{
    [field: SerializeField, Header("Falling"), Min(0f)] public float SafeLandingSpeed { get; private set; } = 10f;
    [field: SerializeField, Min(0f)] public float FallDamagePerSpeed { get; private set; } = 5f;
    [field: SerializeField, Min(0f)] public float BleedingLandingSpeed { get; private set; } = 12f;
    [field: SerializeField, Min(0f)] public float FractureLandingSpeed { get; private set; } = 15f;

    [field: SerializeField, Header("Conditions"), Min(0f)] public float BleedingDuration { get; private set; } = 20f;
    [field: SerializeField, Min(0f)] public float BleedingDamagePerSecond { get; private set; } = 0.75f;
    [field: SerializeField, Min(0f)] public float FractureDuration { get; private set; } = 60f;
    [field: SerializeField, Range(0f, 1f)] public float FractureSlowdown { get; private set; } = 0.3f;
    [field: SerializeField, Range(0f, 1f)] public float InfectionStaminaRegen { get; private set; } = 0.8f;
}
