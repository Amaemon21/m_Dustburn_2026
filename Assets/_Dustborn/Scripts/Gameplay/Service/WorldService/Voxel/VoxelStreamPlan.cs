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
    public const int MORPH_CORNERS = 256;

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
    public const int WATER_LOD_SLACK = 2;
    public const int RIVER_LOD_SLACK = 4;
    public const int CROSSING_LOD = 1;

    private readonly VoxelConfig _voxels;
    private readonly int _lodCount;
    private readonly float _nearDistance;
    private readonly float _worldSize;
    private readonly WaterMap _water;
    private Dictionary<(int, int), int> _adaptive;
    private Vector2 _adaptiveViewer;

    public VoxelStreamPlan(VoxelConfig voxels, int lodCount, float nearDistance, float worldSize = 0f, WaterMap water = null)
    {
        _voxels = voxels;
        _lodCount = Mathf.Max(1, lodCount);
        _nearDistance = Mathf.Max(voxels.ChunkMetres, nearDistance);
        _worldSize = worldSize;
        _water = water;
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
        _adaptive = null;
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

        if (_water != null)
            RefineWater(viewer, output);
    }

    private void RefineWater(Vector2 viewer, List<VoxelColumnKey> output)
    {
        var limits = new Dictionary<(int, int), int>();
        float size = ChunkMetres(0);

        void Protect(float x, float z, float radius, int slack)
        {
            int minX = Mathf.FloorToInt((x - radius) / size) - 2;
            int maxX = Mathf.FloorToInt((x + radius) / size) + 2;
            int minZ = Mathf.FloorToInt((z - radius) / size) - 2;
            int maxZ = Mathf.FloorToInt((z + radius) / size) + 2;
            int limit = Mathf.Max(CROSSING_LOD, LodAt(viewer, new Vector2(x, z)) - slack);

            for (int row = minZ; row <= maxZ; row++)
                for (int column = minX; column <= maxX; column++)
                    if (!limits.TryGetValue((column, row), out int previous) || limit < previous)
                        limits[(column, row)] = limit;
        }

        foreach (int cell in _water.DetailCells)
        {
            Vector2 center = _water.CellCenter(cell);
            Protect(center.x, center.y, _water.CellSize, WATER_LOD_SLACK);
        }

        foreach (RiverPath river in _water.Rivers)
            foreach (RiverPoint point in river.Points)
                Protect(point.Position.x, point.Position.y, point.Width * 0.5f, RIVER_LOD_SLACK);

        foreach (WaterCrossing crossing in _water.Crossings)
            Protect(crossing.Center.x, crossing.Center.y, crossing.Span * 0.5f + crossing.RoadWidth, _lodCount);

        var roots = new List<VoxelColumnKey>(output);
        _adaptiveViewer = viewer;

        for (int pass = 0; pass < _lodCount; pass++)
        {
            output.Clear();

            foreach (VoxelColumnKey root in roots)
                SplitWater(root, limits, output);

            _adaptive = new Dictionary<(int, int), int>();

            foreach (VoxelColumnKey key in output)
            {
                int span = 1 << key.Lod;

                for (int z = key.Z * span; z < (key.Z + 1) * span; z++)
                    for (int x = key.X * span; x < (key.X + 1) * span; x++)
                        _adaptive[(x, z)] = key.Lod;
            }

            bool changed = false;

            foreach (var pair in _adaptive)
            {
                int x = pair.Key.Item1, z = pair.Key.Item2;
                int limit = pair.Value + 1;
                Balance(x - 1, z, limit);
                Balance(x + 1, z, limit);
                Balance(x, z - 1, limit);
                Balance(x, z + 1, limit);
                Balance(x - 1, z - 1, limit);
                Balance(x - 1, z + 1, limit);
                Balance(x + 1, z - 1, limit);
                Balance(x + 1, z + 1, limit);
            }

            void Balance(int x, int z, int limit)
            {
                if (!_adaptive.TryGetValue((x, z), out int lod) || lod <= limit)
                    return;

                if (!limits.TryGetValue((x, z), out int previous) || limit < previous)
                {
                    limits[(x, z)] = limit;
                    changed = true;
                }
            }

            if (!changed)
                break;
        }

        for (int i = 0; i < output.Count; i++)
        {
            VoxelColumnKey key = output[i];
            output[i] = new VoxelColumnKey(key.X, key.Z, key.Lod, Seams(viewer, key.X, key.Z, key.Lod), Morph(viewer, key.X, key.Z, key.Lod));
        }
    }

    private static void SplitWater(VoxelColumnKey key, Dictionary<(int, int), int> limits, List<VoxelColumnKey> output)
    {
        int span = 1 << key.Lod;
        bool split = false;

        for (int z = key.Z * span; z < (key.Z + 1) * span && !split; z++)
            for (int x = key.X * span; x < (key.X + 1) * span && !split; x++)
                split = limits.TryGetValue((x, z), out int limit) && limit < key.Lod;

        if (!split || key.Lod == 0)
        {
            output.Add(key);
            return;
        }

        for (int z = 0; z < 2; z++)
            for (int x = 0; x < 2; x++)
                SplitWater(new VoxelColumnKey(key.X * 2 + x, key.Z * 2 + z, key.Lod - 1), limits, output);
    }

    private int Seams(Vector2 viewer, int x, int z, int lod)
    {
        float size = ChunkMetres(lod);

        int faces = 0;

        if (Neighbour(viewer, new Vector2(x * size - 0.01f, (z + 0.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MIN_X;

        if (Neighbour(viewer, new Vector2((x + 1) * size + 0.01f, (z + 0.5f) * size), lod))
            faces |= VoxelColumnKey.FACE_MAX_X;

        if (Neighbour(viewer, new Vector2((x + 0.5f) * size, z * size - 0.01f), lod))
            faces |= VoxelColumnKey.FACE_MIN_Z;

        if (Neighbour(viewer, new Vector2((x + 0.5f) * size, (z + 1) * size + 0.01f), lod))
            faces |= VoxelColumnKey.FACE_MAX_Z;

        return faces;
    }

    private int Morph(Vector2 viewer, int x, int z, int lod)
    {
        float size = ChunkMetres(lod);
        int morph = 0;

        if (_adaptive != null)
        {
            int corners = 0;

            for (int corner = 0; corner < 4; corner++)
            {
                float cx = (x + (corner & 1)) * size;
                float cz = (z + (corner >> 1)) * size;

                for (int dz = -1; dz <= 1; dz += 2)
                    for (int dx = -1; dx <= 1; dx += 2)
                        if (LodAt(viewer, new Vector2(cx + dx * 0.01f, cz + dz * 0.01f)) > lod)
                            corners |= 1 << corner;
            }

            morph = corners == 0 ? 0 : (corners << 4) | VoxelColumnKey.MORPH_CORNERS;
        }

        if (Coarser(viewer, new Vector2(x * size - 0.01f, (z + 0.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MIN_X;

        if (Coarser(viewer, new Vector2((x + 1) * size + 0.01f, (z + 0.5f) * size), lod))
            morph |= VoxelColumnKey.MORPH_MAX_X;

        if (Coarser(viewer, new Vector2((x + 0.5f) * size, z * size - 0.01f), lod))
            morph |= VoxelColumnKey.MORPH_MIN_Z;

        if (Coarser(viewer, new Vector2((x + 0.5f) * size, (z + 1) * size + 0.01f), lod))
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
        if (_adaptive != null && (viewer - _adaptiveViewer).sqrMagnitude < 1e-8f)
        {
            int x = Mathf.FloorToInt(point.x / ChunkMetres(0));
            int z = Mathf.FloorToInt(point.y / ChunkMetres(0));
            return _adaptive.TryGetValue((x, z), out int level) ? level : -1;
        }

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
