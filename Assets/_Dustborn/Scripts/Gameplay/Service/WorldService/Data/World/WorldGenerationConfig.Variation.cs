using UnityEngine;

public partial class WorldGenerationConfig
{
    private const float FEW_TOWNS = 0.5f;
    private const float MANY_TOWNS = 1.6f;
    private const float FEW_FARMSTEADS = 0.5f;
    private const float MANY_FARMSTEADS = 1.5f;
    private const float FEW_RIVERS = 0.5f;
    private const float MANY_RIVERS = 1.8f;
    private const float FEW_LAKES = 0.5f;
    private const float MANY_LAKES = 1.8f;
    private const float FEW_MOUNTAINS_SHIFT = 0.1f;
    private const float MANY_MOUNTAINS_SHIFT = -0.08f;
    private const float NO_MOUNTAINS_LOW = 0.96f;
    private const float MOUNTAIN_MASK_GAP = 0.04f;
    private const float PLAINS_RELIEF = 0.6f;
    private const float ROLLING_RELIEF = 0.8f;
    private const float SETTLEMENT_AREA_EXPONENT = 0.75f;

    public void Apply(WorldVariation variation)
    {
        variation.Validate();

        Seed = variation.Seed;
        WorldSize = variation.WorldSize;

        ScaleFrequencies(variation.SizeScale);
        ReliefScale *= ReliefFactor(variation.Relief);
        ShiftMountains(variation.Mountains);
        float settlementArea = Mathf.Pow(variation.AreaScale, SETTLEMENT_AREA_EXPONENT);
        ScaleSettlements(WorldVariation.Factor(variation.Towns, FEW_TOWNS, MANY_TOWNS) * settlementArea, variation.Towns != WorldAmount.None);

        float farmsteads = WorldVariation.Factor(variation.Farmsteads, FEW_FARMSTEADS, MANY_FARMSTEADS);
        DirtChance = Mathf.Clamp01(DirtChance * farmsteads);
        FarmsteadChance = Mathf.Clamp01(FarmsteadChance * farmsteads);

        float smallWorldRivers = 1f / Mathf.Min(1f, variation.SizeScale);
        Water.Vary(WorldVariation.Factor(variation.Rivers, FEW_RIVERS, MANY_RIVERS) * smallWorldRivers,
            WorldVariation.Factor(variation.Lakes, FEW_LAKES, MANY_LAKES));
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

    private static float ReliefFactor(WorldRelief relief)
    {
        return relief switch
        {
            WorldRelief.Plains => PLAINS_RELIEF,
            WorldRelief.Rolling => ROLLING_RELIEF,
            _ => 1f
        };
    }

    private void ShiftMountains(WorldAmount mountains)
    {
        if (mountains == WorldAmount.None)
        {
            MountainMaskLow = NO_MOUNTAINS_LOW;
            MountainMaskHigh = 1f;
            return;
        }

        float shift = mountains switch
        {
            WorldAmount.Few => FEW_MOUNTAINS_SHIFT,
            WorldAmount.Many => MANY_MOUNTAINS_SHIFT,
            _ => 0f
        };

        MountainMaskHigh = Mathf.Clamp(MountainMaskHigh + shift, MOUNTAIN_MASK_GAP, 1f);
        MountainMaskLow = Mathf.Clamp(MountainMaskLow + shift, 0f, MountainMaskHigh - MOUNTAIN_MASK_GAP);
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
