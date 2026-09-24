using System.Collections.Generic;
using UnityEngine;

public class BakedWorld : ScriptableObject
{
    [field: SerializeField] public WorldGenerationConfig Config { get; private set; }
    [field: SerializeField] public BiomeDatabase Biomes { get; private set; }
    [field: SerializeField] public TextAsset HeightMap { get; private set; }
    [field: SerializeField] public Texture2D BiomeMap { get; private set; }
    [field: SerializeField] public Texture2D RoadMask { get; private set; }
    [field: SerializeField] public RoadNetworkAsset Roads { get; private set; }
    [field: SerializeField] public PoiPlacementAsset Placement { get; private set; }
    [field: SerializeField] public Material Material { get; private set; }
    [field: SerializeField] public TextAsset Water { get; private set; }
    [field: SerializeField] public List<TerrainStampPlacement> Stamps { get; private set; } = new();
    [field: SerializeField] public bool Complete { get; private set; }

    public bool IsValid => Complete && Config != null && Biomes != null && HeightMap != null
        && BiomeMap != null && RoadMask != null && Roads != null && Placement != null && Material != null && Water != null
        && HeightMap.dataSize == (long)Config.HeightMapResolution * Config.HeightMapResolution * sizeof(ushort)
        && BiomeMap.width == Config.BiomeMapResolution && BiomeMap.height == Config.BiomeMapResolution
        && RoadMask.width == Config.HeightMapResolution && RoadMask.height == Config.HeightMapResolution;

    public WaterMap LoadWater()
    {
        return WaterMap.Load(Water);
    }

#if UNITY_EDITOR
    public void EditorInvalidate()
    {
        Complete = false;
    }

    public void EditorSetup(WorldGenerationConfig config, BiomeDatabase biomes, TextAsset heightMap,
        Texture2D biomeMap, Texture2D roadMask, RoadNetworkAsset roads, PoiPlacementAsset placement, Material material,
        TextAsset water, IEnumerable<TerrainStampPlacement> stamps)
    {
        Config = config;
        Biomes = biomes;
        HeightMap = heightMap;
        BiomeMap = biomeMap;
        RoadMask = roadMask;
        Roads = roads;
        Placement = placement;
        Material = material;
        Water = water;
        Stamps = new List<TerrainStampPlacement>(stamps);
        Complete = true;
    }
#endif
}
