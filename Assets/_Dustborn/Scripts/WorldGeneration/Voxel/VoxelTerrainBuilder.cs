using System.Collections.Generic;
using System.Diagnostics;
using NaughtyAttributes;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class VoxelTerrainBuilder : MonoBehaviour
{
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

    [BoxGroup("Splatmap"), ShowIf(nameof(_paintSplatmap)), MinValue(256)]
    [Tooltip("Side of the world control textures. Match it to the height map: at HeightCellSize 2 over a 4096 m world that is 2048, two metres per texel and exactly the resolution the splat is derived from. Going finer only upsamples, and every step doubles both the texture and the buffer the bake needs.")]
    [SerializeField]
    private int _controlResolution = 2048;

    [BoxGroup("Decor"), SerializeField] private bool _spawnDecor = true;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), SerializeField]
    private PoiPlacementAsset _poiPlacement;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), MinValue(1024)]
    [Tooltip("Vertex budget of one combined batch. Bigger batches mean fewer draw calls but coarser culling, since a batch is culled as a whole.")]
    [SerializeField]
    private int _batchVertexBudget = 48000;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor)), Range(0f, 1f)]
    [Tooltip("Fraction of the grass the biomes ask for. Grass reaches only as far as GrassMaxLod, the ring around the centre, so the whole world does not carry it.")]
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
    private int _lodCount = 5;

    [BoxGroup("World"), MinValue(32f)]
    [Tooltip("Radius of the finest ring in metres, measured from the centre.")]
    [SerializeField]
    private float _nearDistance = 96f;

    [BoxGroup("World"), MinValue(16)]
    [Tooltip("Memory the built world may take in megabytes. The build is refused before it starts if the estimate goes over, because running out of memory takes the editor down with it.")]
    [SerializeField]
    private int _memoryBudget = 512;

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
        return new VoxelStreamPlan(_voxels, _lodCount, _nearDistance, _config.WorldSize);
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

        using var field = new VoxelDensityField(map, _voxels, Lowest(map));
        using var mesher = new VoxelChunkMesher(_voxels, field);
        using var mesh = new VoxelMesh();

        VoxelStreamPlan plan = Plan();

        plan.Around(_center, _columns);

        var clock = Stopwatch.StartNew();
        long geometry = WorldGenProbe.Now;

        int triangles = 0;
        int attempted = 0;
        int completed = 0;

        foreach (VoxelColumnKey key in _columns)
        {
            progress?.Invoke("Геометрия ландшафта", 0.7f * completed++ / Mathf.Max(1, _columns.Count));
            float size = plan.ChunkMetres(key.Lod);

            VerticalRange(field, key, size, plan.VoxelSize(key.Lod), out int low, out int high);

            Transform column = NewColumn(key);

            for (int y = low; y <= high; y++)
            {
                attempted++;
                mesher.Mesh(key.Lod, key.X, y, key.Z, mesh, key.Seams, key.Morph);

                if (mesh.IsEmpty)
                    continue;

                triangles += mesh.TriangleCount;
                Spawn(mesh, column, $"Chunk {key} y {y}");
            }

            if (column.childCount > 0)
                continue;

            _roots.Remove(key);

            GeneratedMesh.Destroy(column.gameObject);
        }

        clock.Stop();
        WorldGenProbe.Record(WorldGenStage.PreviewGeometry, geometry, WorldGenProbe.Now, triangles);

        string splat = PaintSplatmap(map);
        string decor;

        using (WorldGenProbe.Measure(WorldGenStage.PreviewDecor))
            decor = SpawnDecor(field, plan, progress);

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
        _poiPlacement = world.Placement;
        _spawnDecor = settings.SpawnDecor;
        _grassDensity = settings.GrassDensity;
        _batchVertexBudget = settings.BatchVertexBudget;
        _buildColliders = settings.PreviewColliders;
        _center = settings.PreviewCenter;
        _lodCount = settings.LodCount;
        _nearDistance = settings.NearDistance;
        _memoryBudget = settings.MemoryBudget;
    }

    private string SpawnDecor(VoxelDensityField field, VoxelStreamPlan plan, System.Action<string, float> progress)
    {
        if (!_spawnDecor)
            return "no decor";

        if (_biomes == null || !_biomes.IsValid() || _biomeMap == null)
            return "no decor: assign the BiomeDatabase and the BiomeMap texture";

        BiomeMap biomeMap = BiomeMapTexture.Read(_biomeMap, _biomes, _config.WorldSize);

        if (biomeMap == null)
            return "no decor";

        if (_poiPlacement == null)
            Debug.LogWarning("Grass and trees will grow through the houses: PoiPlacement.asset is not assigned", this);

        var weightField = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendRadius);

        float[] roadMask = _roadMask == null ? null : MaskTexture.Read(_roadMask);
        int roadMaskResolution = roadMask == null ? 0 : _roadMask.width;

        var filter = new DecorFilter(_config, weightField, _biomes.Count, roadMask, roadMaskResolution,
            _poiPlacement == null ? null : _poiPlacement.Placements, WidestFootprint());

        var placer = new VoxelDecorPlacer(_config, _biomes, field, filter, _grassDensity);

        if (placer.Layers.Count == 0)
            return "no decor: no biome has trees, rocks or grass with a prefab";

        var spawner = new VoxelDecorSpawner(placer, _voxels, _batchVertexBudget, _decorColliders);
        var clock = Stopwatch.StartNew();

        _decor = spawner;
        int completed = 0;

        foreach (VoxelColumnKey key in _columns)
        {
            progress?.Invoke("Трава, деревья и камни", 0.7f + 0.3f * completed++ / Mathf.Max(1, _columns.Count));
            float size = plan.ChunkMetres(key.Lod);

            if (!_roots.TryGetValue(key, out Transform column))
                continue;

            var bucket = new GameObject($"Decor {key}");

            bucket.transform.SetParent(column, false);

            var origin = new Vector2(key.X * size, key.Z * size);
            var frame = new DecorSurface(origin,
                VoxelDensitySampler.MorphSpan(_voxels.ChunkSize, plan.VoxelSize(key.Lod)),
                plan.VoxelSize(key.Lod), key.Morph);

            spawner.Spawn(origin, size, bucket.transform, key.Lod, frame);

            if (bucket.transform.childCount == 0)
                GeneratedMesh.Destroy(bucket);
        }

        clock.Stop();

        Debug.Log($"Decor layers of the voxel terrain:\n{spawner.Describe()}", this);

        return $"decor: {spawner.Placed} placed, {spawner.Combined} combined into {spawner.Batches} batches, "
            + $"{spawner.Instanced} instanced, {spawner.Instantiated} left as objects, {clock.ElapsedMilliseconds} ms";
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

    private string PaintSplatmap(HeightMap map)
    {
        if (!_paintSplatmap)
            return "no splatmap";

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

        string status = VoxelGroundMaterial.Bake(request, this);

        _material = request.Material;

        foreach (MeshFilter chunk in _chunks)
        {
            if (chunk != null && chunk.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = _material;
        }

        return status;
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

    private Transform NewColumn(VoxelColumnKey key)
    {
        var column = new GameObject($"Column {key}");

        column.transform.SetParent(transform, false);

        _roots[key] = column.transform;

        return column.transform;
    }

    private void Spawn(VoxelMesh source, Transform parent, string name)
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

        if (_buildColliders)
            holder.AddComponent<MeshCollider>().sharedMesh = mesh;

        GeneratedMesh.Own(holder);

        _chunks.Add(holder.GetComponent<MeshFilter>());
    }

    private void VerticalRange(VoxelDensityField field, VoxelColumnKey key, float size, float step, out int low, out int high)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
        {
            for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
            {
                float surface = field.Surface(x, z);

                lowest = Mathf.Min(lowest, surface);
                highest = Mathf.Max(highest, surface);
            }
        }

        low = Mathf.FloorToInt(lowest / size) - 1;
        high = Mathf.FloorToInt(highest / size) + 1;
    }

    private static float Lowest(HeightMap map)
    {
        float lowest = float.MaxValue;

        foreach (float height in map.Heights)
            lowest = Mathf.Min(lowest, height);

        return lowest * map.MaxHeight;
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
