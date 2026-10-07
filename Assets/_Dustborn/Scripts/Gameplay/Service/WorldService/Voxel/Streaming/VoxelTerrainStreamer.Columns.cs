using UnityEngine;

public partial class VoxelTerrainStreamer
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

#if UNITY_EDITOR
        VoxelColumnDebug.Attach(root, key, size, _plan.VoxelSize(key.Lod), low, high);
#endif

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

    private bool Solid(int lod)
    {
        return lod <= _colliderMaxLod;
    }

    private int ByDistance(VoxelColumnKey first, VoxelColumnKey second)
    {
        return SqrRange(first).CompareTo(SqrRange(second));
    }

    private static float SqrRange(Vector2Int cell, float size, Vector2 point)
    {
        float dx = Mathf.Max(Mathf.Max(cell.x * size - point.x, 0f), point.x - (cell.x + 1) * size);
        float dz = Mathf.Max(Mathf.Max(cell.y * size - point.y, 0f), point.y - (cell.y + 1) * size);

        return dx * dx + dz * dz;
    }

    private float SqrRange(VoxelColumnKey key)
    {
        float size = _plan.ChunkMetres(key.Lod);

        return DistanceUtility.SqrDistance(new Vector2((key.X + 0.5f) * size, (key.Z + 0.5f) * size), _sortOrigin);
    }
}
