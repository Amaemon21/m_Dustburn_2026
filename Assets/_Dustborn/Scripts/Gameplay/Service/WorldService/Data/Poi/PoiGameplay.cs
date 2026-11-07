using System;
using UnityEngine;

public enum PoiCategory
{
    Residential,
    Food,
    Medical,
    Workshop,
    Industrial,
    Military,
    Landmark
}

public enum PoiLootProfile
{
    Household,
    Food,
    Medical,
    Tools,
    Industrial,
    Military,
    None
}

public enum PoiThreatProfile
{
    Quiet,
    Common,
    Infested,
    Elite
}

public enum PoiConnection
{
    Any,
    Road,
    Remote
}

[Serializable]
public sealed class PoiGameplay
{
    [Range(1, 5)] public int Tier = 1;
    public PoiCategory Category;
    public PoiLootProfile LootProfile;
    public PoiThreatProfile ThreatProfile = PoiThreatProfile.Common;
    [Min(0f)] public float MinDistanceFromSpawn;
    public string UniqueGroup = string.Empty;
    public BiomeType[] Biomes = Array.Empty<BiomeType>();
    public PoiConnection RequiredConnection;

    public bool Starter => Tier <= 2 && ThreatProfile <= PoiThreatProfile.Common && MinDistanceFromSpawn <= 0f;

    public bool Allows(BiomeType biome) => Biomes == null || Biomes.Length == 0 || Array.IndexOf(Biomes, biome) >= 0;

    public void Validate()
    {
        if (Tier < 1 || Tier > 5 || !Enum.IsDefined(typeof(PoiCategory), Category) || !Enum.IsDefined(typeof(PoiLootProfile), LootProfile)
            || !Enum.IsDefined(typeof(PoiThreatProfile), ThreatProfile) || !Enum.IsDefined(typeof(PoiConnection), RequiredConnection)
            || float.IsNaN(MinDistanceFromSpawn) || float.IsInfinity(MinDistanceFromSpawn) || MinDistanceFromSpawn < 0f)
            throw new InvalidOperationException("Invalid POI gameplay settings.");

        if (Biomes == null)
            return;

        foreach (BiomeType biome in Biomes)
        {
            if (!Enum.IsDefined(typeof(BiomeType), biome))
                throw new InvalidOperationException("A POI restricts placement to an unknown biome.");
        }
    }

    public PoiGameplay Clone() => new()
    {
        Tier = Tier,
        Category = Category,
        LootProfile = LootProfile,
        ThreatProfile = ThreatProfile,
        MinDistanceFromSpawn = MinDistanceFromSpawn,
        UniqueGroup = UniqueGroup,
        Biomes = Biomes == null || Biomes.Length == 0 ? Array.Empty<BiomeType>() : (BiomeType[])Biomes.Clone(),
        RequiredConnection = RequiredConnection
    };
}
