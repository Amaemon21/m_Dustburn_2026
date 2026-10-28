using System.Collections.Generic;
using UnityEngine;

public partial class VoxelStreamPlan
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
    private readonly Dictionary<(int, int), int> _shoreLimits = new();

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
