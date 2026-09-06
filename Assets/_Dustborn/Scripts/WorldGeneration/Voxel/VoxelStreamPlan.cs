using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct VoxelColumnKey : IEquatable<VoxelColumnKey>
{
    public const int TRIM_X = 1;
    public const int TRIM_Z = 2;

    public int X { get; }
    public int Z { get; }
    public int Lod { get; }

    public int Trim { get; }

    public VoxelColumnKey(int x, int z, int lod, int trim = 0)
    {
        X = x;
        Z = z;
        Lod = lod;
        Trim = trim;
    }

    public bool Equals(VoxelColumnKey other)
    {
        return X == other.X && Z == other.Z && Lod == other.Lod && Trim == other.Trim;
    }

    public override bool Equals(object other)
    {
        return other is VoxelColumnKey key && Equals(key);
    }

    public override int GetHashCode()
    {
        return (X * 73856093) ^ (Z * 19349663) ^ (Lod * 83492791) ^ (Trim * 2654435761u).GetHashCode();
    }

    public override string ToString()
    {
        return $"({X}, {Z}) lod {Lod}";
    }
}

public class VoxelStreamPlan
{
    private readonly VoxelConfig _voxels;
    private readonly int _lodCount;
    private readonly float _nearDistance;

    public VoxelStreamPlan(VoxelConfig voxels, int lodCount, float nearDistance)
    {
        _voxels = voxels;
        _lodCount = Mathf.Max(1, lodCount);
        _nearDistance = Mathf.Max(voxels.ChunkMetres, nearDistance);
    }

    public int LodCount => _lodCount;

    public float ChunkMetres(int lod)
    {
        return _voxels.ChunkMetres * (1 << lod);
    }

    public float VoxelSize(int lod)
    {
        return _voxels.VoxelSize * (1 << lod);
    }

    public float Distance(int lod)
    {
        return _nearDistance * (1 << lod);
    }

    public float ViewDistance => Distance(_lodCount - 1);

    public void Around(Vector2 viewer, List<VoxelColumnKey> output)
    {
        output.Clear();

        int innerMinX = 0, innerMaxX = -1, innerMinZ = 0, innerMaxZ = -1;

        for (int lod = 0; lod < _lodCount; lod++)
        {
            Rect(viewer, lod, out int minX, out int maxX, out int minZ, out int maxZ);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x >= innerMinX && x <= innerMaxX && z >= innerMinZ && z <= innerMaxZ)
                        continue;

                    output.Add(new VoxelColumnKey(x, z, lod, Trim(viewer, x, z, lod)));
                }
            }

            innerMinX = Halve(minX);
            innerMaxX = Halve(maxX + 1) - 1;
            innerMinZ = Halve(minZ);
            innerMaxZ = Halve(maxZ + 1) - 1;
        }
    }

    private int Trim(Vector2 viewer, int x, int z, int lod)
    {
        float size = ChunkMetres(lod);

        int trim = 0;

        if (Neighbour(viewer, new Vector2((x - 0.5f) * size, (z + 0.5f) * size), lod))
            trim |= VoxelColumnKey.TRIM_X;

        if (Neighbour(viewer, new Vector2((x + 0.5f) * size, (z - 0.5f) * size), lod))
            trim |= VoxelColumnKey.TRIM_Z;

        return trim;
    }

    private bool Neighbour(Vector2 viewer, Vector2 point, int lod)
    {
        int level = LodAt(viewer, point);

        return level >= 0 && level != lod;
    }

    public int LodAt(Vector2 viewer, Vector2 point)
    {
        for (int lod = 0; lod < _lodCount; lod++)
        {
            Rect(viewer, lod, out int minX, out int maxX, out int minZ, out int maxZ);

            float size = ChunkMetres(lod);

            int x = Mathf.FloorToInt(point.x / size);
            int z = Mathf.FloorToInt(point.y / size);

            if (x >= minX && x <= maxX && z >= minZ && z <= maxZ)
                return lod;
        }

        return -1;
    }

    private void Rect(Vector2 viewer, int lod, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        float size = ChunkMetres(lod);
        float reach = Distance(lod);

        minX = Floor(Mathf.FloorToInt((viewer.x - reach) / size));
        maxX = Ceil(Mathf.CeilToInt((viewer.x + reach) / size)) - 1;
        minZ = Floor(Mathf.FloorToInt((viewer.y - reach) / size));
        maxZ = Ceil(Mathf.CeilToInt((viewer.y + reach) / size)) - 1;
    }

    private static int Floor(int index)
    {
        return index - (((index % 2) + 2) % 2);
    }

    private static int Ceil(int index)
    {
        return index + (((index % 2) + 2) % 2);
    }

    private static int Halve(int index)
    {
        return index >= 0 ? index / 2 : (index - 1) / 2;
    }
}
