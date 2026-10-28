using System;
using UnityEngine;

[Serializable]
public sealed class MetabolismSettings
{
    [field: SerializeField, Header("Food"), Min(1f)] public float MaxFood { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float FoodDecayPerMinute { get; private set; } = 1.5f;
    [field: SerializeField, Min(0f)] public float FoodPerStamina { get; private set; } = 0.01f;
    [field: SerializeField, Range(0f, 1f)] public float HungryThreshold { get; private set; } = 0.25f;
    [field: SerializeField, Min(0f)] public float StarvationDamagePerSecond { get; private set; } = 0.2f;

    [field: SerializeField, Header("Water"), Min(1f)] public float MaxWater { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float WaterDecayPerMinute { get; private set; } = 2.5f;
    [field: SerializeField, Min(0f)] public float WaterPerStamina { get; private set; } = 0.015f;
    [field: SerializeField, Range(0f, 1f)] public float ThirstyThreshold { get; private set; } = 0.25f;
    [field: SerializeField, Min(0f)] public float DehydrationDamagePerSecond { get; private set; } = 0.3f;

    [field: SerializeField, Header("Low Supplies"), Range(0f, 1f)] public float LowSupplyStaminaRegen { get; private set; } = 0.5f;
}
