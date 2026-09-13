using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class VoxelTerrainStreamer : MonoBehaviour
{
    private readonly struct PendingChunk
    {
        public VoxelColumnKey Key { get; }
        public int Y { get; }

        public PendingChunk(VoxelColumnKey key, int y)
        {
            Key = key;
            Y = y;
        }
    }

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

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1024), SerializeField]
    private int _batchVertexBudget = 48000;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), Range(0f, 1f)]
    [Tooltip("Fraction of the grass the biomes ask for. Grass only ever covers the GrassDistance circle around the viewer, so this can stay at one unless that circle is very dense.")]
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

    private class Column
    {
        public VoxelColumnKey Key { get; }
        public GameObject Root { get; }

        public int Remaining { get; set; }

        public bool Ready => Remaining <= 0;

        public Column(VoxelColumnKey key, GameObject root, int chunks)
        {
            Key = key;
            Root = root;
            Remaining = chunks;
        }
    }

    private class DecorRing
    {
        public DecorScope Scope { get; }
        public float Reach { get; }

        public Dictionary<Vector2Int, GameObject> Patches { get; } = new();

        public Vector2 Sown { get; set; }
        public bool Planted { get; set; }

        public DecorRing(DecorScope scope, float reach)
        {
            Scope = scope;
            Reach = reach;
        }
    }

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

    public bool Initialized => _plan != null;

    public float Progress => _wantedAtStart <= 0 || _queue == null
        ? 0f
        : Mathf.Clamp01(1f - (_pending.Count + _queue.InFlight + _decorQueue.Count) / (float)(_wantedAtStart + _decorAtStart));

    [ShowNativeProperty]
    public string WorldStatus => _plan == null
        ? "not started"
        : $"{_loaded.Count} of {_wanted.Count} columns built, {_pending.Count} queued, {_queue.InFlight} chunks meshing on {_queue.Workers} workers, "
          + $"{_colliders.Waiting} waiting for collision, {Patches()} patches of decor, {_decorQueue.Count} decor builds waiting";

    private int Patches()
    {
        int patches = 0;

        foreach (DecorRing ring in _rings)
            patches += ring.Patches.Count;

        return patches;
    }

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

        using (WorldGenProbe.Measure(WorldGenStage.RuntimeDecorSetup))
            _decor = CreateDecor(map);

        _byDistance = ByDistance;
        _plan = new VoxelStreamPlan(_voxels, _lodCount, _nearDistance, _config.WorldSize);

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

    private VoxelDecorSpawner CreateDecor(HeightMap map)
    {
        if (!_spawnDecor || _biomes == null || !_biomes.IsValid() || _biomeMap == null)
            return null;

        BiomeMap biomeMap = BiomeMapTexture.Read(_biomeMap, _biomes, _config.WorldSize);

        if (biomeMap == null)
            return null;

        var weights = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendRadius);

        float[] roadMask = _roadMask == null ? null : MaskTexture.Read(_roadMask);
        int roadResolution = roadMask == null ? 0 : _roadMask.width;

        var filter = new DecorFilter(_config, weights, _biomes.Count, roadMask, roadResolution,
            _poiPlacement == null ? null : _poiPlacement.Placements, WidestFootprint());

        var placer = new VoxelDecorPlacer(_config, _biomes, _field, filter, _grassDensity);

        return placer.Layers.Count == 0 ? null : new VoxelDecorSpawner(placer, _voxels, _batchVertexBudget, false);
    }

    private float WidestFootprint()
    {
        float widest = 0f;

        for (int biome = 0; biome < _biomes.Count; biome++)
        {
            BiomeDefinition definition = _biomes.Get(biome);

            foreach (ScatterLayer layer in definition.Trees)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.Footprint);

            foreach (ScatterLayer layer in definition.Rocks)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.Footprint);
        }

        return widest;
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

    private void Plan(Vector2 ground)
    {
        _planned = true;

        _plan.Around(ground, _wanted);

        _sortOrigin = ground;
        _wanted.Sort(_byDistance);

        _cover.Clear();
        _pending.Clear();

        foreach (VoxelColumnKey key in _wanted)
        {
            _pending.Enqueue(key);

            Cover(key);
        }

        _wantedAtStart = _wanted.Count;

        Debug.Log($"Voxel terrain: building the whole {_config.WorldSize} m world once, {_wanted.Count} columns, "
            + $"{_plan.LodCount} levels of detail around ({ground.x:0}, {ground.y:0}) from {_plan.VoxelSize(0):0.##} m voxels "
            + $"out to {_plan.VoxelSize(_plan.LodCount - 1):0.##} m. Nothing is rebuilt afterwards", this);
    }

    private void Cover(VoxelColumnKey key)
    {
        int scale = 1 << key.Lod;

        for (int z = 0; z < scale; z++)
        {
            for (int x = 0; x < scale; x++)
                _cover[new Vector2Int(key.X * scale + x, key.Z * scale + z)] = key;
        }
    }

    private void Settle()
    {
        if (_ready || !_planned)
            return;

        if (_decorAtStart == 0)
            _decorAtStart = _decorQueue.Count;

        if (_hasActive || _pending.Count > 0 || _queue.InFlight > 0 || _decorQueue.Count > 0 || _colliders.Waiting > 0)
            return;

        _ready = true;

        WorldGenProbe.Mark(WorldGenMilestone.TerrainReady);

        Debug.Log($"Voxel terrain: world built in {Time.timeSinceLevelLoad:0.0} s. {WorldStatus}", this);
    }

    private void Demolish(GameObject patch)
    {
        if (patch == null)
            return;

        foreach (VoxelDecorBuild build in _decorQueue)
        {
            if (build.Owns(patch))
                build.Done = true;
        }

        patch.SetActive(false);

        _demolishing.Enqueue(patch);
    }

    private void Demolish()
    {
        int budget = _demolishPerFrame;

        while (budget > 0 && _demolishing.Count > 0)
        {
            GameObject patch = _demolishing.Peek();

            if (patch == null)
            {
                _demolishing.Dequeue();

                continue;
            }

            Transform root = patch.transform;

            while (budget > 0 && root.childCount > 0)
            {
                Transform child = root.GetChild(root.childCount - 1);

                child.SetParent(null, false);

                GeneratedMesh.Destroy(child.gameObject);

                budget--;
            }

            if (root.childCount > 0)
                break;

            _demolishing.Dequeue();

            GeneratedMesh.Destroy(patch);
        }
    }

    private void Decorate()
    {
        if (_decor == null || _decorQueue.Count == 0)
            return;

        float budget = _preload && !_ready ? _preloadBudget : _decorBudget;

        _clock.Restart();

        while (_decorQueue.Count > 0)
        {
            VoxelDecorBuild build = _decorQueue.Peek();

            _decor.Step(build, DECOR_SLICE);

            if (build.Done)
                _decorQueue.Dequeue();

            if (_clock.Elapsed.TotalMilliseconds >= budget)
                break;
        }
    }

    private void Collect()
    {
        if (_inFlight.Count == 0)
            return;

        _queue.Drain();

        for (int slot = 0; slot < _inFlight.Count; slot++)
        {
            PendingChunk chunk = _inFlight[slot];

            if (!_loaded.TryGetValue(chunk.Key, out Column column) || column.Root == null)
                continue;

            VoxelMesh mesh = _queue.Result(slot, out int chunkY);

            column.Remaining--;

            if (!mesh.IsEmpty)
                Spawn(mesh, column.Root.transform, $"{chunk.Key} y {chunkY}", Solid(chunk.Key.Lod));

            if (column.Ready)
                Show(column);
        }

        _queue.Reset();
        _inFlight.Clear();
    }

    private void Rush()
    {
        _clock.Restart();

        while (_pending.Count > 0 || _hasActive || _queue.InFlight > 0)
        {
            Dispatch();
            Collect();

            if (_clock.Elapsed.TotalMilliseconds >= _preloadBudget)
                break;
        }
    }

    private void Dispatch()
    {
        int started = 0;

        while (_queue.Free > 0)
        {
            if (!_hasActive)
            {
                if (started >= (_preload && !_ready ? int.MaxValue : _columnsPerFrame) || _pending.Count == 0)
                    break;

                VoxelColumnKey key = _pending.Dequeue();

                if (_loaded.ContainsKey(key))
                    continue;

                Open(key);
                started++;
            }

            while (_hasActive && _queue.Free > 0)
            {
                if (!_queue.TrySchedule(_active.Lod, _active.X, _activeY, _active.Z, _active.Seams, _active.Morph))
                    break;

                _inFlight.Add(new PendingChunk(_active, _activeY));

                _activeY++;
                _hasActive = _activeY <= _activeHigh;
            }
        }

        _queue.Kick();
    }

    private void Open(VoxelColumnKey key)
    {
        float size = _plan.ChunkMetres(key.Lod);

        VerticalRange(key, size, out int low, out int high);

        GameObject root = NewColumn(key);

        root.SetActive(false);

        var column = new Column(key, root, high - low + 1);

        _loaded[key] = column;

        _active = key;
        _activeY = low;
        _activeHigh = high;
        _hasActive = low <= high;

        if (column.Ready)
            Show(column);
    }

    private void Show(Column column)
    {
        if (column.Root == null || column.Root.activeSelf)
            return;

        column.Root.SetActive(true);
    }

    private void Sow(Vector2 ground)
    {
        if (_decor == null || !_planned)
            return;

        foreach (DecorRing ring in _rings)
            Sow(ring, ground);
    }

    private void Sow(DecorRing ring, Vector2 ground)
    {
        if (ring.Planted && DistanceUtility.WithinRadius(ground, ring.Sown, _decorStep))
            return;

        ring.Sown = ground;
        ring.Planted = true;

        float size = _plan.ChunkMetres(0);

        _faded.Clear();

        foreach (KeyValuePair<Vector2Int, GameObject> patch in ring.Patches)
        {
            if (SqrRange(patch.Key, size, ground) > (ring.Reach + size) * (ring.Reach + size))
                _faded.Add(patch.Key);
        }

        foreach (Vector2Int cell in _faded)
        {
            Demolish(ring.Patches[cell]);

            ring.Patches.Remove(cell);
        }

        int minX = Mathf.FloorToInt((ground.x - ring.Reach) / size);
        int maxX = Mathf.FloorToInt((ground.x + ring.Reach) / size);
        int minZ = Mathf.FloorToInt((ground.y - ring.Reach) / size);
        int maxZ = Mathf.FloorToInt((ground.y + ring.Reach) / size);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var cell = new Vector2Int(x, z);

                if (ring.Patches.ContainsKey(cell))
                    continue;

                if (SqrRange(cell, size, ground) > ring.Reach * ring.Reach)
                    continue;

                Grow(ring, cell, size);
            }
        }
    }

    private void Grow(DecorRing ring, Vector2Int cell, float size)
    {
        if (!_cover.TryGetValue(cell, out VoxelColumnKey column))
            return;

        var patch = new GameObject($"{ring.Scope} {cell.x} {cell.y}");

        patch.transform.SetParent(transform, false);

        ring.Patches[cell] = patch;

        _decorQueue.Enqueue(new VoxelDecorBuild(new Vector2(cell.x * size, cell.y * size), size, patch.transform,
            column.Lod, Frame(column), ring.Scope));
    }

    private void Rings()
    {
        _rings.Clear();

        if (_decor == null)
            return;

        _rings.Add(new DecorRing(DecorScope.Grass, _voxels.GrassDistance <= 0f ? _plan.Distance(0) : _voxels.GrassDistance));
        _rings.Add(new DecorRing(DecorScope.Rocks, _plan.Distance(Mathf.Min(_voxels.RockMaxLod, _plan.LodCount - 1))));
        _rings.Add(new DecorRing(DecorScope.Trees, _plan.Distance(Mathf.Min(_voxels.TreeMaxLod, _plan.LodCount - 1))));
    }

    private DecorSurface Frame(VoxelColumnKey column)
    {
        float span = _plan.ChunkMetres(column.Lod);

        return new DecorSurface(new Vector2(column.X * span, column.Z * span),
            VoxelDensitySampler.MorphSpan(_voxels.ChunkSize, _plan.VoxelSize(column.Lod)),
            _plan.VoxelSize(column.Lod), column.Morph);
    }

    private static float SqrRange(Vector2Int cell, float size, Vector2 point)
    {
        float dx = Mathf.Max(Mathf.Max(cell.x * size - point.x, 0f), point.x - (cell.x + 1) * size);
        float dz = Mathf.Max(Mathf.Max(cell.y * size - point.y, 0f), point.y - (cell.y + 1) * size);

        return dx * dx + dz * dz;
    }

    private bool Solid(int lod)
    {
        return lod <= _colliderMaxLod;
    }

    private int ByDistance(VoxelColumnKey first, VoxelColumnKey second)
    {
        return SqrRange(first).CompareTo(SqrRange(second));
    }

    private float SqrRange(VoxelColumnKey key)
    {
        float size = _plan.ChunkMetres(key.Lod);

        return DistanceUtility.SqrDistance(new Vector2((key.X + 0.5f) * size, (key.Z + 0.5f) * size), _sortOrigin);
    }

    private GameObject NewColumn(VoxelColumnKey key)
    {
        var column = new GameObject($"Column {key}");

        column.transform.SetParent(transform, false);

        return column;
    }

    private void Spawn(VoxelMesh source, Transform parent, string name, bool collider)
    {
        WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.VoxelSpawn);

        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);

        var mesh = new Mesh { name = name };

        if (source.VertexCount > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        using (WorldGenProbe.Measure(WorldGenStage.VoxelMeshUpload))
        {
            mesh.SetVertices(source.Vertices.AsArray());
            mesh.SetNormals(source.Normals.AsArray());
            mesh.SetUVs(0, source.Uv.AsArray());
            mesh.SetIndices(source.Triangles.AsArray(), MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
        }

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        holder.AddComponent<MeshRenderer>().sharedMaterial = _material;

        if (collider)
            _colliders.Add(holder, mesh);

        GeneratedMesh.Own(holder);

        WorldGenProbe.Mark(WorldGenMilestone.FirstTerrain);
        span.Finish(source.TriangleCount);
    }

    private void VerticalRange(VoxelColumnKey key, float size, out int low, out int high)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        float step = _plan.VoxelSize(key.Lod);

        for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
        {
            for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
            {
                float surface = _field.Surface(x, z);

                lowest = Mathf.Min(lowest, surface);
                highest = Mathf.Max(highest, surface);
            }
        }

        low = Mathf.FloorToInt(lowest / size) - 1;
        high = Mathf.FloorToInt(highest / size) + 1;
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
