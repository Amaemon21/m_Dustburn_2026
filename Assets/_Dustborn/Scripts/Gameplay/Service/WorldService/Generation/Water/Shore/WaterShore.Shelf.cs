using System.Threading.Tasks;
using UnityEngine;

public static partial class WaterShore
{
    private static void Shelf(WaterMap water, HeightMap map, StandingWaterRegions regions)
    {
        int resolution = map.Resolution;
        float texel = (float)map.WorldSize / (resolution - 1);
        float maxHeight = map.MaxHeight;
        float[] heights = map.Heights;
        float near = water.CellSize * SHELF_FADE_END + water.CellSize;
        int cells = water.Resolution;
        var rows = new bool[cells];

        for (int cell = 0; cell < water.ShoreDistance.Length; cell++)
            rows[cell / cells] |= water.ShoreDistance[cell] <= near;

        Parallel.For(0, resolution, row =>
        {
            float z = row * texel;
            int cellRow = Mathf.Clamp((int)(z / water.CellSize), 0, cells - 1);

            if (!rows[cellRow])
                return;

            for (int column = 0; column < resolution; column++)
            {
                float x = column * texel;
                int cellColumn = Mathf.Clamp((int)(x / water.CellSize), 0, cells - 1);
                int cell = cellRow * cells + cellColumn;

                if (!(water.ShoreDistance[cell] <= near))
                {
                    column = Mathf.Max(column, Mathf.CeilToInt((cellColumn + 1) * water.CellSize / texel) - 2);
                    continue;
                }

                int index = row * resolution + column;
                float ground = heights[index] * maxHeight;
                int node = regions.NearestNode(x, z);

                if (regions.IsSunk(node))
                {
                    float level = water.LevelOf(regions.Owner(node));

                    if (ground > level - SUNK_DEPTH)
                        heights[index] = (level - SUNK_DEPTH) / maxHeight;

                    continue;
                }

                if (!Target(water, regions, x, z, ground, out float surface, out bool covered, out float reachWeight))
                    continue;

                float difference = ground - surface;
                float magnitude = Mathf.Abs(difference);

                if (magnitude >= SHELF)
                    continue;

                if (!covered && difference < 0f && water.TryRiver(x, z, out _))
                    continue;

                float steep = SHELF * Mathf.Sqrt(magnitude / SHELF);

                float weight = reachWeight * (1f - Smooth(water.CellSize * SHELF_FADE_START, water.CellSize * SHELF_FADE_END, SmoothDistance(water, x, z)));
                float shelved = surface + (difference < 0f && covered ? -steep : Mathf.Max(steep, DRY_LIFT));

                heights[index] = Mathf.Lerp(ground, shelved, weight) / maxHeight;
            }
        });
    }

    private static bool Target(WaterMap water, StandingWaterRegions regions, float x, float z, float ground, out float surface, out bool covered, out float weight)
    {
        weight = 1f;

        if (water.Covers(x, z, out short owner))
        {
            surface = water.LevelOf(owner);
            covered = true;
            return true;
        }

        covered = false;
        bool drowned = water.SeaLevel > 0f && ground < water.SeaLevel;
        float reach = Mathf.Max(DAM_REACH, 2f * water.NodeStep);

        if (!drowned && regions.NearestBody(x, z, reach, out short body, out float distance))
        {
            surface = water.LevelOf(body);

            if (Mathf.Abs(ground - surface) < SHELF)
            {
                weight = 1f - Smooth(0.5f * reach, reach, distance);
                return true;
            }
        }

        surface = water.SeaLevel;
        return water.SeaLevel > 0f && Mathf.Abs(ground - water.SeaLevel) < SHELF;
    }

    private static float SmoothDistance(WaterMap water, float x, float z)
    {
        float cell = water.CellSize;
        int last = water.Resolution - 1;
        float u = Mathf.Clamp(x / cell - 0.5f, 0f, last);
        float v = Mathf.Clamp(z / cell - 0.5f, 0f, last);
        int column = Mathf.Min((int)u, last - 1);
        int row = Mathf.Min((int)v, last - 1);
        int origin = row * water.Resolution + column;
        float[] distance = water.ShoreDistance;

        float a = distance[origin], b = distance[origin + 1];
        float c = distance[origin + water.Resolution], d = distance[origin + water.Resolution + 1];

        if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsInfinity(c) || float.IsInfinity(d))
            return Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) + cell;

        return Mathf.Lerp(Mathf.Lerp(a, b, u - column), Mathf.Lerp(c, d, u - column), v - row);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / (to - from));

        return t * t * (3f - 2f * t);
    }
}
