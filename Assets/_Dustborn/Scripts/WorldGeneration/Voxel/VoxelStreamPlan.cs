using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct VoxelColumnKey : IEquatable<VoxelColumnKey>
{
    public const int FACE_MIN_X = 1;
    public const int FACE_MAX_X = 2;
    public const int FACE_MIN_Z = 4;
    public const int FACE_MAX_Z = 8;
    public const int FACE_ALL = FACE_MIN_X | FACE_MAX_X | FACE_MIN_Z | FACE_MAX_Z;

    public const int MORPH_MIN_X = 1;
    public const int MORPH_MAX_X = 2;
    public const int MORPH_MIN_Z = 4;
    public const int MORPH_MAX_Z = 8;

    public int X { get; }
    public int Z { get; }
    public int Lod { get; }

    public int Seams { get; }
    public int Morph { get; }

    public VoxelColumnKey(int x, int z, int lod, int seams = 0, int morph = 0)
    {
        X = x;
        Z = z;
        Lod = lod;
        Seams = seams;
        Morph = morph;
    }

    public bool Equals(VoxelColumnKey other)
    {
        return X == other.X && Z == other.Z && Lod == other.Lod
            && Seams == other.Seams && Morph == other.Morph;
    }

    public override bool Equals(object other)
    {
        return other is VoxelColumnKey key && Equals(key);
    }

    public override int GetHashCode()
    {
        return (X * 73856093) ^ (Z * 19349663) ^ (Lod * 83492791)
            ^ (Seams * 2654435761u).GetHashCode() ^ (Morph * 40503u).GetHashCode();
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
    private readonly float _worldSize;

    public VoxelStreamPlan(VoxelConfig voxels, int lodCount, float nearDistance, float worldSize = 0f)
    {
        _voxels = voxels;
        _lodCount = Mathf.Max(1, lodCount);
        _nearDistance = Mathf.Max(voxels.ChunkMetres, nearDistance);
        _worldSize = worldSize;
    }

    public int LodCount => _lodCount;

    public bool CoversWorld => _worldSize > 0f;

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

    public float ViewDistance => CoversWorld ? _worldSize : Distance(_lodCount - 1);

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

                    output.Add(new VoxelColumnKey(x, z, lod, Seams(viewer, x, z, lod), Morph(viewer, x, z, lod)));
                }
            }

            innerMinX = Halve(minX);
            innerMaxX = Halve(maxX + 1) - 1;
            innerMinZ = Halve(minZ);
            innerMaxZ = Halve(maxZ + 1) - 1;
        }
    }

    private int Seams(Vector2 viewer, int x, int z, int lod)
    {
        float size = ChunkMetres(lod);

        int faces = 0;

        if (Neighbour(viewer, new Vector2((x - 0.5f) * size, (z + 0.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MIN_X;

        if (Neighbour(viewer, new Vector2((x + 1.5f) * size, (z + 0.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MAX_X;

        if (Neighbour(viewer, new Vector2((x + 0.5f) * size, (z - 0.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MIN_Z;

        if (Neighbour(viewer, new Vector2((x + 0.5f) * size, (z + 1.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MAX_Z;

        return faces;
    }

    private int Morph(Vector2 viewer, int x, int z, int lod)
    {
        float size = ChunkMetres(lod);

        int morph = 0;

        if (Coarser(viewer, new Vector2((x - 0.5f) * size, (z + 0.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MIN_X;

        if (Coarser(viewer, new Vector2((x + 1.5f) * size, (z + 0.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MAX_X;

        if (Coarser(viewer, new Vector2((x + 0.5f) * size, (z - 0.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MIN_Z;

        if (Coarser(viewer, new Vector2((x + 0.5f) * size, (z + 1.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MAX_Z;

        return morph;
    }

    private bool Coarser(Vector2 viewer, Vector2 point, int lod)
    {
        int level = LodAt(viewer, point);

        return level > lod;
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

        if (CoversWorld && lod == _lodCount - 1)
        {
            minX = 0;
            minZ = 0;
            maxX = Last(size);
            maxZ = Last(size);

            return;
        }

        float reach = Distance(lod);

        minX = Floor(Mathf.FloorToInt((viewer.x - reach) / size));
        maxX = Ceil(Mathf.CeilToInt((viewer.x + reach) / size)) - 1;
        minZ = Floor(Mathf.FloorToInt((viewer.y - reach) / size));
        maxZ = Ceil(Mathf.CeilToInt((viewer.y + reach) / size)) - 1;

        if (!CoversWorld)
            return;

        int last = Last(size);

        minX = Floor(Mathf.Clamp(minX, 0, last));
        maxX = Ceil(Mathf.Clamp(maxX, 0, last) + 1) - 1;
        minZ = Floor(Mathf.Clamp(minZ, 0, last));
        maxZ = Ceil(Mathf.Clamp(maxZ, 0, last) + 1) - 1;
    }

    private int Last(float size)
    {
        return Ceil(Mathf.CeilToInt(_worldSize / size)) - 1;
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
