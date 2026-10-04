using System.Collections.Generic;
using UnityEngine;

public sealed class VoxelSurfaceProbe
{
    private readonly VoxelDensitySampler _sampler;
    private readonly VoxelStreamPlan _plan;
    private readonly Vector2 _viewer;
    private readonly int _size;
    private readonly Dictionary<(int Lod, int X, int Z), VoxelColumnKey> _columns = new();

    public VoxelSurfaceProbe(VoxelDensitySampler sampler, VoxelStreamPlan plan, Vector2 viewer, int chunkSize)
    {
        _sampler = sampler;
        _plan = plan;
        _viewer = viewer;
        _size = chunkSize;

        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);

        foreach (VoxelColumnKey key in keys)
            _columns[(key.Lod, key.X, key.Z)] = key;
    }

    public int LodAt(float x, float z)
    {
        return Mathf.Max(0, _plan.LodAt(_viewer, new Vector2(x, z)));
    }

    public float VoxelAt(float x, float z)
    {
        return _plan.VoxelSize(LodAt(x, z));
    }

    public float Height(float x, float z)
    {
        int lod = LodAt(x, z);
        float size = _plan.ChunkMetres(lod);
        float voxel = _plan.VoxelSize(lod);

        int column = Mathf.FloorToInt(x / size);
        int row = Mathf.FloorToInt(z / size);

        int morph = _columns.TryGetValue((lod, column, row), out VoxelColumnKey key) ? key.Morph : 0;

        return _sampler.Height(x, z, voxel, morph, column * size, row * size, VoxelDensitySampler.MorphSpan(_size, voxel), voxel * 2f);
    }
}
