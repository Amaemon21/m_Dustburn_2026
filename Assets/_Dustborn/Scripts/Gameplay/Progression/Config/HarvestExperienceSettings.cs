using System;
using UnityEngine;

[Serializable]
public sealed class HarvestExperienceSettings
{
    [field: SerializeField, Min(0f)] public float PerDamage { get; private set; } = 0.2f;
    [field: SerializeField, Min(0f)] public float PerDestroyedHealth { get; private set; } = 0.1f;

    public float For(DamageResult result, int maxHealth)
    {
        if (result.Dealt <= 0)
            return 0f;

        float experience = result.Dealt * PerDamage;
        return result.Destroyed ? experience + maxHealth * PerDestroyedHealth : experience;
    }
}
