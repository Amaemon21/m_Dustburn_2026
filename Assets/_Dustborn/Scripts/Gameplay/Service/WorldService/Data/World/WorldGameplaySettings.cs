using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public sealed class WorldGameplaySettings
{
    public bool Enabled = true;
    [Min(0f)] public float SafeRadius = 100f;
    [Min(0f)] public float StarterMinDistance = 110f;
    [Min(1f)] public float StarterMaxDistance = 300f;
    [Range(0, 8)] public int StarterPoiCount = 2;
    [Range(1f, 45f)] public float MaxWalkSlope = 25f;
    [Min(1f)] public float EdgeMargin = 128f;
    [Min(0f)] public float BiomeMargin = 32f;
}

public partial class WorldGenerationConfig
{
    [field: SerializeField, Foldout("Gameplay planning")]
    [field: Tooltip("Selects a deterministic dry start beside reachable low-tier POI. Invalid starts reject the generated map before it is saved.")]
    public WorldGameplaySettings Gameplay { get; private set; } = new();
}
