using UnityEngine;

public class BakedWorld : ScriptableObject
{
    [field: SerializeField] public WorldGenerationConfig Config { get; private set; }
    [field: SerializeField] public BiomeDatabase Biomes { get; private set; }
    [field: SerializeField] public TextAsset HeightMap { get; private set; }
    [field: SerializeField] public Texture2D BiomeMap { get; private set; }
    [field: SerializeField] public Texture2D RoadMask { get; private set; }
    [field: SerializeField] public PoiPlacementAsset Placement { get; private set; }
    [field: SerializeField] public Material Material { get; private set; }
    [field: SerializeField] public bool Complete { get; private set; }

    public bool IsValid => Complete && Config != null && Biomes != null && HeightMap != null
        && BiomeMap != null && RoadMask != null && Placement != null && Material != null
        && HeightMap.dataSize == (long)Config.HeightMapResolution * Config.HeightMapResolution * sizeof(ushort)
        && BiomeMap.width == Config.BiomeMapResolution && BiomeMap.height == Config.BiomeMapResolution
        && RoadMask.width == Config.HeightMapResolution && RoadMask.height == Config.HeightMapResolution;

#if UNITY_EDITOR
    public void EditorInvalidate()
    {
        Complete = false;
    }

    public void EditorSetup(WorldGenerationConfig config, BiomeDatabase biomes, TextAsset heightMap,
        Texture2D biomeMap, Texture2D roadMask, PoiPlacementAsset placement, Material material)
    {
        Config = config;
        Biomes = biomes;
        HeightMap = heightMap;
        BiomeMap = biomeMap;
        RoadMask = roadMask;
        Placement = placement;
        Material = material;
        Complete = true;
    }
#endif
}
