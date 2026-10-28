using System;
using UnityEngine;

[Serializable]
public sealed class HandsMotionClip
{
    private const float DEFAULT_FADE = 0.15f;
    private const float DEFAULT_NEXT_FADE = 0.1f;

    [field: SerializeField] public HandsMotion Motion { get; private set; }
    [field: SerializeField] public AnimationClip Clip { get; private set; }
    [field: SerializeField] public AnimationClip ItemClip { get; private set; }
    [field: SerializeField, Min(0.01f)] public float Speed { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float FadeIn { get; private set; } = DEFAULT_FADE;
    [field: SerializeField, Min(0f)] public float End { get; private set; }
    [field: SerializeField, Min(0f)] public float Impact { get; private set; }
    [field: SerializeField] public bool Chained { get; private set; }
    [field: SerializeField] public HandsMotion Next { get; private set; }
    [field: SerializeField, Min(0f)] public float NextFade { get; private set; } = DEFAULT_NEXT_FADE;

    public bool IsUnset => Speed <= 0f;

    public float Duration => End > 0f ? End : Clip.length;

    public float ExitTime => Math.Max(0f, Duration - NextFade);

    public float PlayTime => Duration / Speed;

    public float ImpactTime => Impact / Speed;

    public void ApplyDefaults()
    {
        Speed = 1f;
        FadeIn = DEFAULT_FADE;
        NextFade = DEFAULT_NEXT_FADE;
    }
}
