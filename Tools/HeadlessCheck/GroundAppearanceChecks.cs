using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

static class GroundAppearanceChecks
{
    public static void Run()
    {
        CheckStacking();
        CheckBorder();
        CheckPalettes();

        Console.WriteLine("Ground appearance checks passed: stacked grounds, biome cliffs, road priority, normalized weights, tile continuity, one border for ground and plants, dry biome palettes.");
    }

    private static void CheckStacking()
    {
        var config = new WorldGenerationConfig();
        Set(config, "WorldSize", 64);

        var baseLayer = new TerrainLayer { name = "base" };
        var patchLayer = new TerrainLayer { name = "patch" };
        var shelfLayer = new TerrainLayer { name = "shelf" };
        var cliffLayer = new TerrainLayer { name = "cliff" };
        var roadLayer = new TerrainLayer { name = "road" };

        Set(config, "RoadLayer", roadLayer);
        Set(config, "CliffLayer", new TerrainLayer { name = "shared cliff" });

        var biome = new BiomeDefinition();
        Set(biome, "CliffLayer", cliffLayer);
        biome.Ground.Add(Ground(baseLayer, 1f, 0f));
        biome.Ground.Add(Ground(patchLayer, 0.75f, 0f));
        biome.Ground.Add(Ground(shelfLayer, 1f, 18f));

        var map = new BiomeMap(8, 64);
        var field = new BiomeWeightField(map, 1, 0f);

        var roads = new List<Road> { new Road(new[] { new Vector2(52f, 60f), new Vector2(68f, 60f) }, 40f) };

        using var painter = new GroundSplatPainter(config, Database(biome), field, roads);

        var slopes = new float[64];
        slopes[0] = 65f;
        slopes[1] = 24f;

        using var weights = painter.BakeWorld(8, slopes, new float[64]);

        int count = painter.Layers.Length;

        float At(int pixel, TerrainLayer layer) => weights[pixel * count + Array.IndexOf(painter.Layers, layer)];

        Require(At(0, cliffLayer) > 0.999f, "A slope past CliffSlopeFull must show the biome's own cliff ground.");
        Require(Near(At(9, patchLayer), 0.75f) && Near(At(9, baseLayer), 0.25f), "A ground must hide the grounds below it by its opacity.");
        Require(At(1, shelfLayer) > 0.999f, "The last ground whose rules pass must win outright.");
        Require(At(63, roadLayer) > 0.999f, "The road must cover cliffs and biome ground.");

        for (int pixel = 0; pixel < 64; pixel++)
        {
            float sum = 0f;

            for (int layer = 0; layer < count; layer++)
            {
                float weight = weights[pixel * count + layer];
                Require(!float.IsNaN(weight) && weight >= 0f && weight <= 1f, "Ground weights must be finite and bounded.");
                sum += weight;
            }

            Require(Math.Abs(sum - 1f) < 1e-5f, "Each surface pixel must retain full coverage.");
        }

        var tileSlopes = new float[16];
        Array.Copy(slopes, 0, tileSlopes, 0, 4);

        using var tile = painter.BakeTile(8, 4, 0, 0, tileSlopes, new float[16]);

        for (int layer = 0; layer < count; layer++)
        {
            Require(Math.Abs(tile[5 * count + layer] - weights[9 * count + layer]) < 1e-5f, "Tile boundaries must not change surface weights.");
            Require(Math.Abs(tile[1 * count + layer] - weights[1 * count + layer]) < 1e-5f, "Tile boundaries must not change surface weights.");
        }
    }

