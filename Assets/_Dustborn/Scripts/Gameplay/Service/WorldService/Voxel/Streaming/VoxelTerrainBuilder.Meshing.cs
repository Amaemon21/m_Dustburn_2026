using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;

public partial class VoxelTerrainBuilder
{
    private readonly struct ChunkTask
    {
        public VoxelColumnKey Key { get; }
        public Transform Column { get; }
        public int Y { get; }

        public ChunkTask(VoxelColumnKey key, Transform column, int y)
        {
            Key = key;
            Column = column;
            Y = y;
        }
    }

    private List<ChunkTask> CollectChunks(VoxelDensityField field, VoxelStreamPlan plan)
    {
        var tasks = new List<ChunkTask>();

        foreach (VoxelColumnKey key in _columns)
        {
            float size = plan.ChunkMetres(key.Lod);

            field.ChunkRange(key, size, plan.VoxelSize(key.Lod), out int low, out int high);

            Transform column = NewColumn(key);

#if UNITY_EDITOR
            VoxelColumnDebug.Attach(column.gameObject, key, size, plan.VoxelSize(key.Lod), low, high);
#endif

            for (int y = low; y <= high; y++)
                tasks.Add(new ChunkTask(key, column, y));
        }

        return tasks;
    }

    private int MeshChunks(VoxelDensityField field, List<ChunkTask> tasks, System.Action<string, float> progress)
    {
        int workers = Mathf.Clamp(SystemInfo.processorCount - 1, 1, MAX_MESH_WORKERS);
        var meshers = new VoxelChunkMesher[2 * workers];
        var meshes = new VoxelMesh[2 * workers];
        var handles = new JobHandle[2 * workers];
        VoxelColliderQueue colliders = _buildColliders ? new VoxelColliderQueue(COLLIDER_BATCH) : null;
        int triangles = 0;

        try
        {
            for (int i = 0; i < meshers.Length; i++)
            {
                meshers[i] = new VoxelChunkMesher(_voxels, field);
                meshes[i] = new VoxelMesh();
            }

            ScheduleBatch(tasks, 0, workers, 0, meshers, meshes, handles);

            for (int start = 0, bank = 0; start < tasks.Count; start += workers, bank ^= 1)
            {
                progress?.Invoke("Геометрия ландшафта", 0.7f * start / tasks.Count);
                int count = Mathf.Min(workers, tasks.Count - start);
                int offset = bank * workers;

                ScheduleBatch(tasks, start + workers, workers, (bank ^ 1) * workers, meshers, meshes, handles);

                for (int i = 0; i < count; i++)
                    handles[offset + i].Complete();

                RoadSurfaceMesh.Geometry[] roads = RoadSurfaces(tasks, start, meshes, offset, count);

                for (int i = 0; i < count; i++)
                {
                    if (meshes[offset + i].IsEmpty)
                        continue;

                    ChunkTask task = tasks[start + i];
                    triangles += meshes[offset + i].TriangleCount;
                    Spawn(meshes[offset + i], task.Column, $"Chunk {task.Key} y {task.Y}", colliders, roads[i]);
                }
            }

            if (colliders != null)
            {
                progress?.Invoke("Коллизия ландшафта", 0.7f);
                colliders.Flush();
            }
        }
        finally
        {
            foreach (JobHandle handle in handles)
                handle.Complete();

            foreach (VoxelChunkMesher mesher in meshers)
                mesher?.Dispose();

            foreach (VoxelMesh mesh in meshes)
                mesh?.Dispose();

            colliders?.Dispose();
        }

        return triangles;
    }

    private static void ScheduleBatch(List<ChunkTask> tasks, int start, int workers, int offset, VoxelChunkMesher[] meshers, VoxelMesh[] meshes, JobHandle[] handles)
    {
        int count = Mathf.Min(workers, tasks.Count - start);

        for (int i = 0; i < count; i++)
        {
            ChunkTask task = tasks[start + i];
            handles[offset + i] = meshers[offset + i].Schedule(task.Key.Lod, task.Key.X, task.Y, task.Key.Z, meshes[offset + i], task.Key.Seams, task.Key.Morph);
        }

        if (count > 0)
            JobHandle.ScheduleBatchedJobs();
    }

    private void RemoveEmptyColumns()
    {
        foreach (VoxelColumnKey key in _columns)
        {
            if (!_roots.TryGetValue(key, out Transform column) || column.childCount > 0)
                continue;

            _roots.Remove(key);
            GeneratedMesh.Destroy(column.gameObject);
        }
    }

    private RoadSurfaceMesh.Geometry[] RoadSurfaces(List<ChunkTask> tasks, int start, VoxelMesh[] meshes, int offset, int count)
    {
        var surfaces = new RoadSurfaceMesh.Geometry[count];

        if (_roadSurface == null || _roadMaterial == null)
            return surfaces;

        using (WorldGenProbe.Measure(WorldGenStage.RoadSurface))
        {
            var paved = new List<int>(count);

            for (int i = 0; i < count; i++)
            {
                VoxelColumnKey key = tasks[start + i].Key;

                if (!meshes[offset + i].IsEmpty && _roadSurface.Touches(key, _voxels.ChunkMetres * (1 << key.Lod)))
                    paved.Add(i);
            }

            if (paved.Count > 0)
                Parallel.For(0, paved.Count, k => surfaces[paved[k]] = _roadSurface.Build(meshes[offset + paved[k]]));
        }

        return surfaces;
    }

    private void Spawn(VoxelMesh source, Transform parent, string name, VoxelColliderQueue colliders, RoadSurfaceMesh.Geometry road)
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

        colliders?.Add(holder, mesh);

        GeneratedMesh.Own(holder);

        RoadSurfaceBuilder.Build(road, _roadMaterial, holder.transform);

        _chunks.Add(holder.GetComponent<MeshFilter>());
    }

    private Transform NewColumn(VoxelColumnKey key)
    {
        var column = new GameObject($"Column {key}");

        column.transform.SetParent(transform, false);

        _roots[key] = column.transform;

        return column.transform;
    }

    private static float Lowest(HeightMap map)
    {
        float lowest = float.MaxValue;

        foreach (float height in map.Heights)
            lowest = Mathf.Min(lowest, height);

        return lowest * map.MaxHeight;
    }
}
