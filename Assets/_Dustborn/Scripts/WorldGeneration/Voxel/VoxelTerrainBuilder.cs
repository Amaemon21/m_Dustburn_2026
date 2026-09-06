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
    [Tooltip("Fraction of the grass the biomes ask for. Unity Terrain could afford full density because detailObjectDistance culled it at 200 m; a mesh has no such cull until chunk streaming lands, and full density is hundreds of thousands of bushes per square kilometre.")]
    [SerializeField]
    private float _grassDensity = 1f;

    [BoxGroup("Decor"), ShowIf(nameof(_spawnDecor))]
    [Tooltip("Keep the colliders that decor prefabs carry. Turning this on stops such prefabs from being combined, because a combined mesh has no per object collider.")]
    [SerializeField]
    private bool _decorColliders;

    [BoxGroup("Region")]
    [Tooltip("Centre of the area to build, in metres. The whole world at one metre per voxel does not fit in memory, so the builder only covers a patch.")]
    [SerializeField]
    private Vector2 _center = new(1024f, 1024f);

    [BoxGroup("Region"), MinValue(1), SerializeField]
    private int _chunksPerSide = 8;

    [BoxGroup("Region"), MinValue(16)]
    [Tooltip("Memory the built region may take in megabytes. The build is refused before it starts if the estimate goes over, because running out of memory takes the editor down with it.")]
    [SerializeField]
    private int _memoryBudget = 512;

    private readonly List<MeshFilter> _chunks = new();

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
    public string RegionStatus => _voxels == null
        ? "VoxelConfig is not assigned"
        : $"{_chunksPerSide}x{_chunksPerSide} chunks of {_voxels.ChunkMetres} m, {_chunksPerSide * _voxels.ChunkMetres} m across, {_chunks.Count} built";

    [ShowNativeProperty]
    public string BuildCost => _voxels == null ? "VoxelConfig is not assigned" : Estimate().Describe();

    private VoxelBudget Estimate()
    {
        float side = _chunksPerSide * _voxels.ChunkMetres;

        return VoxelBudget.Estimate(_voxels.VoxelSize, side * side, _buildColliders);
    }

    [Button("Build Voxel Terrain")]
    public void Build()
    {
        if (!Validate())
            return;

        if (!Affordable())
            return;

        Clear();

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        using var field = new VoxelDensityField(map, _voxels, Lowest(map));
        using var mesher = new VoxelChunkMesher(_voxels, field);
        using var mesh = new VoxelMesh();

        float span = _voxels.ChunkMetres;

        int originX = Mathf.FloorToInt((_center.x - _chunksPerSide * span * 0.5f) / span);
        int originZ = Mathf.FloorToInt((_center.y - _chunksPerSide * span * 0.5f) / span);

        VerticalRange(field, originX, originZ, span, out int low, out int high);

        var clock = Stopwatch.StartNew();

        int triangles = 0;
        int attempted = 0;

        for (int z = 0; z < _chunksPerSide; z++)
        {
            for (int x = 0; x < _chunksPerSide; x++)
            {
                for (int y = low; y <= high; y++)
                {
                    attempted++;
                    mesher.Mesh(0, originX + x, y, originZ + z, mesh);

                    if (mesh.IsEmpty)
                        continue;

                    triangles += mesh.TriangleCount;
                    Spawn(mesh, originX + x, y, originZ + z);
                }
            }
        }

        clock.Stop();

        string splat = PaintSplatmap(map);
        string decor = SpawnDecor(field, originX, originZ, span);

        Debug.Log($"Voxel terrain: {_chunks.Count} chunks of {_voxels.ChunkSize} voxels at {_voxels.VoxelSize} m, "
            + $"{triangles} triangles, {attempted} chunks visited, {clock.ElapsedMilliseconds} ms. {splat}. {decor}", this);
    }

    private string SpawnDecor(VoxelDensityField field, int originX, int originZ, float span)
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

        var weightField = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendPasses);

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

        for (int z = 0; z < _chunksPerSide; z++)
        {
            for (int x = 0; x < _chunksPerSide; x++)
            {
                var bucket = new GameObject($"Decor {originX + x} {originZ + z}");

                bucket.transform.SetParent(transform, false);

                spawner.Spawn(new Vector2((originX + x) * span, (originZ + z) * span), span, bucket.transform);

                if (bucket.transform.childCount == 0)
                    GeneratedMesh.Destroy(bucket);
            }
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
        _chunks.Clear();

        _decor?.Dispose();
        _decor = null;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GeneratedMesh.Destroy(transform.GetChild(i).gameObject);
        }
    }

    private void Spawn(VoxelMesh source, int chunkX, int chunkY, int chunkZ)
    {
        var holder = new GameObject($"Chunk {chunkX} {chunkY} {chunkZ}");

        holder.transform.SetParent(transform, false);

        var mesh = new Mesh { name = holder.name };

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

    private void VerticalRange(VoxelDensityField field, int originX, int originZ, float span, out int low, out int high)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        float from = originX * span;
        float to = (originX + _chunksPerSide) * span;
        float fromZ = originZ * span;
        float toZ = (originZ + _chunksPerSide) * span;

        for (float z = fromZ; z <= toZ; z += _voxels.VoxelSize)
        {
            for (float x = from; x <= to; x += _voxels.VoxelSize)
            {
                float surface = field.Surface(x, z);

                lowest = Mathf.Min(lowest, surface);
                highest = Mathf.Max(highest, surface);
            }
        }

        low = Mathf.FloorToInt(lowest / span) - 1;
        high = Mathf.FloorToInt(highest / span) + 1;
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

        float side = _chunksPerSide * _voxels.ChunkMetres;

        Debug.LogError($"Voxel terrain: {side:0} m at {_voxels.VoxelSize} m per voxel is {budget.Describe()}, over the {_memoryBudget} MB budget. "
            + $"The build is refused because running out of memory takes the editor down. {VoxelBudget.Advise(_voxels.VoxelSize, side * side, _memoryBudget, _buildColliders)}, "
            + "or lower ChunksPerSide, or raise the budget if the machine really has the memory", this);

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
