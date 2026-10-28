using System;
using UnityEngine;

[Serializable]
public sealed class DragSwingSettings
{
    [field: SerializeField, Min(0f)] public float TiltPerSpeed { get; private set; } = 0.015f;
    [field: SerializeField, Range(0f, 45f)] public float MaxTilt { get; private set; } = 18f;
    [field: SerializeField, Min(0.01f)] public float TiltFrequency { get; private set; } = 3.5f;
    [field: SerializeField, Range(0f, 2f)] public float TiltDamping { get; private set; } = 0.35f;
    [field: SerializeField, Min(0f)] public float VelocitySmoothing { get; private set; } = 20f;
    [field: SerializeField, Min(0.1f)] public float HeldScale { get; private set; } = 1.12f;
    [field: SerializeField, Min(0.01f)] public float ScaleFrequency { get; private set; } = 5f;
    [field: SerializeField, Range(0f, 2f)] public float ScaleDamping { get; private set; } = 0.45f;
    [field: SerializeField, Min(0f)] public float RestEpsilon { get; private set; } = 0.01f;
}
