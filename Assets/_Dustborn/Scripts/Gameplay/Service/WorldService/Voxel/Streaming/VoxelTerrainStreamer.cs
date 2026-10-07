using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public partial class VoxelTerrainStreamer : MonoBehaviour
{
    [BoxGroup("Source"), SerializeField] private WorldGenerationConfig _config;
    [BoxGroup("Source"), SerializeField] private VoxelConfig _voxels;

    [BoxGroup("Source"), Required("Assets/_Dustborn/Generated/HeightMap.bytes"), SerializeField]
    private TextAsset _heightMap;

    [BoxGroup("Rendering"), SerializeField] private Material _material;

    [BoxGroup("Rendering"), SerializeField] private bool _useRepetitionless = true;

    [BoxGroup("Rendering"), ShowIf(nameof(_useRepetitionless)), SerializeField]
    private string _repetitionlessMaterialPath = "Assets/_Dustborn/Generated/VoxelMaterial.mat";

    [BoxGroup("Rendering"), MinValue(256)]
    [Tooltip("Side of the world control textures. Match it to the height map: at HeightCellSize 2 over a 4096 m world that is 2048, two metres per texel and exactly the resolution the splat is derived from. Going finer only upsamples, and every step doubles both the texture and the buffer the bake needs.")]
    [SerializeField]
    private int _controlResolution = 4096;

    [BoxGroup("Decor"), SerializeField] private bool _spawnDecor = true;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private BiomeDatabase _biomes;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private Texture2D _biomeMap;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private Texture2D _roadMask;
    [BoxGroup("Decor"), SerializeField] private RoadNetworkAsset _roads;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private PoiPlacementAsset _poiPlacement;
    [SerializeField, HideInInspector] private TextAsset _water;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1024), SerializeField]
    private int _batchVertexBudget = 48000;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), Range(0f, 4f)]
    [Tooltip("Multiplier on the grass the biomes ask for; 2 to 4 tightens the grid for that many times more tufts. Grass only ever covers the GrassDistance circle around the viewer.")]
    [SerializeField]
    private float _grassDensity = 1f;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1f)]
    [Tooltip("How far the viewer has to move before the decor circles around them are recomputed. The ground never moves; decor is the one thing that follows the player, because a whole world of grass is millions of cards and a whole world of trees is a quarter of a million prefabs. Each kind reaches as far as its own ring in VoxelConfig: GrassDistance for grass, RockMaxLod and TreeMaxLod for the rest.")]
    [SerializeField]
    private float _decorStep = 16f;

    [BoxGroup("World"), Required("The transform the world is built around, usually the player"), SerializeField]
    private Transform _viewer;

    [BoxGroup("World"), Range(1, 6)]
    [Tooltip("Levels of detail. Each one doubles both the voxel size and the radius it covers, and the coarsest one is stretched over everything that is left of the world, so the whole map is always built. Raising it does not push the fine rings out, that is NearDistance: it hands the far remainder of the map to a coarser ring, which costs a quarter of the triangles and only looks right while something else limits the view.")]
    [SerializeField]
    private int _lodCount = 6;

    [BoxGroup("World"), MinValue(32f)]
    [Tooltip("Radius of the finest ring in metres, measured from where the viewer stands when the world is built. Everything closer is built at the full voxel size.")]
    [SerializeField]
    private float _nearDistance = 96f;

    [BoxGroup("World"), MinValue(1)]
    [Tooltip("Chunk columns started per frame once the world is up. The first set ignores this while Preload is on, because nothing is being played yet.")]
    [SerializeField]
    private int _columnsPerFrame = 3;

    [BoxGroup("World"), Range(1, 16)]
    [Tooltip("Chunk meshing jobs allowed to run at once. They are scheduled one frame and picked up the next, so the meshing happens on worker threads while the main thread runs the game. Each worker holds about 0.3 MB of scratch buffers.")]
    [SerializeField]
    private int _meshWorkers = 8;

    [BoxGroup("World"), Range(0, 5)]
    [Tooltip("Coarsest ring that still gets a collider. The world is built once and the player can walk anywhere in it, so this should cover every ring: below the last one they walk off the edge of the collision into ground that renders and does not exist to physics.")]
    [SerializeField]
    private int _colliderMaxLod = 5;

    [BoxGroup("World"), MinValue(1)]
    [Tooltip("Collision meshes cooked at once. Cooking runs on worker threads and the collider is attached the next frame, so a larger batch simply means the world becomes solid sooner.")]
    [SerializeField]
    private int _collidersPerFrame = 32;

    [BoxGroup("World"), MinValue(0.5f)]
    [Tooltip("Milliseconds per frame spent placing decor once the world is up. That is grass following the viewer; the trees and rocks are placed with the world and never move.")]
    [SerializeField]
    private float _decorBudget = 6f;

    [BoxGroup("World"), MinValue(1)]
    [Tooltip("Children destroyed per frame when a patch of grass leaves the circle around the viewer.")]
    [SerializeField]
    private int _demolishPerFrame = 256;

    [BoxGroup("World")]
    [Tooltip("Build the whole world before the game starts rather than over the following seconds, and spend as much of each frame on it as it takes. Poll Progress and Ready from a loading screen; the streamer does not stop anything by itself.")]
    [SerializeField]
    private bool _preload = true;

    [BoxGroup("World"), MinValue(1f)]
    [Tooltip("Milliseconds per frame the preload may spend placing decor. Nothing is being played yet, so this is far larger than the running budget.")]
    [SerializeField]
    private float _preloadBudget = 60f;

    private const int DECOR_SLICE = 48;

    private readonly Dictionary<VoxelColumnKey, Column> _loaded = new();
    private readonly Dictionary<Vector2Int, VoxelColumnKey> _cover = new();
    private readonly List<DecorRing> _rings = new();
    private readonly List<Vector2Int> _faded = new();
    private readonly List<VoxelColumnKey> _wanted = new();
    private readonly Queue<VoxelColumnKey> _pending = new();
    private readonly List<PendingChunk> _inFlight = new();
    private readonly Queue<VoxelDecorBuild> _decorQueue = new();
    private readonly Queue<GameObject> _demolishing = new();

    private VoxelDensityField _field;
    private VoxelDecorSpawner _decor;
    private VoxelMeshQueue _queue;
    private VoxelColliderQueue _colliders;
    private VoxelStreamPlan _plan;

    private bool _planned;
    private bool _ready;
    private int _wantedAtStart;
    private int _decorAtStart;

    private VoxelColumnKey _active;
    private int _activeY;
    private int _activeHigh;
    private bool _hasActive;

    private readonly System.Diagnostics.Stopwatch _clock = new();

    private Comparison<VoxelColumnKey> _byDistance;
    private Vector2 _sortOrigin;

    [ShowNativeProperty]
    public bool Ready => _ready;
    public IDecorObjectHook DecorHook { get; set; }

    public bool Initialized => _plan != null;

    public float Progress => _wantedAtStart <= 0 || _queue == null
        ? 0f
        : Mathf.Clamp01(1f - (_pending.Count + _queue.InFlight + _decorQueue.Count) / (float)(_wantedAtStart + _decorAtStart));

    [ShowNativeProperty]
    public string WorldStatus => _plan == null
        ? "not started"
        : $"{_loaded.Count} of {_wanted.Count} columns built, {_pending.Count} queued, {_queue.InFlight} chunks meshing on {_queue.Workers} workers, "
          + $"{_colliders.Waiting} waiting for collision, {Patches()} patches of decor, {_decorQueue.Count} decor builds waiting";

    private void OnEnable()
    {
        if (!Application.isPlaying || !Validate())
            return;

        long construct = WorldGenProbe.Now;
        HeightMap map;

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeDecodeHeights))
            map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        float lowest;

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeLowest))
            lowest = Lowest(map);

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeField))
        {
            _field = new VoxelDensityField(map, _voxels, lowest);
            _queue = new VoxelMeshQueue(_voxels, _field, _meshWorkers);
            _colliders = new VoxelColliderQueue(_collidersPerFrame);
        }

        WaterMap water = WaterMapFormat.Load(_water);

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeDecorSetup))
            _decor = CreateDecor(water);

        _byDistance = ByDistance;
        _plan = new VoxelStreamPlan(_voxels, _lodCount, _nearDistance, _config.WorldSize, water);

        Rings();

        _planned = false;
        _ready = false;
        _wantedAtStart = 0;
        _decorAtStart = 0;

        WorldGenProbe.Record(WorldGenStage.RuntimeConstruct, construct, WorldGenProbe.Now, 0L);
        WorldGenProbe.Mark(WorldGenMilestone.RuntimeConstructed);
    }

    public void Configure(WorldBuildSettings settings, BakedWorld world, Transform viewer)
    {
        _config = world.Config;
        _voxels = settings.Voxels;
        _heightMap = world.HeightMap;
        _material = world.Material;
        _biomes = world.Biomes;
        _biomeMap = world.BiomeMap;
        _roadMask = world.RoadMask;
        _roads = world.Roads;
        _poiPlacement = world.Placement;
        _water = world.Water;
        _viewer = viewer;
        _spawnDecor = settings.SpawnDecor;
        _grassDensity = settings.GrassDensity;
        _batchVertexBudget = settings.BatchVertexBudget;
        _lodCount = settings.LodCount;
        _nearDistance = settings.NearDistance;
        _meshWorkers = settings.MeshWorkers;
        _colliderMaxLod = Mathf.Max(0, settings.LodCount - 1);
        _decorBudget = settings.DecorBudget;
        _preload = true;
        _preloadBudget = settings.LoadingBudget;
    }

    private void OnDisable()
    {
        _colliders?.Dispose();
        _colliders = null;
        Unload();

        _queue?.Dispose();
        _field?.Dispose();
        _decor?.Dispose();

        _inFlight.Clear();
        _decorQueue.Clear();

        _queue = null;
        _colliders = null;
        _decor = null;
        _plan = null;
        _field = null;
        _ready = false;
        _wantedAtStart = 0;
    }

    private void Update()
    {
        if (_plan == null || _viewer == null)
            return;

        Vector3 position = _viewer.position;
        var ground = new Vector2(position.x, position.z);

        using WorldGenProbe.Span tick = WorldGenProbe.Measure(WorldGenStage.RuntimeTick);

        using (WorldGenProbe.Measure(WorldGenStage.ColliderCollect))
            _colliders.Collect();

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeCollect))
            Collect();

        if (!_planned)
        {
            using (WorldGenProbe.Measure(WorldGenStage.RuntimePlan))
                Plan(ground);

            WorldGenProbe.Mark(WorldGenMilestone.Planned);
        }

        if (_preload && !_ready)
        {
            using (WorldGenProbe.Measure(WorldGenStage.RuntimeRush))
                Rush();
        }
        else
        {
            using (WorldGenProbe.Measure(WorldGenStage.RuntimeDispatch))
                Dispatch();
        }

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeSow))
            Sow(ground);

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeDecorate))
            Decorate();

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeSettle))
            Settle();

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeDemolish))
            Demolish();

        using (WorldGenProbe.Measure(WorldGenStage.ColliderDispatch))
            _colliders.Dispatch();

        if (_pending.Count == 0 && !_hasActive && _queue.InFlight == 0)
            WorldGenProbe.Mark(WorldGenMilestone.TerrainQueuesEmpty);

        if (_colliders.Waiting == 0 && WorldGenProbe.Reached(WorldGenMilestone.FirstCollider))
            WorldGenProbe.Mark(WorldGenMilestone.CollidersIdle);

        if (_decorQueue.Count == 0 && _decorAtStart > 0)
            WorldGenProbe.Mark(WorldGenMilestone.InitialDecor);

        if (_decorQueue.Count == 0 && _demolishing.Count == 0 && _planned)
            WorldGenProbe.Remark(WorldGenMilestone.DecorSettled);
    }

    [Button("Bake Ground Material")]
    public void BakeMaterial()
    {
        if (_config == null || _heightMap == null)
        {
            Debug.LogError("Voxel terrain: assign the WorldGenerationConfig and HeightMap.bytes before baking the material", this);
            return;
        }

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        var request = new VoxelGroundMaterial.Request
        {
            Config = _config,
            Biomes = _biomes,
            BiomeMap = _biomeMap,
            Roads = _roads == null ? null : _roads.Paved(),
            Map = map,
            ControlResolution = _controlResolution,
            UseRepetitionless = _useRepetitionless,
            MaterialPath = _repetitionlessMaterialPath,
            Material = _material
        };

        Debug.Log($"Voxel ground material: {VoxelGroundMaterial.Bake(request, this)}", this);

        _material = request.Material;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [Button("Rebuild World")]
    public void Rebuild()
    {
        if (_plan == null)
        {
            Debug.LogWarning("Voxel terrain: the world is built in play mode, there is nothing to rebuild here", this);
            return;
        }

        Unload();

        _planned = false;
        _ready = false;
        _wantedAtStart = 0;
        _decorAtStart = 0;
    }

    [Button("Unload Everything")]
    public void Unload()
    {
        foreach (KeyValuePair<VoxelColumnKey, Column> pair in _loaded)
        {
            GeneratedMesh.Destroy(pair.Value.Root);
        }

        foreach (DecorRing ring in _rings)
        {
            foreach (KeyValuePair<Vector2Int, GameObject> patch in ring.Patches)
                GeneratedMesh.Destroy(patch.Value);

            ring.Patches.Clear();
            ring.Planted = false;
        }

        while (_demolishing.Count > 0)
        {
            GeneratedMesh.Destroy(_demolishing.Dequeue());
        }

        _loaded.Clear();
        _cover.Clear();
        _pending.Clear();
        _inFlight.Clear();
        _decorQueue.Clear();

        _hasActive = false;

        if (_queue == null)
            return;

        _queue.Drain();
        _queue.Reset();
    }

    private static float Lowest(HeightMap map)
    {
        float lowest = float.MaxValue;

        foreach (float height in map.Heights)
            lowest = Mathf.Min(lowest, height);

        return lowest * map.MaxHeight;
    }

    private bool Validate()
    {
        if (_config == null || _voxels == null || _heightMap == null)
        {
            Debug.LogError("Voxel terrain: assign the WorldGenerationConfig, the VoxelConfig and HeightMap.bytes", this);
            return false;
        }

        if (_viewer == null)
        {
            Debug.LogError("Voxel terrain: no viewer transform assigned, there is nothing to build the world around", this);
            return false;
        }

        return true;
    }
}