    private static void CheckBorder()
    {
        var config = new WorldGenerationConfig();
        Set(config, "WorldSize", 512);
        Set(config, "BiomeBlendRadius", 48f);
        Set(config, "BiomeBorderWidth", 6f);
        Set(config, "BiomeBorderWarp", 0f);

        var left = new TerrainLayer { name = "left" };
        var right = new TerrainLayer { name = "right" };

        var a = new BiomeDefinition();
        a.Ground.Add(Ground(left, 1f, 0f));

        var b = new BiomeDefinition();
        b.Ground.Add(Ground(right, 1f, 0f));

        var map = new BiomeMap(64, 512);

        for (int y = 0; y < 64; y++)
            for (int x = 32; x < 64; x++)
                map.Cells[y * 64 + x] = 1;

        var field = new BiomeWeightField(map, 2, 48f);
        var straight = new DecorFilter(config, field, 2, null, 0, null, 0f);

        float blendWidth = Width(x => straight.Weight(new Vector2(x, 256f), 0));
        float surfaceWidth = Width(x => straight.SurfaceWeight(new Vector2(x, 256f), 0));

        Require(surfaceWidth < blendWidth * 0.2f, $"The painted border must be far narrower than the height blend: {surfaceWidth:0.0} m against {blendWidth:0.0} m.");
        Require(surfaceWidth > 2f && surfaceWidth < 18f, $"A 6 m border must come out a few metres wide, got {surfaceWidth:0.0} m.");

        Set(config, "BiomeBorderWarp", 24f);

        var winding = new DecorFilter(config, field, 2, null, 0, null, 0f);

        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (float z = 64f; z <= 448f; z += 16f)
        {
            float crossing = Crossing(x => winding.SurfaceWeight(new Vector2(x, z), 0));

            lowest = Mathf.Min(lowest, crossing);
            highest = Mathf.Max(highest, crossing);

            for (float x = 180f; x <= 332f; x += 3f)
            {
                var point = new Vector2(x, z);
                float sum = winding.SurfaceWeight(point, 0) + winding.SurfaceWeight(point, 1);

                Require(Math.Abs(sum - 1f) < 1e-3f, "Surface weights of all biomes must add up to one.");
            }
        }

        Require(highest - lowest > 8f, $"The border warp must move the border, it wandered only {highest - lowest:0.0} m.");

        using var painter = new GroundSplatPainter(config, Database(a, b), field, null);

        const int RESOLUTION = 256;

        using var weights = painter.BakeWorld(RESOLUTION, new float[RESOLUTION * RESOLUTION], new float[RESOLUTION * RESOLUTION]);

        int count = painter.Layers.Length;
        int index = Array.IndexOf(painter.Layers, left);
        float texel = 512f / RESOLUTION;
        float worst = 0f;

        for (int y = 16; y < RESOLUTION - 16; y += 7)
        {
            for (int x = 0; x < RESOLUTION; x++)
            {
                var point = new Vector2((x + 0.5f) * texel, (y + 0.5f) * texel);
                float painted = weights[(y * RESOLUTION + x) * count + index];

                worst = Mathf.Max(worst, Math.Abs(painted - winding.SurfaceWeight(point, 0)));
            }
        }

        Require(worst < 3e-3f, $"Ground paint and plant placement must agree on the border, they differ by {worst:0.0000}.");
    }

    private static void CheckPalettes()
    {
        foreach (BiomeType type in Enum.GetValues(typeof(BiomeType)))
        {
            var grass = new GrassLayer();
            grass.ApplyDefaults(type);

            if (type == BiomeType.PineForest)
                Require(grass.LeavesColor.g > grass.LeavesColor.r, "Forest defaults must remain green.");
            else if (type == BiomeType.Snow)
                Require(grass.SnowAmount > 0.5f, "Snow defaults must include frost.");
            else
                Require(grass.LeavesColor.r > grass.LeavesColor.g, "Dry biome defaults must not create green grass.");
        }
    }

    private static float Width(Func<float, float> weight)
    {
        float high = float.NaN;
        float low = float.NaN;

        for (float x = 96f; x <= 416f; x += 0.25f)
        {
            float value = weight(x);

            if (float.IsNaN(high) && value < 0.9f)
                high = x;

            if (float.IsNaN(low) && value < 0.1f)
                low = x;
        }

        return low - high;
    }

    private static float Crossing(Func<float, float> weight)
    {
        for (float x = 96f; x <= 416f; x += 0.5f)
        {
            if (weight(x) < 0.5f)
                return x;
        }

        return float.NaN;
    }

    private static GroundLayer Ground(TerrainLayer layer, float opacity, float minSlope)
    {
        var ground = new GroundLayer();

        Set(ground, "Layer", layer);
        Set(ground, "Opacity", opacity);
        Set(ground, "MinSlope", minSlope);

        return ground;
    }

    private static BiomeDatabase Database(params BiomeDefinition[] biomes)
    {
        var database = new BiomeDatabase();

        typeof(BiomeDatabase).GetField("_biomes", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(database, new List<BiomeDefinition>(biomes));

        return database;
    }

    private static bool Near(float value, float expected)
    {
        return Math.Abs(value - expected) < 1e-4f;
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void Require(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }
}
