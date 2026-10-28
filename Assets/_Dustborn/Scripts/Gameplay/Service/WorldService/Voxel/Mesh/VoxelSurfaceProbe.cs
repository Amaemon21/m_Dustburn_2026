using System;
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

    public readonly struct Cell : IEquatable<Cell>
    {
        public int Lod { get; }
        public int Column { get; }
        public int Row { get; }
        public bool Found { get; }
        public VoxelColumnKey Key { get; }

        public Cell(int lod, int column, int row, bool found, VoxelColumnKey key)
        {
            Lod = lod;
            Column = column;
            Row = row;
            Found = found;
            Key = key;
        }

        public bool Equals(Cell other)
        {
            return Lod == other.Lod && Column == other.Column && Row == other.Row && Found == other.Found && (!Found || Key.Morph == other.Key.Morph);
        }

        public override bool Equals(object obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Lod, Column, Row, Found, Found ? Key.Morph : 0);
    }

    public Cell Locate(float x, float z)
    {
        int lod = LodAt(x, z);
        float size = _plan.ChunkMetres(lod);
        int column = Mathf.FloorToInt(x / size);
        int row = Mathf.FloorToInt(z / size);
        bool found = _columns.TryGetValue((lod, column, row), out VoxelColumnKey key);

        return new Cell(lod, column, row, found, key);
    }

    public float MeshHeight(float x, float z)
    {
        return MeshHeight(Locate(x, z), x, z);
    }

    public float MeshHeight(Cell cell, float x, float z)
    {
        return cell.Found ? MeshHeight(cell.Key, x, z) : Height(x, z);
    }

    public float FineHeight(float x, float z)
    {
        float size = _plan.ChunkMetres(0);
        var key = new VoxelColumnKey(Mathf.FloorToInt(x / size), Mathf.FloorToInt(z / size), 0);
        return MeshHeight(key, x, z);
    }

    private float MeshHeight(VoxelColumnKey key, float x, float z)
    {
        float voxel = _plan.VoxelSize(key.Lod);
        float size = _plan.ChunkMetres(key.Lod);
        int sx = Mathf.Clamp(Mathf.FloorToInt((x - key.X * size) / voxel + 0.5f), 0, _size - 1);
        int sz = Mathf.Clamp(Mathf.FloorToInt((z - key.Z * size) / voxel + 0.5f), 0, _size - 1);

        for (int radius = 0; radius <= 2; radius++)
        {
            for (int j = Mathf.Max(0, sz - radius); j <= Mathf.Min(_size - 1, sz + radius); j++)
            {
                for (int i = Mathf.Max(0, sx - radius); i <= Mathf.Min(_size - 1, sx + radius); i++)
                {
                    if (radius > 0 && Mathf.Abs(i - sx) != radius && Mathf.Abs(j - sz) != radius)
                        continue;

                    Vector3 a = MeshVertex(key, i, j), b = MeshVertex(key, i, j + 1);
                    Vector3 c = MeshVertex(key, i + 1, j + 1), d = MeshVertex(key, i + 1, j);

                    if (TriangleHeight(x, z, a, b, c, out float height) || TriangleHeight(x, z, a, c, d, out height))
                        return height;
                }
            }
        }

        return Height(x, z);
    }

    private Vector3 MeshVertex(VoxelColumnKey key, int x, int z)
    {
        return _sampler.MeshVertex(key.X, key.Z, _size, _plan.VoxelSize(key.Lod), key.Morph, x, z);
    }

    private static bool TriangleHeight(float x, float z, Vector3 a, Vector3 b, Vector3 c, out float height)
    {
        height = 0f;
        float area = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);

        if (Mathf.Abs(area) < 1e-6f)
            return false;

        float u = ((z - a.z) * (c.x - a.x) - (x - a.x) * (c.z - a.z)) / area;
        float v = ((b.z - a.z) * (x - a.x) - (b.x - a.x) * (z - a.z)) / area;

        if (u < -1e-5f || v < -1e-5f || u + v > 1.00001f)
            return false;

        height = a.y + u * (b.y - a.y) + v * (c.y - a.y);
        return true;
    }
}
