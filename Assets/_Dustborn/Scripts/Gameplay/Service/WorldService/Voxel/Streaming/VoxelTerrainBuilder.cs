using System.Collections.Generic;
using System.Diagnostics;
using NaughtyAttributes;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class VoxelTerrainBuilder : MonoBehaviour
{
    private const int MAX_MESH_WORKERS = 16;
    private const int COLLIDER_BATCH = 256;

    [BoxGroup("Source"), SerializeField]
    private WorldGenerationConfig _config;

    [BoxGroup("Source"), SerializeField]
    private VoxelConfig _voxels;

    [BoxGroup("Source"), Required("Assets/_Dustborn/Generated/HeightMap.bytes"), SerializeField]
    private TextAsset _heightMap;

    [BoxGroup("Rendering"), SerializeField]
    private bool _useRepetitionless = true;

    [BoxGroup("Rendering"), ShowIf(nameof(_useRepetitionless)), SerializeField]
    private string _repetitionlessMaterialPath = "Assets/_Dustborn/Generated/VoxelMaterial.mat";

    [BoxGroup("Rendering"), HideIf(nameof(_useRepetitionless)), SerializeField]
    private Material _material;

    [BoxGroup("Rendering"), SerializeField]
    private bool _buildColliders = true;

    [BoxGroup("Splatmap"), SerializeField] private bool _paintSplatmap = true;

    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), SerializeField]
    private BiomeDatabase _biomes;

    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), SerializeField] private Texture2D _biomeMap;
    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), SerializeField] private Texture2D _roadMask;
    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), SerializeField] private RoadNetworkAsset _roads;

    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), MinValue(256)]
    [Tooltip("Side of the world control textures. Match it to the height map: at HeightCellSize 2 over a 4096 m world that is 2048, two metres per texel and exactly the resolution the splat is derived from. Going finer only upsamples, and every step doubles both the texture and the buffer the bake needs.")]
    [SerializeField]
    private int _controlResolution = 4096;

    [BoxGroup("Decor"), SerializeField] private bool _spawnDecor = true;
    [BoxGroup("Decor"), SerializeField] private bool _decorEverywhere = true;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField]
    private PoiPlacementAsset _poiPlacement;

    [SerializeField, HideInInspector] private TextAsset _water;
    [SerializeField, HideInInspector] private Material _waterMaterial;
    [SerializeField, HideInInspector] private Material _iceMaterial;
    [SerializeField, HideInInspector] private Material _bridgeMaterial;
    [SerializeField, HideInInspector] private GameObject _culvertPrefab;
    [SerializeField, HideInInspector] private List<TerrainStampPlacement> _stamps = new();

    private WaterMap _waterMap;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1024)]
    [Tooltip("Vertex budget of one combined batch. Bigger batches mean fewer draw calls but coarser culling, since a batch is culled as a whole.")]
    [SerializeField]
    private int _batchVertexBudget = 48000;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), Range(0f, 4f)]
    [Tooltip("Multiplier on the grass the biomes ask for; 2 to 4 tightens the grid for that many times more tufts. Grass reaches only as far as GrassMaxLod, the ring around the centre.")]
    [SerializeField]
    private float _grassDensity = 1f;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor))]
    [Tooltip("Keep the colliders that decor prefabs carry. Turning this on stops such prefabs from being combined, because a combined mesh has no per object collider.")]
    [SerializeField]
    private bool _decorColliders;

    [BoxGroup("World")]
    [Tooltip("Where the detail is centred, in metres. The whole world is built either way; this is where the finest ring sits, so put it where the player starts.")]
    [SerializeField]
    private Vector2 _center = new(1024f, 1024f);

    [BoxGroup("World"), Range(1, 6)]
    [Tooltip("Levels of detail, the same rings the streamer builds at runtime. Each one doubles both the voxel size and the radius it covers, and the coarsest is stretched over everything left of the world.")]
    [SerializeField]
    private int _lodCount = 6;

    [BoxGroup("World"), MinValue(32f)]
    [Tooltip("Radius of the finest ring in metres, measured from the centre.")]
    [SerializeField]
    private float _nearDistance = 96f;

    [BoxGroup("World"), MinValue(16)]
    [Tooltip("Memory the built world may take in megabytes. The build is refused before it starts if the estimate goes over, because running out of memory takes the editor down with it.")]
    [SerializeField]
    private int _memoryBudget = 768;

    private readonly List<MeshFilter> _chunks = new();
    private readonly List<VoxelColumnKey> _columns = new();
    private readonly Dictionary<VoxelColumnKey, Transform> _roots = new();

    private void Awake()
    {
        if (transform.childCount == 0)
            return;

        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(false);

        Debug.LogWarning($"Voxel terrain: {transform.childCount} preview objects were saved into the scene and are hidden for play mode. "
            + "The streamer draws the same ground and the same decor, so leaving them visible renders everything twice. Press Clear to remove them", this);
    }

    private VoxelDecorSpawner _decor;

    [ShowNativeProperty]
    public string WorldStatus => _voxels == null || _config == null
        ? "VoxelConfig or WorldGenerationConfig is not assigned"
        : $"the whole {_config.WorldSize} m world in {Plan().LodCount} rings around ({_center.x:0}, {_center.y:0}), {_chunks.Count} chunks built";

    [ShowNativeProperty]
    public string BuildCost => _voxels == null || _config == null
        ? "VoxelConfig or WorldGenerationConfig is not assigned"
        : Estimate().Describe();

    private VoxelStreamPlan Plan()
    {
        return new VoxelStreamPlan(_voxels, _lodCount, _nearDistance, _config.WorldSize, WaterMapFormat.Load(_water));
    }

    private VoxelBudget Estimate()
    {
        VoxelStreamPlan plan = Plan();
        var budget = new VoxelBudget();

        plan.Around(_center, _columns);

        foreach (VoxelColumnKey key in _columns)
        {
            float size = plan.ChunkMetres(key.Lod);

            budget += VoxelBudget.Estimate(plan.VoxelSize(key.Lod), size * size, _buildColliders);
        }

        return budget;
    }

    [Button("Build Voxel Terrain")]
    public void Build()
    {
        Build(null);
    }

    public bool Build(System.Action<string, float> progress)
    {
        if (!Validate())
            return false;

        if (!Affordable())
            return false;

        Clear();

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);
        _waterMap = WaterMapFormat.Load(_water);

        using var field = new VoxelDensityField(map, _voxels, Lowest(map));

        VoxelStreamPlan plan = Plan();

        plan.Around(_center, _columns);

        var clock = Stopwatch.StartNew();
        long geometry = WorldGenProbe.Now;

        List<ChunkTask> tasks = CollectChunks(field, plan);
        int attempted = tasks.Count;
        int triangles = MeshChunks(field, tasks, progress);

        RemoveEmptyColumns();

        clock.Stop();
        WorldGenProbe.Record(WorldGenStage.PreviewGeometry, geometry, WorldGenProbe.Now, triangles);

        string splat = PaintSplatmap(map);
        string decor;

        using (WorldGenProbe.Measure(WorldGenStage.PreviewDecor))
            decor = SpawnDecor(field, plan, progress);

        WaterSurfaceBuilder.Build(_waterMap, _waterMaterial, _iceMaterial, transform, WorldEdgeBuilder.OffshoreReach(_config));
        WorldEdgeBuilder.Build(_config, _material, transform);
        WaterCrossingBuilder.Build(_waterMap, _bridgeMaterial, _culvertPrefab, transform);

