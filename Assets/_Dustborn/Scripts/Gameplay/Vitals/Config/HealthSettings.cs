using System;
using UnityEngine;

[Serializable]
public sealed class HealthSettings
{
    [field: SerializeField, Min(1f)] public float Max { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float RegenPerSecond { get; private set; } = 0.5f;
    [field: SerializeField, Range(0f, 1f)] public float RegenFoodThreshold { get; private set; } = 0.5f;
    [field: SerializeField, Range(0f, 1f)] public float RegenWaterThreshold { get; private set; } = 0.5f;
}
