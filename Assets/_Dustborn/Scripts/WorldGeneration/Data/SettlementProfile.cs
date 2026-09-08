using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class SettlementProfile
{
    [field: SerializeField] public string Name { get; private set; }
    [field: SerializeField, Min(0.05f)] public float RadiusScale { get; private set; }
    [field: SerializeField, Min(20f)] public float CoreBlockSize { get; private set; }
    [field: SerializeField, Min(20f)] public float OuterBlockSize { get; private set; }
    [field: SerializeField, Range(0, 4)] public int StreetBranchDepth { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float DowntownFraction { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float ResidentialFraction { get; private set; }
    [field: SerializeField, Range(0f, 360f)] public float IndustrialSpan { get; private set; }
    [field: SerializeField] public DistrictType OuterDistrict { get; private set; }
    [field: SerializeField, Min(0f)] public float LotMargin { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Buildings this settlement rank must have. Lots are cut to fit them before the weighted fill runs, and a settlement that cannot host them all is replanned.")]
    public List<PoiRequirement> Composition { get; private set; } = new();

    public SettlementProfile(string name, float radiusScale, float coreBlock, float outerBlock, int branchDepth,
        float downtownFraction, float residentialFraction, float industrialSpan, DistrictType outerDistrict, float lotMargin)
    {
        Name = name;
        RadiusScale = radiusScale;
        CoreBlockSize = coreBlock;
        OuterBlockSize = outerBlock;
        StreetBranchDepth = branchDepth;
        DowntownFraction = downtownFraction;
        ResidentialFraction = residentialFraction;
        IndustrialSpan = industrialSpan;
        OuterDistrict = outerDistrict;
        LotMargin = lotMargin;
        Composition = new List<PoiRequirement>();
    }

    public bool HasDistrict(DistrictType district)
    {
        if (district == OuterDistrict)
            return true;

        return district switch
        {
            DistrictType.Downtown => DowntownFraction > 0f,
            DistrictType.Residential => ResidentialFraction > DowntownFraction,
            DistrictType.Industrial => IndustrialSpan > 0f,
            _ => false
        };
    }

    public static SettlementProfile DefaultCity()
    {
        return new SettlementProfile("City", 1f, 62f, 96f, 3, 0.34f, 0.66f, 120f, DistrictType.Residential, 1.5f);
    }

    public static SettlementProfile DefaultTown()
    {
        return new SettlementProfile("Town", 0.66f, 62f, 88f, 2, 0.38f, 0.72f, 100f, DistrictType.Residential, 3.5f);
    }

    public static SettlementProfile DefaultVillage()
    {
        return new SettlementProfile("Village", 0.25f, 48f, 64f, 1, 0f, 0f, 0f, DistrictType.Rural, 6f);
    }
}