#if UNITY_EDITOR
        WorldFeatureGizmos.Attach(transform, _stamps, _water);
#endif

        Debug.Log($"Voxel terrain: the whole {_config.WorldSize} m world in {_columns.Count} columns, {_chunks.Count} chunks, "
            + $"{triangles} triangles, {attempted} chunks visited, {clock.ElapsedMilliseconds} ms. {splat}. {decor}", this);
        progress?.Invoke("Ландшафт готов", 1f);
        return true;
    }

    public void Configure(WorldBuildSettings settings, BakedWorld world)
    {
        _config = world.Config;
        _voxels = settings.Voxels;
        _heightMap = world.HeightMap;
        _material = world.Material;
        _useRepetitionless = false;
        _paintSplatmap = false;
        _biomes = world.Biomes;
        _biomeMap = world.BiomeMap;
        _roadMask = world.RoadMask;
        _roads = world.Roads;
        _poiPlacement = world.Placement;
        _water = world.Water;
        _stamps = new List<TerrainStampPlacement>(world.Stamps);
        _waterMaterial = settings.WaterMaterial;
        _iceMaterial = settings.IceMaterial;
        _bridgeMaterial = settings.BridgeMaterial;
        _culvertPrefab = settings.CulvertPrefab;
        _spawnDecor = settings.SpawnDecor;
        _decorEverywhere = settings.PreviewDecorEverywhere;
        _grassDensity = settings.GrassDensity;
        _batchVertexBudget = settings.BatchVertexBudget;
        _buildColliders = settings.PreviewColliders;
        _center = settings.PreviewCenter;
        _lodCount = settings.LodCount;
        _nearDistance = settings.NearDistance;
        _memoryBudget = settings.MemoryBudget;
    }

    [Button("Clear Voxel Terrain")]
    public void Clear()
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.PreviewClear);

        _chunks.Clear();
        _roots.Clear();

        _decor?.Dispose();
        _decor = null;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GeneratedMesh.Destroy(transform.GetChild(i).gameObject);
        }
    }

    private void OnDestroy()
    {
        _decor?.Dispose();
        _decor = null;
    }

    private bool Affordable()
    {
        VoxelBudget budget = Estimate();

        if (budget.Megabytes <= _memoryBudget)
            return true;

        Debug.LogError($"Voxel terrain: the world at {_voxels.VoxelSize} m per voxel over {_lodCount} rings is {budget.Describe()}, "
            + $"over the {_memoryBudget} MB budget. The build is refused because running out of memory takes the editor down. "
            + "Lower LodCount or NearDistance, raise VoxelSize, or raise the budget if the machine really has the memory", this);

        return false;
    }

    private bool Validate()
    {
        if (_config == null)
        {
            Debug.LogError("Voxel terrain: WorldGenerationConfig is not assigned", this);
            return false;
        }

        if (_voxels == null)
        {
            Debug.LogError("Voxel terrain: VoxelConfig is not assigned. Create one through Create/World/Voxel Config", this);
            return false;
        }

        if (_heightMap == null)
        {
            Debug.LogError("Voxel terrain: HeightMap.bytes is not assigned. Run the world generator first", this);
            return false;
        }

        if (!_useRepetitionless && _material == null)
            Debug.LogWarning("Voxel terrain: no material assigned and Repetitionless is switched off, chunks will render magenta", this);

        return true;
    }
}
