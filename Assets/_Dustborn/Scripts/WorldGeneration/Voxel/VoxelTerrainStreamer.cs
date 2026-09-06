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
    private int _controlResolution = 2048;

    [BoxGroup("Decor"), SerializeField] private bool _spawnDecor = true;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private BiomeDatabase _biomes;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private Texture2D _biomeMap;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private Texture2D _roadMask;
    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField] private PoiPlacementAsset _poiPlacement;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1024), SerializeField]
    private int _batchVertexBudget = 48000;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), Range(0f, 1f)]
    [Tooltip("Fraction of the grass the biomes ask for. Streaming already keeps grass to the near rings, so this can stay at one unless the near ring is very dense.")]
    [SerializeField]
    private float _grassDensity = 1f;

    [BoxGroup("Streaming"), Required("The transform the world is streamed around, usually the player"), SerializeField]
    private Transform _viewer;

    [BoxGroup("Streaming"), Range(1, 6)]
    [Tooltip("Levels of detail. Each one doubles both the voxel size and the radius it covers.")]
    [SerializeField]
    private int _lodCount = 5;

    [BoxGroup("Streaming"), MinValue(32f)]
    [Tooltip("Radius of the finest ring in metres. Everything closer is built at the full voxel size.")]
    [SerializeField]
    private float _nearDistance = 96f;

    [BoxGroup("Streaming"), MinValue(1)]
    [Tooltip("Chunk columns started per frame. The rest wait in the queue, so a jump across the map costs frames rather than a freeze.")]
    [SerializeField]
    private int _columnsPerFrame = 3;

    [BoxGroup("Streaming"), Range(1, 16)]
    [Tooltip("Chunk meshing jobs allowed to run at once. They are scheduled one frame and picked up the next, so the meshing happens on worker threads while the main thread runs the game. Each worker holds about 0.3 MB of scratch buffers.")]
    [SerializeField]
    private int _meshWorkers = 8;

    [BoxGroup("Streaming"), MinValue(0f)]
    [Tooltip("How far the viewer has to move before the wanted set is recomputed.")]
    [SerializeField]
    private float _refreshStep = 16f;

    [BoxGroup("Streaming"), MinValue(0f)]
    [Tooltip("Metres the rings are pushed along the direction of travel. The ground a walking player is about to stand on is built before they get there, and the queue is sorted from that point too, so what is ahead is built before what is behind. Zero centres everything on the viewer.")]
    [SerializeField]
    private float _lookAhead = 32f;

    [BoxGroup("Streaming")]
    [Tooltip("Colliders are built for the finest ring only: you can only walk on what is near, and a collider on a coarse chunk would not match the ground you see.")]
    [SerializeField]
    private bool _nearColliders = true;

    [BoxGroup("Streaming"), MinValue(1)]
    [Tooltip("Collision meshes cooked at once. Cooking runs on worker threads and the collider is attached the next frame, so this is how many chunks may wait for ground to stand on.")]
    [SerializeField]
    private int _collidersPerFrame = 3;

    [BoxGroup("Streaming"), MinValue(0.5f)]
    [Tooltip("Milliseconds per frame spent placing decor. A column is not shown until its decor is done, so this also sets how fast ground appears: too small and the queue falls behind the columns being opened, too large and placement costs a visible hitch.")]
    [SerializeField]
    private float _decorBudget = 6f;

    [BoxGroup("Streaming"), MinValue(1)]
    [Tooltip("Children destroyed per frame when a column is unloaded.")]
    [SerializeField]
    private int _demolishPerFrame = 256;

    private class Column
    {
        public VoxelColumnKey Key { get; }
        public GameObject Root { get; }

        public int Remaining { get; set; }

        public VoxelDecorBuild Decor { get; set; }

        public bool Ready => Remaining <= 0 && (Decor == null || Decor.Done);

        public bool Visible => Root != null && Root.activeSelf;

        public Column(VoxelColumnKey key, GameObject root, int chunks)
        {
            Key = key;
            Root = root;
            Remaining = chunks;
        }
    }

    private const int RETIRED_LIMIT = 64;
    private const int DECOR_SLICE = 48;
    private const int DECOR_BACKLOG = 4;
    private const float STEER_EPSILON = 1e-4f;
    private const float STEER_SMOOTHING = 0.05f;

    private readonly Dictionary<VoxelColumnKey, Column> _loaded = new();
    private readonly List<Column> _retired = new();
    private readonly List<VoxelColumnKey> _wanted = new();
    private readonly Queue<VoxelColumnKey> _pending = new();
    private readonly List<VoxelColumnKey> _stale = new();
    private readonly HashSet<VoxelColumnKey> _keep = new();
    private readonly List<PendingChunk> _inFlight = new();
    private readonly Queue<VoxelDecorBuild> _decorQueue = new();
    private readonly Queue<GameObject> _demolishing = new();

    private VoxelDensityField _field;
    private VoxelDecorSpawner _decor;
    private VoxelMeshQueue _queue;
    private VoxelColliderQueue _colliders;
    private VoxelStreamPlan _plan;

    private Vector2 _lastRefresh = new(float.MinValue, float.MinValue);
    private Vector2 _lastGround;
    private Vector2 _heading;

    private bool _releaseDue;

    private VoxelColumnKey _active;
    private int _activeY;
    private int _activeHigh;
    private bool _hasActive;

    private readonly System.Diagnostics.Stopwatch _clock = new();

    private Comparison<VoxelColumnKey> _byDistance;
    private Vector2 _sortOrigin;

    [ShowNativeProperty]
    public string StreamStatus => _plan == null
        ? "not started"
        : $"{_loaded.Count} columns loaded, {_pending.Count} queued, {_retired.Count} kept until replaced, "
          + $"{_queue.InFlight} chunks meshing on {_queue.Workers} workers, view distance {_plan.ViewDistance:0} m";

    private void OnEnable()
    {
        if (!Validate())
            return;

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        _field = new VoxelDensityField(map, _voxels, Lowest(map));
        _queue = new VoxelMeshQueue(_voxels, _field, _meshWorkers);
        _colliders = new VoxelColliderQueue(_collidersPerFrame);

        _decor = CreateDecor(map);

        _byDistance = ByDistance;
        _plan = new VoxelStreamPlan(_voxels, _lodCount, _nearDistance);

        _lastRefresh = new Vector2(float.MinValue, float.MinValue);
        _lastGround = new Vector2(_viewer.position.x, _viewer.position.z);
        _heading = Vector2.zero;
    }

    private VoxelDecorSpawner CreateDecor(HeightMap map)
    {
        if (!_spawnDecor || _biomes == null || !_biomes.IsValid() || _biomeMap == null)
            return null;

        BiomeMap biomeMap = BiomeMapTexture.Read(_biomeMap, _biomes, _config.WorldSize);

        if (biomeMap == null)
            return null;

        var weights = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendPasses);

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
        Unload();

        _colliders?.Dispose();
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
    }

    private void Update()
    {
        if (_plan == null || _viewer == null)
            return;

        Vector3 position = _viewer.position;
        var ground = new Vector2(position.x, position.z);

        _colliders.Collect();

        Collect();

        Steer(ground);

        if (!DistanceUtility.WithinRadius(ground, _lastRefresh, _refreshStep))
        {
            _lastRefresh = ground;
            Refresh(ground + _heading * _lookAhead);
        }

        Dispatch();

        Decorate();

        if (_releaseDue)
        {
            _releaseDue = false;

            Release();
            Promote();
        }

        Demolish();

        _colliders.Dispatch();
    }

    // The heading follows travel rather than where the camera looks: turning on the spot must not
    // move the rings, or every look around costs a band of columns.
    private void Steer(Vector2 ground)
    {
        Vector2 step = ground - _lastGround;

        _lastGround = ground;

        if (step.sqrMagnitude < STEER_EPSILON)
            return;

        _heading = Vector2.Lerp(_heading, step.normalized, STEER_SMOOTHING).normalized;
    }

    private void Demolish(GameObject column)
    {
        if (column == null)
            return;

        foreach (VoxelDecorBuild build in _decorQueue)
        {
            if (build.Owns(column))
                build.Done = true;
        }

        column.SetActive(false);

        _demolishing.Enqueue(column);
    }

    private void Demolish()
    {
        int budget = _demolishPerFrame;

        while (budget > 0 && _demolishing.Count > 0)
        {
            GameObject column = _demolishing.Peek();

            if (column == null)
            {
                _demolishing.Dequeue();

                continue;
            }

            Transform root = column.transform;

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

            GeneratedMesh.Destroy(column);
        }
    }

    private void Decorate()
    {
        if (_decor == null || _decorQueue.Count == 0)
            return;

        _clock.Restart();

        while (_decorQueue.Count > 0)
        {
            VoxelDecorBuild build = _decorQueue.Peek();

            _decor.Step(build, DECOR_SLICE);

            if (build.Done)
            {
                _decorQueue.Dequeue();

                _releaseDue = true;
            }

            if (_clock.Elapsed.TotalMilliseconds >= _decorBudget)
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
                Spawn(mesh, column.Root.transform, $"{chunk.Key} y {chunkY}", chunk.Key.Lod == 0 && _nearColliders);

            if (column.Remaining <= 0)
                _releaseDue = true;
        }

        _queue.Reset();
        _inFlight.Clear();
    }

    private void Dispatch()
    {
        int started = 0;

        while (_queue.Free > 0)
        {
            if (!_hasActive)
            {
                if (started >= _columnsPerFrame || _pending.Count == 0)
                    break;

                if (_decorQueue.Count >= DECOR_BACKLOG)
                    break;

                VoxelColumnKey key = _pending.Dequeue();

                if (_loaded.ContainsKey(key))
                    continue;

                Open(key);
                started++;
            }

            while (_hasActive && _queue.Free > 0)
            {
                if (!_queue.TrySchedule(_active.Lod, _active.X, _activeY, _active.Z, _active.Trim))
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

        if (_decor != null)
        {
            column.Decor = new VoxelDecorBuild(new Vector2(key.X * size, key.Z * size), size, root.transform, key.Lod);

            _decorQueue.Enqueue(column.Decor);
        }

        _active = key;
        _activeY = low;
        _activeHigh = high;
        _hasActive = low <= high;

        if (column.Ready)
            _releaseDue = true;
    }

    // Ground that replaces ground waits for everything, so the swap carries no doubled surface and no
    // half-decorated frame. Ground that replaces nothing is shown the moment it has geometry: waiting
    // for its grass there would only mean a hole, and there is nothing underneath to fight with.
    private void Promote()
    {
        foreach (KeyValuePair<VoxelColumnKey, Column> pair in _loaded)
        {
            Column column = pair.Value;

            if (column.Visible || column.Root == null)
                continue;

            if (Retires(column.Key))
            {
                if (!column.Ready)
                    continue;
            }
            else if (column.Remaining > 0)
            {
                continue;
            }

            column.Root.SetActive(true);
        }
    }

    private bool Retires(VoxelColumnKey key)
    {
        foreach (Column retired in _retired)
        {
            if (Overlaps(key, retired.Key))
                return true;
        }

        return false;
    }

    private void Refresh(Vector2 ground)
    {
        _releaseDue = true;

        _plan.Around(ground, _wanted);

        _sortOrigin = ground;
        _wanted.Sort(_byDistance);

        _keep.Clear();

        foreach (VoxelColumnKey key in _wanted)
            _keep.Add(key);

        _stale.Clear();

        foreach (KeyValuePair<VoxelColumnKey, Column> pair in _loaded)
        {
            if (!_keep.Contains(pair.Key))
                _stale.Add(pair.Key);
        }

        foreach (VoxelColumnKey key in _stale)
        {
            if (_hasActive && _active.Equals(key))
                _hasActive = false;

            Retire(_loaded[key]);
            _loaded.Remove(key);
        }

        _pending.Clear();

        foreach (VoxelColumnKey key in _wanted)
        {
            if (_loaded.ContainsKey(key))
                continue;

            Column revived = Revive(key);

            if (revived == null)
                _pending.Enqueue(key);
            else
                _loaded[key] = revived;
        }
    }

    private void Retire(Column column)
    {
        if (!column.Ready)
        {
            Demolish(column.Root);

            return;
        }

        _retired.Add(column);

        while (_retired.Count > RETIRED_LIMIT)
            Demolish(Evict());
    }

    // A retired column is the only ground under the area it covers until the replacement is shown,
    // so eviction takes one that covers nothing unfinished, and only then the farthest.
    private GameObject Evict()
    {
        int worst = 0;
        float range = float.MinValue;

        for (int i = 0; i < _retired.Count; i++)
        {
            if (!Blocked(_retired[i].Key))
            {
                worst = i;

                break;
            }

            float distance = SqrRange(_retired[i].Key);

            if (distance <= range)
                continue;

            range = distance;
            worst = i;
        }

        GameObject root = _retired[worst].Root;

        _retired.RemoveAt(worst);

        return root;
    }

    private Column Revive(VoxelColumnKey key)
    {
        for (int i = 0; i < _retired.Count; i++)
        {
            if (!_retired[i].Key.Equals(key))
                continue;

            Column column = _retired[i];

            _retired.RemoveAt(i);

            return column;
        }

        return null;
    }

    private void Release()
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            if (Blocked(_retired[i].Key))
                continue;

            Demolish(_retired[i].Root);
            _retired.RemoveAt(i);
        }
    }

    private bool Blocked(VoxelColumnKey key)
    {
        foreach (VoxelColumnKey pending in _pending)
        {
            if (Overlaps(key, pending))
                return true;
        }

        foreach (KeyValuePair<VoxelColumnKey, Column> pair in _loaded)
        {
            if (!pair.Value.Ready && Overlaps(key, pair.Key))
                return true;
        }

        return false;
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

    private bool Overlaps(VoxelColumnKey a, VoxelColumnKey b)
    {
        float sizeA = _plan.ChunkMetres(a.Lod);
        float sizeB = _plan.ChunkMetres(b.Lod);

        return a.X * sizeA < (b.X + 1) * sizeB && b.X * sizeB < (a.X + 1) * sizeA
            && a.Z * sizeA < (b.Z + 1) * sizeB && b.Z * sizeB < (a.Z + 1) * sizeA;
    }


    private GameObject NewColumn(VoxelColumnKey key)
    {
        var column = new GameObject($"Column {key}");

        column.transform.SetParent(transform, false);

        return column;
    }

    private void Spawn(VoxelMesh source, Transform parent, string name, bool collider)
    {
        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);

        var mesh = new Mesh { name = name };

        if (source.VertexCount > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(source.Vertices.AsArray());
        mesh.SetNormals(source.Normals.AsArray());
        mesh.SetUVs(0, source.Uv.AsArray());
        mesh.SetIndices(source.Triangles.AsArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        holder.AddComponent<MeshRenderer>().sharedMaterial = _material;

        if (collider)
            _colliders.Add(holder, mesh);

        GeneratedMesh.Own(holder);
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
            Debug.LogError("Voxel streaming: assign the WorldGenerationConfig and HeightMap.bytes before baking the material", this);
            return;
        }

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        var request = new VoxelGroundMaterial.Request
        {
            Config = _config,
            Biomes = _biomes,
            BiomeMap = _biomeMap,
            RoadMask = _roadMask,
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

    [Button("Unload Everything")]
    public void Unload()
    {
        foreach (KeyValuePair<VoxelColumnKey, Column> pair in _loaded)
        {
            GeneratedMesh.Destroy(pair.Value.Root);
        }

        foreach (Column column in _retired)
        {
            GeneratedMesh.Destroy(column.Root);
        }

        while (_demolishing.Count > 0)
        {
            GeneratedMesh.Destroy(_demolishing.Dequeue());
        }

        _retired.Clear();
        _loaded.Clear();
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
            Debug.LogError("Voxel streaming: assign the WorldGenerationConfig, the VoxelConfig and HeightMap.bytes", this);
            return false;
        }

        if (_viewer == null)
        {
            Debug.LogError("Voxel streaming: no viewer transform assigned, there is nothing to stream around", this);
            return false;
        }

        return true;
    }
}
