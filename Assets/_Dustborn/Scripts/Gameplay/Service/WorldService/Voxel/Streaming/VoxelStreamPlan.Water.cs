using System.Collections.Generic;
using UnityEngine;

public partial class VoxelStreamPlan
{
    private Dictionary<(int, int), int> _waterLimits;
    private Vector2 _waterLimitsViewer;

    public bool ProtectShore(float x, float z, int maximum, int padding = 0)
    {
        int column = Mathf.FloorToInt(x / ChunkMetres(0));
        int row = Mathf.FloorToInt(z / ChunkMetres(0));
        bool changed = false;

        for (int j = row - padding; j <= row + padding; j++)
            for (int i = column - padding; i <= column + padding; i++)
                if (!_shoreLimits.TryGetValue((i, j), out int previous) || maximum < previous)
                {
                    _shoreLimits[(i, j)] = maximum;
                    changed = true;
                }

        return changed;
    }

    private void RefineWater(Vector2 viewer, List<VoxelColumnKey> output)
    {
        var limits = new Dictionary<(int, int), int>(WaterLimits(viewer));

        foreach (KeyValuePair<(int, int), int> shore in _shoreLimits)
        {
            if (!limits.TryGetValue(shore.Key, out int previous) || shore.Value < previous)
                limits[shore.Key] = shore.Value;
        }

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

    private Dictionary<(int, int), int> WaterLimits(Vector2 viewer)
    {
        if (_waterLimits != null && viewer.x == _waterLimitsViewer.x && viewer.y == _waterLimitsViewer.y)
            return _waterLimits;

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

        _waterLimits = limits;
        _waterLimitsViewer = viewer;

        return limits;
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
}
