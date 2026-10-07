using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static partial class CoastShaper
{
    public const float FIELD_STEP = 8f;
    public const float EDGE_SHARE = 0.12f;
    public const float CORNER_SHARE = 0.35f;
    public const float INLAND_MARGIN = 0.5f;
    public const float MAX_WORLD_SHARE = 0.12f;
    private const float INLET_MAX_LENGTH = 320f;
    private const float INLET_MAX_CUT = 8f;
    private const float INLET_CUT_WEIGHT = 0.5f;
    private const float INLET_DEPTH = 2f;
    private const float INLET_HALF_WIDTH = 14f;
    private const float INLET_BANK = 24f;
    private const float DIAGONAL = 1.41421356f;
    private const float WAVE_PERIOD = 1.3f;
    public const int WAVE_OCTAVES = 3;
    public const float WAVE_LACUNARITY = 2.1f;
    public const float WAVE_PERSISTENCE = 0.5f;
    private const int STREAM = 0x0C0A57;

    public static bool Active(WorldGenerationConfig config)
    {
        WaterGenerationSettings water = config.Water;

        return config.SeaLevel > 0f && water != null && water.Enabled && water.Coast;
    }

    public static float Floor(WorldGenerationConfig config)
    {
        return config.SeaLevel - config.Water.CoastDepth;
    }

    public static float Width(WorldGenerationConfig config)
    {
        return Mathf.Min(config.Water.CoastWidth, config.WorldSize * MAX_WORLD_SHARE);
    }

    private static float Weight(WorldGenerationConfig config, float x, float z, float wander)
    {
        WaterGenerationSettings water = config.Water;
        float width = Width(config);
        float border = Border(x, z, config.WorldSize, CORNER_SHARE * width);
        float shifted = border + width * water.CoastVariation * wander;

        return Mathf.SmoothStep(0f, 1f, shifted / width) * Mathf.SmoothStep(0f, 1f, border / (EDGE_SHARE * width));
    }

    public static float Border(float x, float z, float world, float corner)
    {
        float west = x, east = world - x, south = z, north = world - z;
        float nearest = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));

        if (corner <= 0f)
            return Mathf.Max(0f, nearest);

        float sum = Mathf.Exp((nearest - west) / corner) + Mathf.Exp((nearest - east) / corner) + Mathf.Exp((nearest - south) / corner) + Mathf.Exp((nearest - north) / corner);

        return Mathf.Max(0f, nearest - corner * Mathf.Log(sum));
    }

    public static void Shape(HeightMap map, WorldGenerationConfig config)
    {
        if (!Active(config))
            return;

        int fieldSize = Mathf.CeilToInt(config.WorldSize / FIELD_STEP) + 1;
        float fieldStep = config.WorldSize / (float)(fieldSize - 1);
        float[] field = Wander(config, fieldSize, fieldStep);

        Parallel.For(0, fieldSize, row =>
        {
            for (int column = 0; column < fieldSize; column++)
            {
                int node = row * fieldSize + column;
                field[node] = Weight(config, column * fieldStep, row * fieldStep, field[node]);
            }
        });

        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        float floor = Floor(config) / map.MaxHeight;
        float reach = Width(config) * (1f + config.Water.CoastVariation) + fieldStep;
        float[] heights = map.Heights;

        Parallel.For(0, resolution, row =>
        {
            float z = row * step;

            for (int column = 0; column < resolution; column++)
            {
                float x = column * step;

                if (Mathf.Min(Mathf.Min(x, map.WorldSize - x), Mathf.Min(z, map.WorldSize - z)) > reach)
                    continue;

                int index = row * resolution + column;
                float height = heights[index];

                if (height <= floor)
                    continue;

                heights[index] = floor + (height - floor) * Bilinear(field, fieldSize, x / fieldStep, z / fieldStep);
            }
        });

        int lifted = LiftInland(map, config.SeaLevel, INLAND_MARGIN, out int inlets);
        float cellArea = map.WorldSize / (float)(map.Resolution - 1);
        Debug.Log($"Coast: {inlets} inland pockets below the sea opened to the ocean, {lifted * cellArea * cellArea / 1e6f:0.000} km2 of the rest lifted");
    }

    private static float[] Wander(WorldGenerationConfig config, int size, float step)
    {
        var wander = new NativeArray<float>(size * size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            new CoastWaveJob
            {
                Wander = wander,
                Size = size,
                Step = step,
                Period = Width(config) * WAVE_PERIOD,
                Offset = FractalNoise.Offset(config.Seed, STREAM),
                Axis = FractalNoise.Axis(config.Seed, STREAM)
            }.Schedule(size, 1).Complete();

            var field = new float[size * size];
            wander.CopyTo(field);
            return field;
        }
        finally
        {
            wander.Dispose();
        }
    }

    private static float Bilinear(float[] field, int size, float u, float v)
    {
        u = Mathf.Clamp(u, 0f, size - 1);
        v = Mathf.Clamp(v, 0f, size - 1);
        int x0 = Math.Min((int)u, size - 2), z0 = Math.Min((int)v, size - 2);
        float fx = u - x0, fz = v - z0;
        float bottom = Mathf.Lerp(field[z0 * size + x0], field[z0 * size + x0 + 1], fx);
        float top = Mathf.Lerp(field[(z0 + 1) * size + x0], field[(z0 + 1) * size + x0 + 1], fx);

        return Mathf.Lerp(bottom, top, fz);
    }
}
