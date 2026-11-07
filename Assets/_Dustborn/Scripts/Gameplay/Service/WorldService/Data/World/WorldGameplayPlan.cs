using System;
using UnityEngine;

[Serializable]
public sealed class WorldThreatZone
{
    public int PoiIndex;
    public Vector2 Center;
    public float Radius;
    public int Tier;
    public PoiThreatProfile Profile;
}

[Serializable]
public sealed class WorldGameplayPlan
{
    public const int CURRENT_VERSION = 1;

    public int Version;
    public Vector3 Spawn;
    public float Facing;
    public BiomeType SpawnBiome;
    public float SafeRadius;
    public int[] StarterPois = Array.Empty<int>();
    public WorldThreatZone[] ThreatZones = Array.Empty<WorldThreatZone>();

    public bool HasSpawn => Version == CURRENT_VERSION;

    public void Validate(int worldSize, int placements)
    {
        if (!HasSpawn || !Finite(Spawn.x) || !Finite(Spawn.y) || !Finite(Spawn.z) || !Finite(Facing)
            || Spawn.x < 0f || Spawn.z < 0f || Spawn.x > worldSize || Spawn.z > worldSize
            || !Finite(SafeRadius) || SafeRadius < 0f || SafeRadius > worldSize || StarterPois == null || ThreatZones == null
            || !Enum.IsDefined(typeof(BiomeType), SpawnBiome))
            throw new InvalidOperationException("The world has an invalid gameplay plan.");

        foreach (int index in StarterPois)
        {
            if (index < 0 || index >= placements)
                throw new InvalidOperationException("The start refers to a missing POI.");
        }

        foreach (WorldThreatZone zone in ThreatZones)
        {
            if (zone == null || zone.PoiIndex < 0 || zone.PoiIndex >= placements || zone.Tier < 1 || zone.Tier > 5
                || !Finite(zone.Center.x) || !Finite(zone.Center.y) || !Finite(zone.Radius) || zone.Radius <= 0f
                || zone.Radius > worldSize || zone.Center.x < 0f || zone.Center.y < 0f || zone.Center.x > worldSize || zone.Center.y > worldSize
                || !Enum.IsDefined(typeof(PoiThreatProfile), zone.Profile))
                throw new InvalidOperationException("The world has an invalid threat zone.");
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
