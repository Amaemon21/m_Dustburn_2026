using System.IO;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "WorldBuildSettings", menuName = "World/World Build Settings")]
public class WorldBuildSettings : ScriptableObject
{
    [field: SerializeField, Foldout("Sources")] public WorldGenerationConfig Config { get; private set; }
    [field: SerializeField, Foldout("Sources")] public BiomeDatabase Biomes { get; private set; }
    [field: SerializeField, Foldout("Sources")] public PoiDatabase Pois { get; private set; }
    [field: SerializeField, Foldout("Sources")] public VoxelConfig Voxels { get; private set; }

    [field: SerializeField, BoxGroup("Decor")] public bool SpawnDecor { get; private set; } = true;
    [field: SerializeField, BoxGroup("Decor"), Range(0f, 1f)] public float GrassDensity { get; private set; } = 1f;

    [field: SerializeField, Foldout("Preview")] public Vector2 PreviewCenter { get; private set; } = new(1024f, 1024f);
    [field: SerializeField, Foldout("Preview")] public bool PreviewColliders { get; private set; } = true;
    [field: SerializeField, Foldout("Preview"), MinValue(16)] public int MemoryBudget { get; private set; } = 512;

    [field: SerializeField, Foldout("Quality"), Range(1, 6)] public int LodCount { get; private set; } = 6;
    [field: SerializeField, Foldout("Quality"), MinValue(32f)] public float NearDistance { get; private set; } = 96f;
    [field: SerializeField, Foldout("Quality"), Range(256, 4096)] public int ControlResolution { get; private set; } = 4096;
    [field: SerializeField, Foldout("Quality"), MinValue(1024)] public int BatchVertexBudget { get; private set; } = 48000;

    [field: SerializeField, Foldout("Runtime")] public bool ShowLoadingScreen { get; private set; } = true;
    [field: SerializeField, Foldout("Runtime"), Range(1, 16)] public int MeshWorkers { get; private set; } = 8;
    [field: SerializeField, Foldout("Runtime"), Range(1f, 100f)] public float LoadingBudget { get; private set; } = 60f;
    [field: SerializeField, Foldout("Runtime"), Range(1f, 20f)] public float DecorBudget { get; private set; } = 6f;
    [field: SerializeField, Foldout("Runtime"), Range(1, 128)] public int PoisPerFrame { get; private set; } = 16;

    public string MaterialPath => Path.Combine(Path.GetDirectoryName(Config.HeightMapAssetPath), "VoxelMaterial.mat").Replace('\\', '/');
    public string BakedWorldPath => Path.Combine(Path.GetDirectoryName(Config.HeightMapAssetPath), "BakedWorld.asset").Replace('\\', '/');

    public bool IsValid => Config != null && Biomes != null && Pois != null && Voxels != null;
}
