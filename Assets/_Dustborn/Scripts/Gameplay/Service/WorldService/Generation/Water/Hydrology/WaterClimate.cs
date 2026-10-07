using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed class WaterClimate
{
    public const float DRY_MARGIN = 48f;
    public const float SNOW_BORDER = 48f;
    public const float SEA_MARGIN = 1.5f;
    public const float LIFT_RELIEF = 12f;
    public const float LIFT_BLEND = 64f;

    public const float MAIN_MARGIN = 96f;

    private readonly bool[] _dry;
    private readonly bool[] _frozen;
    private readonly bool[] _main;
    private readonly float[] _abundance;
    private readonly float[] _lift;
    private readonly int _resolution;
    private readonly float _cellSize;

    private WaterClimate(bool[] dry, bool[] frozen, bool[] main, float[] abundance, float[] lift, int resolution, float cellSize)
    {
        _dry = dry;
        _frozen = frozen;
        _main = main;
        _abundance = abundance;
        _lift = lift;
        _resolution = resolution;
        _cellSize = cellSize;
    }

    public static WaterClimate From(BiomeMap map, BiomeDatabase database)
    {
        if (map == null || database == null)
            return null;

        int count = database.Count;
        var dryBiome = new bool[256];
        var frozenBiome = new bool[256];
        var mainBiome = new bool[256];
        var abundanceBiome = new float[256];

        for (int i = 0; i < count; i++)
        {
            BiomeDefinition biome = database.Get(i);
            dryBiome[i] = biome.Water == BiomeWater.None;
            frozenBiome[i] = biome.Water == BiomeWater.Frozen;
            mainBiome[i] = biome.Water == BiomeWater.Liquid && biome.MainRiver;
            abundanceBiome[i] = dryBiome[i] ? 0f : biome.WaterAbundance;
        }

        return From(map, dryBiome, frozenBiome, mainBiome, abundanceBiome);
    }

    public static WaterClimate From(BiomeMap map, bool[] dryBiome, bool[] frozenBiome, bool[] mainBiome = null, float[] abundanceBiome = null)
    {
        int resolution = map.Resolution;
        byte[] cells = map.Cells;
        var desert = new bool[cells.Length];
        var outside = new bool[cells.Length];
        var frozen = new bool[cells.Length];
        var abundance = new float[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            byte biome = cells[i];
            desert[i] = dryBiome[biome];
            frozen[i] = frozenBiome[biome];
            outside[i] = mainBiome == null ? desert[i] || frozen[i] : !mainBiome[biome];
            abundance[i] = desert[i] ? 0f : abundanceBiome == null ? 1f : abundanceBiome[biome];
        }

        bool[] dry = Dilate(desert, resolution, Mathf.CeilToInt(DRY_MARGIN / map.CellSize));
        bool[] near = Dilate(outside, resolution, Mathf.CeilToInt(MAIN_MARGIN / map.CellSize));
        var main = new bool[cells.Length];
        var lift = new float[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            main[i] = !near[i];
            lift[i] = dry[i] ? 1f : 0f;
        }

        Blur(lift, resolution, Mathf.CeilToInt(LIFT_BLEND / map.CellSize));

        return new WaterClimate(dry, frozen, main, abundance, lift, resolution, map.CellSize);
    }

    public bool Dry(float x, float z)
    {
        return _dry[Cell(x, z)];
    }

    public float Runoff(float x, float z, float dry)
    {
        return _dry[Cell(x, z)] ? dry : 1f;
    }

    public bool Main(float x, float z)
    {
        return _main[Cell(x, z)];
    }

    public float Abundance(float x, float z)
    {
        return _abundance[Cell(x, z)];
    }

    public bool Frozen(float x, float z)
    {
        return _frozen[Cell(x, z)];
    }

    public void LiftAboveSea(HeightMap map, float seaLevel)
    {
        if (seaLevel <= 0f)
            return;

        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        float target = (seaLevel + SEA_MARGIN) / map.MaxHeight;
        float top = (seaLevel + SEA_MARGIN + LIFT_RELIEF) / map.MaxHeight;
        float[] heights = map.Heights;
        float low = Lowest(heights, resolution, step, top);

        if (low >= target)
            return;

        float scale = (top - target) / (top - low);

        Parallel.For(0, resolution, row =>
        {
            float z = row * step;

            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;
                float height = heights[index];

                if (height >= top)
                    continue;

                float weight = Lift(column * step, z);

                if (weight > 0f)
                    heights[index] = height + (target + (height - low) * scale - height) * weight;
            }
        });
    }

    private float Lowest(float[] heights, int resolution, float step, float top)
    {
        var lowest = new float[resolution];

        Parallel.For(0, resolution, row =>
        {
            float low = float.MaxValue;
            float z = row * step;

            for (int column = 0; column < resolution; column++)
            {
                float height = heights[row * resolution + column];

                if (height < low && height < top && Lift(column * step, z) > 0f)
                    low = height;
            }

            lowest[row] = low;
        });

        float result = float.MaxValue;

        foreach (float low in lowest)
            result = Mathf.Min(result, low);

        return result;
    }

    private float Lift(float x, float z)
    {
        float u = Mathf.Clamp(x / _cellSize - 0.5f, 0f, _resolution - 1);
        float v = Mathf.Clamp(z / _cellSize - 0.5f, 0f, _resolution - 1);
        int x0 = (int)u, z0 = (int)v;
        int x1 = Math.Min(x0 + 1, _resolution - 1), z1 = Math.Min(z0 + 1, _resolution - 1);
        float fx = u - x0, fz = v - z0;

        float bottom = Mathf.Lerp(_lift[z0 * _resolution + x0], _lift[z0 * _resolution + x1], fx);
        float top = Mathf.Lerp(_lift[z1 * _resolution + x0], _lift[z1 * _resolution + x1], fx);

        return Mathf.SmoothStep(0f, 1f, Mathf.Lerp(bottom, top, fz));
    }

    private int Cell(float x, float z)
    {
        int column = Math.Clamp((int)Math.Floor(x / _cellSize), 0, _resolution - 1);
        int row = Math.Clamp((int)Math.Floor(z / _cellSize), 0, _resolution - 1);

        return row * _resolution + column;
    }

    private static void Blur(float[] values, int resolution, int radius)
    {
        var temp = new float[values.Length];
        float scale = 1f / (2 * radius + 1);

        Parallel.For(0, resolution, y =>
        {
            int row = y * resolution;
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += values[row + Math.Clamp(k, 0, resolution - 1)];

            for (int x = 0; x < resolution; x++)
            {
                temp[row + x] = sum * scale;
                sum += values[row + Math.Min(x + radius + 1, resolution - 1)] - values[row + Math.Max(x - radius, 0)];
            }
        });

        Parallel.For(0, resolution, x =>
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += temp[Math.Clamp(k, 0, resolution - 1) * resolution + x];

            for (int y = 0; y < resolution; y++)
            {
                values[y * resolution + x] = sum * scale;
                sum += temp[Math.Min(y + radius + 1, resolution - 1) * resolution + x] - temp[Math.Max(y - radius, 0) * resolution + x];
            }
        });
    }

    private static bool[] Dilate(bool[] source, int resolution, int reach)
    {
        if (reach <= 0)
            return source;

        var rows = new bool[source.Length];
        var result = new bool[source.Length];

        for (int y = 0; y < resolution; y++)
            Spread(source, rows, y * resolution, 1, resolution, reach);

        for (int x = 0; x < resolution; x++)
            Spread(rows, result, x, resolution, resolution, reach);

        return result;
    }

    private static void Spread(bool[] source, bool[] target, int start, int stride, int length, int reach)
    {
        int last = -reach - 1;

        for (int i = 0; i < length; i++)
        {
            if (source[start + i * stride])
                last = i;

            if (i - last <= reach)
                target[start + i * stride] = true;
        }

        last = length + reach + 1;

        for (int i = length - 1; i >= 0; i--)
        {
            if (source[start + i * stride])
                last = i;

            if (last - i <= reach)
                target[start + i * stride] = true;
        }
    }
}
