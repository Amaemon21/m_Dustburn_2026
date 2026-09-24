using System;
using UnityEngine;

static partial class WaterStampChecks
{
    private const int VALLEY_WORLD = 4096;
    private const float VALLEY_MAX_HEIGHT = 384f;
    private const float HANG_MARGIN = 0.75f;
    private const int UNIT_WORLD = 2048;

    public static void RunValleys(WorldGenerationConfig source, string[] args)
    {
        int[] seeds = { source.Seed, 1337, 20240917, -5550123 };

        WaterAudit.Result legacy = WaterChecks.Run(ValleyConfig(source, source.Seed), false, VALLEY_WORLD, Valleys, "Вода без штампов в долинах (запасной режим)", float.PositiveInfinity);
        float limit = Mathf.Max(1.5f, legacy.WorstHang + HANG_MARGIN);

        Console.WriteLine($"  запасной режим на этой местности: висит {legacy.Area(legacy.Hanging):0} м², до {legacy.WorstHang:0.00} м — порог для штампов {limit:0.00} м");

        foreach (int seed in seeds)
            WaterChecks.Run(ValleyConfig(source, seed), true, VALLEY_WORLD, Valleys, "Вода со штампами в долинах", limit);

        WorldGenerationConfig unit = WaterChecks.Config(source, UNIT_WORLD);
        StampLoader.Set(unit, "Seed", 1337);
        StampLoader.Set(unit, "SeaLevel", 50f);
        StampLoader.Set(unit.Water, "RiverStartArea", 0.15f);
        WaterChecks.Run(unit, true, UNIT_WORLD, UnitValley, "Вода со штампами на мире из Unity-теста", limit);
    }

    private static WorldGenerationConfig ValleyConfig(WorldGenerationConfig source, int seed)
    {
        WorldGenerationConfig config = WaterChecks.Config(source, VALLEY_WORLD);

        StampLoader.Set(config, "Seed", seed);
        StampLoader.Set(config.Water, "RiverStartArea", 0.15f);
        StampLoader.Set(config.Water, "MinLakeArea", 15000f);
        StampLoader.Set(config.Water, "LakeSpacing", 400f);

        return config;
    }

    private static HeightMap Valleys()
    {
        var map = new HeightMap(VALLEY_WORLD + 1, VALLEY_WORLD, VALLEY_MAX_HEIGHT);
        float world = VALLEY_WORLD;
        var tributaryStart = new Vector2(3100f, 3950f);
        var tributaryEnd = new Vector2(2300f, 2250f);
        var sideStart = new Vector2(3300f, 250f);
        var sideEnd = new Vector2(2600f, 1750f);

        System.Threading.Tasks.Parallel.For(0, VALLEY_WORLD + 1, z =>
        {
            for (int x = 0; x <= VALLEY_WORLD; x++)
            {
                float u = x / world;
                float plain = 40f + 150f * Mathf.Pow(u, 1.15f);
                float axis = 2048f + 260f * Mathf.Sin(x / 950f) + 90f * Mathf.Sin(x / 310f + 1.3f);
                float trunk = 20f * Mathf.Exp(-Square((z - axis) / 280f));
                float tributary = 13f * Mathf.Exp(-Square(Distance(new Vector2(x, z), tributaryStart, tributaryEnd) / 210f));
                float side = 11f * Mathf.Exp(-Square(Distance(new Vector2(x, z), sideStart, sideEnd) / 190f));
                float lake = 16f * Bump(x, z, 3450f, 2048f + 260f * Mathf.Sin(3450f / 950f) + 90f * Mathf.Sin(3450f / 310f + 1.3f), 240f);
                float ponds = 6f * Bump(x, z, 1350f, 700f, 70f) + 5f * Bump(x, z, 2900f, 3300f, 60f) + 7f * Bump(x, z, 900f, 3500f, 90f);
                float ridges = 9f * Mathf.Abs(Mathf.Sin(z / 520f)) * (1f - Mathf.Exp(-Square((z - axis) / 600f)));
                float noise = 1.4f * Mathf.Sin(x * 0.013f + Mathf.Sin(z * 0.007f) * 2f) + 0.9f * Mathf.Sin(z * 0.019f - x * 0.004f);

                float height = plain + ridges + noise - trunk - Mathf.Max(tributary, side) - lake - ponds;
                map.Heights[z * (VALLEY_WORLD + 1) + x] = Mathf.Max(1f, height) / VALLEY_MAX_HEIGHT;
            }
        });

        return map;
    }

    private static HeightMap UnitValley()
    {
        var map = new HeightMap(UNIT_WORLD + 1, UNIT_WORLD, VALLEY_MAX_HEIGHT);

        for (int z = 0; z <= UNIT_WORLD; z++)
        {
            for (int x = 0; x <= UNIT_WORLD; x++)
            {
                float axis = 1024f + 150f * Mathf.Sin(x / 400f);
                float valley = 14f * Mathf.Exp(-Mathf.Pow((z - axis) / 180f, 2f));
                float pond = Mathf.Max(0f, 1f - ((x - 1400f) * (x - 1400f) + (z - 380f) * (z - 380f)) / 8100f);
                float height = 40f + 110f * x / UNIT_WORLD - valley - 7f * pond * pond + 1.5f * Mathf.Sin(x * 0.011f + z * 0.007f);

                map.Heights[z * (UNIT_WORLD + 1) + x] = height / VALLEY_MAX_HEIGHT;
            }
        }

        return map;
    }

    private static float Square(float value)
    {
        return value * value;
    }

    private static float Bump(float x, float z, float cx, float cz, float radius)
    {
        float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (radius * radius);

        return Mathf.Max(0f, 1f - d2) * Mathf.Max(0f, 1f - d2);
    }

    private static float Distance(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 axis = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, axis) / axis.sqrMagnitude);

        return Vector2.Distance(point, a + axis * t);
    }
}
