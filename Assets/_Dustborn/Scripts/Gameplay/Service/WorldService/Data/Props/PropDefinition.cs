using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PropDefinition
{
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public PropContext Context { get; private set; } = PropContext.Yard;
    [field: SerializeField, Min(0f)] public float Weight { get; private set; } = 1f;
    [field: SerializeField, Min(0.1f)] public float Radius { get; private set; } = 0.5f;
    [field: SerializeField] public float GroundOffset { get; private set; }
    [field: SerializeField] public bool OnRoad { get; private set; }
    [field: SerializeField] public bool AlongRoad { get; private set; }
    [field: SerializeField, Range(0f, 180f)] public float YawJitter { get; private set; } = 180f;
    [field: SerializeField] public bool TiltToGround { get; private set; } = true;
    [field: SerializeField, Range(0f, 60f)] public float MaxSlope { get; private set; } = 20f;
    [field: SerializeField, Min(1)] public int ClusterMin { get; private set; } = 1;
    [field: SerializeField, Min(1)] public int ClusterMax { get; private set; } = 1;
    [field: SerializeField, Min(0f)] public float ClusterRadius { get; private set; } = 3f;
    [field: SerializeField] public List<BiomeType> Biomes { get; private set; } = new();

    public bool IsUnset => Prefab == null && Context == 0 && Weight == 0f && Radius == 0f && MaxSlope == 0f && ClusterMin == 0;

    public PropDefinition()
    {
    }

    public PropDefinition(GameObject prefab, PropContext context, float weight, bool onRoad, bool alongRoad, float yawJitter,
        float maxSlope, int clusterMin, int clusterMax, float clusterRadius, IEnumerable<BiomeType> biomes)
    {
        Prefab = prefab;
        Context = context;
        Weight = weight;
        OnRoad = onRoad;
        AlongRoad = alongRoad;
        YawJitter = yawJitter;
        MaxSlope = maxSlope;
        ClusterMin = clusterMin;
        ClusterMax = clusterMax;
        ClusterRadius = clusterRadius;
        Biomes = biomes == null ? new List<BiomeType>() : new List<BiomeType>(biomes);
    }

    public bool Fits(PropContext context) => (Context & context) != 0;

    public bool Allows(BiomeType biome) => Biomes == null || Biomes.Count == 0 || Biomes.Contains(biome);

    public void ApplyDefaults()
    {
        Context = PropContext.Yard;
        Weight = 1f;
        Radius = 0.5f;
        YawJitter = 180f;
        TiltToGround = true;
        MaxSlope = 20f;
        ClusterMin = 1;
        ClusterMax = 1;
        ClusterRadius = 3f;
        Biomes ??= new List<BiomeType>();
    }

    public void Validate()
    {
        ClusterMin = Mathf.Max(1, ClusterMin);
        ClusterMax = Mathf.Max(ClusterMin, ClusterMax);
        Biomes ??= new List<BiomeType>();
    }

    public void ClearPrefab() => Prefab = null;

    public void SetMeasurements(float radius, float groundOffset)
    {
        Radius = Mathf.Max(0.1f, radius);
        GroundOffset = groundOffset;
    }
}
