using System;
using UnityEngine;

[Serializable]
public sealed class LevelSettings
{
    [field: SerializeField, Min(2)] public int MaxLevel { get; private set; } = 100;
    [field: SerializeField, Min(1)] public int BaseExperience { get; private set; } = 100;
    [field: SerializeField, Min(1f)] public float ExperienceGrowth { get; private set; } = 1.15f;

    public ExperienceCurve CreateCurve() => new(BaseExperience, ExperienceGrowth, MaxLevel);
}
