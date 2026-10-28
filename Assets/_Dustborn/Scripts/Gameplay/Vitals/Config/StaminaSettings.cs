using System;
using UnityEngine;

[Serializable]
public sealed class StaminaSettings
{
    [field: SerializeField, Min(1f)] public float Max { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float RegenPerSecond { get; private set; } = 20f;
    [field: SerializeField, Min(0f)] public float RegenDelay { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float SprintCostPerSecond { get; private set; } = 12f;
    [field: SerializeField, Min(0f)] public float JumpCost { get; private set; } = 10f;
    [field: SerializeField, Range(0f, 1f)] public float ExhaustionRecovery { get; private set; } = 0.25f;
}
