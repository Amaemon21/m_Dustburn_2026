using UnityEngine;

public class WorldGenBenchFixture : ScriptableObject
{
    public const string RESOURCES_PATH = "WorldGenBenchFixture";

    [field: SerializeField] public WorldGenerationConfig Config { get; private set; }
    [field: SerializeField] public BiomeDatabase Biomes { get; private set; }
    [field: SerializeField] public PoiDatabase Pois { get; private set; }
    [field: SerializeField] public VoxelConfig Voxels { get; private set; }
    [field: SerializeField] public WorldBuildSettings Settings { get; private set; }
    [field: SerializeField] public BakedWorld World { get; private set; }
    [field: SerializeField] public GameObject DecorProbePrefab { get; private set; }

    public bool HasSources => Config != null && Biomes != null && Pois != null && Voxels != null && Settings != null;

    public bool HasBake => World != null && World.IsValid;

    public string Describe()
    {
        return $"config={(Config == null ? "missing" : Config.name)}, biomes={(Biomes == null ? "missing" : Biomes.name)}, "
            + $"pois={(Pois == null ? "missing" : Pois.name)}, voxels={(Voxels == null ? "missing" : Voxels.name)}, "
            + $"settings={(Settings == null ? "missing" : Settings.name)}, bake={(HasBake ? "valid" : "missing")}";
    }

#if UNITY_EDITOR
    public void EditorSetup(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois, VoxelConfig voxels,
        WorldBuildSettings settings, BakedWorld world)
    {
        Config = config;
        Biomes = biomes;
        Pois = pois;
        Voxels = voxels;
        Settings = settings;
        World = world;
    }
#endif
}
