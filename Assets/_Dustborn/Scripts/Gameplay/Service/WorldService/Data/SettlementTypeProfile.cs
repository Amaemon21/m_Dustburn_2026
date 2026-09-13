using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class SettlementTypeProfile
{
    [field: SerializeField, MinValue(0)]
    [field: Tooltip("Settlements of this type the world tries to place. Fewer appear when no flat, dry site with room around it is left.")]
    public int Count { get; private set; }

    [field: SerializeField, MinValue(1)]
    [field: Tooltip("Fewest occupied tiles a settlement of this type is grown to. Terrain can still leave it smaller.")]
    public int MinTiles { get; private set; } = 1;

    [field: SerializeField, MinValue(1)]
    [field: Tooltip("Most occupied tiles a settlement of this type is grown to.")]
    public int MaxTiles { get; private set; } = 1;

    [field: SerializeField, MinValue(1f)]
    [field: Tooltip("Metres between two settlement centres of this type, so the large ones spread over the map instead of sharing one plain.")]
    public float Spacing { get; private set; } = 600f;

    [field: SerializeField, MinValue(1)]
    [field: Tooltip("Most regional links planned for one settlement of this type. Keeps hamlets as leaves and stops accidental hubs.")]
    public int MaxLinks { get; private set; } = 2;

    [field: SerializeField, MinValue(1)]
    [field: Tooltip("Most gateway tiles one settlement of this type opens toward the highway network. Links beyond it share a gateway and branch outside town.")]
    public int MaxGateways { get; private set; } = 2;

    [field: SerializeField, MinValue(0.1f)]
    [field: Tooltip("Weight in the regional graph. Important pairs are linked first, keep their trunk corridors and earn loops sooner.")]
    public float Importance { get; private set; } = 1f;

    [field: SerializeField, Range(0, 6)]
    [field: Tooltip("Tiles the arterial runs from the core toward each gateway before it leaves town.")]
    public int ArmTiles { get; private set; } = 1;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Chance to keep a tile adjacency as a street after every tile is connected. Zero gives a tree of dead ends, one a full grid.")]
    public float LoopDensity { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Compact grows around the core, Cross along two crossing streets, Linear along one main street.")]
    public SettlementShape Shape { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of tiles nearest the core zoned Downtown.")]
    public float DowntownShare { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of tiles zoned Commercial, taken along the arterials next to the core.")]
    public float CommercialShare { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of tiles zoned Industrial, taken from the side of one gateway.")]
    public float IndustrialShare { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of the outermost tiles left as Rural outskirts.")]
    public float RuralShare { get; private set; }

    [field: SerializeField, Range(0.05f, 1f)]
    [field: Tooltip("How densely parcels along a street are built. Lower leaves gaps between buildings, which is what makes a ghost town read as abandoned.")]
    public float PoiDensity { get; private set; } = 1f;

    public SettlementTypeProfile()
    {
    }

    public SettlementTypeProfile(int count, int minTiles, int maxTiles, float spacing, int maxLinks, int maxGateways, float importance,
        int armTiles, float loopDensity, SettlementShape shape, float downtown, float commercial, float industrial, float rural, float poiDensity)
    {
        Count = count;
        MinTiles = minTiles;
        MaxTiles = maxTiles;
        Spacing = spacing;
        MaxLinks = maxLinks;
        MaxGateways = maxGateways;
        Importance = importance;
        ArmTiles = armTiles;
        LoopDensity = loopDensity;
        Shape = shape;
        DowntownShare = downtown;
        CommercialShare = commercial;
        IndustrialShare = industrial;
        RuralShare = rural;
        PoiDensity = poiDensity;
    }

    public int LowTiles => Mathf.Max(1, Mathf.Min(MinTiles, MaxTiles));

    public int HighTiles => Mathf.Max(LowTiles, Mathf.Max(MinTiles, MaxTiles));

    public float EstimateRadius(float tileSize)
    {
        float tiles = (LowTiles + HighTiles) * 0.5f;

        return Mathf.Sqrt(tiles / Mathf.PI) * tileSize + tileSize * 0.5f;
    }
}
