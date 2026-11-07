using System;
using NaughtyAttributes;
using UnityEngine;

public partial class WorldGenerationConfig
{
    private const float FEW_TOWNS = 0.5f;
    private const float MANY_TOWNS = 1.6f;
    private const float FEW_WILDERNESS = 0.5f;
    private const float MANY_WILDERNESS = 1.5f;
    private const float FEW_RIVERS = 0.5f;
    private const float MANY_RIVERS = 1.8f;
    private const float FEW_LAKES = 0.5f;
    private const float MANY_LAKES = 1.8f;
    private const float SETTLEMENT_AREA_EXPONENT = 0.75f;
    private const int NORMAL_CRATERS = 3;
    private const int NORMAL_RIFTS = 3;
    private const int NORMAL_CANYONS = 3;

    [field: SerializeField, Foldout("Menu variation")] public BiomeLayout BiomeLayout { get; private set; } = BiomeLayout.Random;
    [field: SerializeField, Foldout("Menu variation")] public float[] BiomeShares { get; private set; } = Array.Empty<float>();
    [field: SerializeField, Foldout("Menu variation")] public bool ReliefSharesEnabled { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), Range(0f, 1f)] public float PlainsShare { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), Range(0f, 1f)] public float HillsShare { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), Range(0f, 1f)] public float MountainsShare { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), MinValue(0)] public int CraterCount { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), MinValue(0)] public int RiftCount { get; private set; }
    [field: SerializeField, Foldout("Menu variation"), MinValue(0)] public int CanyonCount { get; private set; }
    [field: SerializeField, Foldout("Menu variation")] public bool RandomRifts { get; private set; } = true;
    [field: SerializeField, Foldout("Menu variation")] public bool RandomCanyons { get; private set; } = true;

    public bool HasBiomeShares => BiomeShares != null && BiomeShares.Length > 0;

    public float BiomeShare(BiomeType type)
    {
        int index = (int)type;
        return HasBiomeShares && index < BiomeShares.Length ? BiomeShares[index] : 0f;
    }

    public void Apply(WorldVariation variation)
    {
        variation.Validate();

        Seed = variation.Seed;
        WorldSize = variation.WorldSize;

        ScaleFrequencies(variation.SizeScale);
        ApplyRelief(variation.Relief());
        BiomeLayout = variation.Layout;
        BiomeShares = variation.BiomeShares();

        float settlementArea = Mathf.Pow(variation.AreaScale, SETTLEMENT_AREA_EXPONENT);
        ScaleSettlements(WorldVariation.Factor(variation.Towns, FEW_TOWNS, MANY_TOWNS) * settlementArea, variation.Towns != WorldAmount.None);

        float wilderness = WorldVariation.Factor(variation.Wilderness, FEW_WILDERNESS, MANY_WILDERNESS);
        DirtChance = Mathf.Clamp01(DirtChance * wilderness);
        FarmsteadChance = Mathf.Clamp01(FarmsteadChance * wilderness);

        float smallWorldRivers = 1f / Mathf.Min(1f, variation.SizeScale);
        Water.Vary(WorldVariation.Factor(variation.Rivers, FEW_RIVERS, MANY_RIVERS) * smallWorldRivers,
            WorldVariation.Factor(variation.Lakes, FEW_LAKES, MANY_LAKES));

        CraterCount = WorldVariation.Count(variation.Craters, NORMAL_CRATERS, variation.AreaScale);
        RiftCount = WorldVariation.Count(variation.Rifts, NORMAL_RIFTS, variation.AreaScale);
        CanyonCount = WorldVariation.Count(variation.Canyons, NORMAL_CANYONS, variation.AreaScale);
        RandomRifts = variation.Rifts != WorldAmount.None;
        RandomCanyons = variation.Canyons != WorldAmount.None;
    }

    private void ApplyRelief(ReliefShares shares)
    {
        ReliefSharesEnabled = true;
        PlainsShare = shares.Plains;
        HillsShare = shares.Hills;
        MountainsShare = shares.Mountains;
    }

    private void ScaleFrequencies(float sizeScale)
    {
        ContinentFrequency *= sizeScale;
        HillFrequency *= sizeScale;
        RidgeFrequency *= sizeScale;
        DuneFrequency *= sizeScale;
        DetailFrequency *= sizeScale;
        MountainMaskFrequency *= sizeScale;
    }

    private void ScaleSettlements(float factor, bool keepRoads)
    {
        int minimum = keepRoads ? 1 : 0;

        CityProfile.ScaleCount(factor, 0);
        TownProfile.ScaleCount(factor, 0);
        CountryTownProfile.ScaleCount(factor, minimum);
        GhostTownProfile.ScaleCount(factor, minimum);
    }
}
