using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

static class StampLoader
{
    private const string PACK = "../../Assets/_Dustborn/Content/TerrainStamps";

    public static TerrainStampDatabase Load()
    {
        string manifest = $"{PACK}/terrain_height_stamps/manifest.json";

        if (!File.Exists(manifest))
            return null;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
        JsonElement root = document.RootElement;
        int resolution = root.GetProperty("resolution")[0].GetInt32();
        var definitions = new List<TerrainStampDefinition>();

        foreach (JsonElement stamp in root.GetProperty("stamps").EnumerateArray())
        {
            string name = stamp.GetProperty("name").GetString();
            string data = $"{PACK}/TerrainStampData/{name}.bytes";

            if (!File.Exists(data))
                data = $"{PACK}/terrain_height_stamps/{stamp.GetProperty("heightmap_raw16").GetString()}";

            JsonElement footprint = stamp.GetProperty("suggested_footprint_m");
            TerrainStampCategory category = TerrainStampCategories.FromName(name);

            definitions.Add(new TerrainStampDefinition(name, new TextAsset(File.ReadAllBytes(data)) { name = name },
                stamp.GetProperty("operation").GetString() == "subtract" ? TerrainStampOperation.Subtract : TerrainStampOperation.Add,
                resolution, new Vector2(footprint[0].GetSingle(), footprint[1].GetSingle()),
                stamp.GetProperty("suggested_amplitude_m").GetSingle(), category, TerrainStampCategories.DefaultBiomes(category)));
        }

        var database = new TerrainStampDatabase();
        database.Replace(definitions);

        return database;
    }

    public static void Attach(WorldGenerationConfig config)
    {
        TerrainStampDatabase database = Load();

        if (database != null)
            Set(config.Stamps, "Database", database);
    }

    public static void Set(object target, string property, object value)
    {
        FieldInfo field = target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new ArgumentException($"no property {property} on {target.GetType().Name}");

        if (field.FieldType == typeof(float) && value is int whole)
            value = (float)whole;

        field.SetValue(target, value);
    }
}

static class StampChecks
{
    private static int _failures;

    public static void Run(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        TerrainStampDatabase database = StampLoader.Load() ?? throw new InvalidOperationException("stamp pack not found");

        CheckRaw(database);
        CheckSampling(database);
        CheckOperations(database);
        CheckRotation(database);
        CheckBoundary(database);
        CheckOverlap(database);
        CheckPlacement(config, biomes, database);

        Finish("Штампы", "RAW16 1024x1024 little-endian, чёрное = ноль, ADD поднимает, SUBTRACT опускает, повороты 0/90/180/270, билинейная выборка, ноль вне UV, без ступеньки на краю, мягкий потолок, детерминированное размещение.");
    }

    private static void CheckRaw(TerrainStampDatabase database)
    {
        foreach (TerrainStampDefinition definition in database.Stamps)
        {
            byte[] bytes = definition.HeightData.bytes;

            Expect(bytes.Length == 2097152, $"{definition.Name}: {bytes.Length} bytes, expected 2097152");
            Expect(definition.IsValid, $"{definition.Name}: definition is not valid");

            TerrainStampShape shape = TerrainStampShape.From(definition);
            int max = 0, border = 0;

            for (int row = 0; row < shape.Resolution; row++)
            {
                for (int column = 0; column < shape.Resolution; column++)
                {
                    int value = shape.Raw(column, row);
                    max = Math.Max(max, value);

                    if (row == 0 || column == 0 || row == shape.Resolution - 1 || column == shape.Resolution - 1)
                        border = Math.Max(border, value);
                }
            }

            int middle = (shape.Resolution / 2 * shape.Resolution + shape.Resolution / 2) * 2;
            int little = bytes[middle] | bytes[middle + 1] << 8;

            Expect(shape.Raw(shape.Resolution / 2, shape.Resolution / 2) == little, $"{definition.Name}: not read as little-endian");
            Expect(max == 65535, $"{definition.Name}: peak {max}, expected the full 65535");
            Expect(border == 0, $"{definition.Name}: border reaches {border}, expected zero");
        }

        Console.WriteLine($"  RAW: {database.Count} штампов по 2 097 152 байта, uint16 LE, края нулевые, пик 65535");
    }

    private static void CheckSampling(TerrainStampDatabase database)
    {
        TerrainStampShape shape = TerrainStampShape.From(database.Get(0));
        int last = shape.Resolution - 1;
        float worst = 0f;
        var random = new System.Random(3);

        for (int i = 0; i < 2000; i++)
        {
            int column = random.Next(0, last);
            int row = random.Next(0, last);

            float exact = shape.Sample(column / (float)last, row / (float)last) * 65535f;
            worst = Mathf.Max(worst, Mathf.Abs(exact - shape.Raw(column, row)));

            float middle = shape.Sample((column + 0.5f) / last, (row + 0.5f) / last) * 65535f;
            float average = (shape.Raw(column, row) + shape.Raw(column + 1, row) + shape.Raw(column, row + 1) + shape.Raw(column + 1, row + 1)) / 4f;

            worst = Mathf.Max(worst, Mathf.Abs(middle - average));
        }

        Expect(worst < 0.05f, $"bilinear sampling off by {worst} levels");

        float outside = 0f;

        foreach (float u in new[] { -0.01f, 1.01f, -5f, 7f })
            outside = Mathf.Max(outside, Mathf.Max(shape.Sample(u, 0.5f), shape.Sample(0.5f, u)));

        Expect(outside == 0f, $"outside UV influence {outside}");

        Console.WriteLine($"  выборка: узлы и середины ячеек сходятся с RAW до {worst:0.000} уровня, вне UV влияние {outside}");
    }

    private static void CheckOperations(TerrainStampDatabase database)
    {
        foreach (int index in new[] { 0, 14 })
        {
            TerrainStampDefinition definition = database.Get(index);
            TerrainStampShape shape = TerrainStampShape.From(definition);
            float[] heights = Flat(513, 0.4f);

            var placement = new TerrainStampPlacement
            {
                Center = new Vector2(1024f, 1024f),
                Size = new Vector2(1200f, 1200f),
                Amplitude = 80f,
                StampIndex = 0,
                Operation = definition.Operation
            };

            TerrainStampApplier.Apply(heights, 513, 2048f, 384f, new[] { placement }, new[] { shape }, Settings());

            float lowest = float.MaxValue, highest = float.MinValue;

            foreach (float h in heights)
            {
                lowest = Mathf.Min(lowest, h * 384f);
                highest = Mathf.Max(highest, h * 384f);
            }

            float flat = 0.4f * 384f;

            if (definition.Operation == TerrainStampOperation.Add)
                Expect(lowest >= flat - 1e-3f && highest > flat + 40f, $"{definition.Name}: ADD gave {lowest:0.0}..{highest:0.0} over {flat:0.0}");
            else
                Expect(highest <= flat + 1e-3f && lowest < flat - 40f, $"{definition.Name}: SUBTRACT gave {lowest:0.0}..{highest:0.0} under {flat:0.0}");

            Console.WriteLine($"  {definition.Name} ({definition.Operation}): ровная земля {flat:0.0} м -> {lowest:0.0}..{highest:0.0} м");
        }

        Expect(Mathf.Abs(TerrainStampApplier.Combine(150f, 0f, 0f, 384f, 0.9f) - 150f) < 1e-4f, "zero displacement changed the height");
    }

    private static void CheckRotation(TerrainStampDatabase database)
    {
        TerrainStampShape shape = TerrainStampShape.From(database.Get(2));
        float worst = 0f;
        var random = new System.Random(5);

        foreach (float angle in new[] { 90f, 180f, 270f })
        {
            var still = new TerrainStampPlacement { Center = new Vector2(1000f, 1000f), Size = new Vector2(1600f, 1000f), Amplitude = 100f };
            TerrainStampPlacement turned = still;
            turned.Rotation = angle;

            for (int i = 0; i < 500; i++)
            {
                var local = new Vector2((float)random.NextDouble() * 1600f - 800f, (float)random.NextDouble() * 1000f - 500f);

                Vector2 plain = still.World(local.x, local.y);
                Vector2 spun = turned.World(local.x, local.y);

                float a = TerrainStampApplier.Displacement(still, shape, plain.x, plain.y);
                float b = TerrainStampApplier.Displacement(turned, shape, spun.x, spun.y);

                worst = Mathf.Max(worst, Mathf.Abs(a - b));
            }

            var probe = new Vector2(1000f + 300f, 1000f);
            Vector2 back = turned.Local(probe.x, probe.y);
            float expectedX = 300f * Mathf.Cos(angle * Mathf.Deg2Rad);

            Expect(Mathf.Abs(back.x - expectedX) < 1e-2f, $"rotation {angle}: local x {back.x}, expected {expectedX}");
        }

        Expect(worst < 1e-3f, $"rotated stamp differs by {worst} m");
        Console.WriteLine($"  поворот 90/180/270: форма совпадает до {worst:0.0000} м");
    }

    private static void CheckBoundary(TerrainStampDatabase database)
    {
        float worst = 0f;

        for (int index = 0; index < database.Count; index++)
        {
            TerrainStampShape shape = TerrainStampShape.From(database.Get(index));
            var placement = new TerrainStampPlacement { Center = Vector2.zero, Size = new Vector2(1000f, 1000f), Amplitude = 300f, Rotation = 37f };

            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f - 0.5f;

                foreach (Vector2 local in new[] { new Vector2(t * 1000f, 499.9f), new Vector2(499.9f, t * 1000f), new Vector2(t * 1000f, -499.9f), new Vector2(-499.9f, t * 1000f) })
                {
                    Vector2 world = placement.World(local.x, local.y);
                    worst = Mathf.Max(worst, TerrainStampApplier.Displacement(placement, shape, world.x, world.y));
                }
            }
        }

        Expect(worst < 1e-3f, $"stamp edge carries {worst} m, a step at the footprint border");
        Console.WriteLine($"  край пятна: смещение на границе до {worst:0.00000} м при амплитуде 300 м");
    }

    private static void CheckOverlap(TerrainStampDatabase database)
    {
        TerrainStampShape shape = TerrainStampShape.From(database.Get(0));
        float[] heights = Flat(257, 0.8f);
        var placements = new List<TerrainStampPlacement>();

        for (int i = 0; i < 5; i++)
            placements.Add(new TerrainStampPlacement { Center = new Vector2(512f, 512f), Size = new Vector2(900f, 900f), Amplitude = 200f, Rotation = i * 30f });

        TerrainStampApplier.Apply(heights, 257, 1024f, 384f, placements, new[] { shape }, Settings());

        float highest = 0f;

        foreach (float h in heights)
            highest = Mathf.Max(highest, h);

        Expect(highest <= 1f, $"stacked stamps reached {highest * 384f:0.0} m over MaxHeight");

        float blended = TerrainStampApplier.Combine(100f, 150f + 0.35f * 150f, 0f, 384f, 0.9f);
        Expect(blended < 100f + 300f - 1f, $"two overlapping 150 m stamps summed to {blended - 100f:0.0} m instead of blending");

        float moderate = TerrainStampApplier.Combine(300f, 100f, 0f, 384f, 0.9f);
        float gentle = TerrainStampApplier.Combine(300f, 40f, 0f, 384f, 0.9f);
        Expect(moderate < 383f && moderate > gentle + 5f, $"an overshoot to 400 m came out at {moderate:0.0} m against {gentle:0.0} m for 340 m");

        Console.WriteLine($"  пять гор в одной точке на высоте 307 м: вершина {highest * 384f:0.0} м при MaxHeight 384 м; "
            + $"два штампа по 150 м дают {blended - 100f:0} м вместо 300; 300 + 100 м -> {moderate:0.0} м, 300 + 40 м -> {gentle:0.0} м");
    }

    private static void CheckPlacement(WorldGenerationConfig config, BiomeDatabase biomes, TerrainStampDatabase database)
    {
        var map = new BiomeMap(config.BiomeMapResolution, config.WorldSize);
        var settings = new TerrainStampSettings();
        StampLoader.Set(settings, "Database", database);

        List<TerrainStampPlacement> first = new TerrainStampPlacer(config, settings, biomes, map).Place();
        List<TerrainStampPlacement> second = new TerrainStampPlacer(config, settings, biomes, map).Place();

        Expect(first.Count > 0, "no stamp placed");
        Expect(first.Count == second.Count, "same seed placed a different number of stamps");

        for (int i = 0; i < Math.Min(first.Count, second.Count); i++)
        {
            Expect(first[i].Center.x == second[i].Center.x && first[i].Center.y == second[i].Center.y && first[i].Rotation == second[i].Rotation
                && first[i].StampIndex == second[i].StampIndex && first[i].Amplitude == second[i].Amplitude, $"placement {i} differs between runs");
        }

        foreach (TerrainStampPlacement placement in first)
        {
            placement.Bounds(out Vector2 min, out Vector2 max);
            Expect(min.x >= settings.EdgeMargin && min.y >= settings.EdgeMargin && max.x <= config.WorldSize - settings.EdgeMargin
                && max.y <= config.WorldSize - settings.EdgeMargin, $"{placement.Name} leaves the world margin");

            foreach (TerrainStampPlacement other in first)
            {
                if (ReferenceEquals(placement.Name, other.Name) && placement.Center.x == other.Center.x && placement.Center.y == other.Center.y)
                    continue;

                Expect(Vector2.Distance(placement.Center, other.Center) >= settings.MinSpacing - 1e-2f, $"{placement.Name} and {other.Name} closer than MinSpacing");
            }
        }

        var shifted = new WorldGenerationConfig();
        StampLoader.Set(shifted, "Seed", config.Seed + 1);
        StampLoader.Set(shifted, "WorldSize", config.WorldSize);

        List<TerrainStampPlacement> other2 = new TerrainStampPlacer(shifted, settings, biomes, map).Place();
        bool differs = other2.Count != first.Count || other2.Count > 0 && other2[0].Center.x != first[0].Center.x;

        Expect(differs, "a different seed placed the same stamps");

        var names = new List<string>();

        foreach (TerrainStampPlacement placement in first)
            names.Add($"{placement.Name} ({placement.Center.x:0}, {placement.Center.y:0}) {placement.Rotation:0}°");

        Console.WriteLine($"  размещение: {first.Count} штампов, повтор с тем же seed совпадает, другой seed даёт другое: {string.Join("; ", names)}");
    }

    private static TerrainStampSettings Settings()
    {
        return new TerrainStampSettings();
    }

    private static float[] Flat(int resolution, float height)
    {
        var heights = new float[resolution * resolution];

        for (int i = 0; i < heights.Length; i++)
            heights[i] = height;

        return heights;
    }

    public static void Expect(bool condition, string message)
    {
        if (condition)
            return;

        _failures++;
        Console.WriteLine($"  ПРОВАЛ: {message}");
    }

    public static void Finish(string what, string summary)
    {
        if (_failures > 0)
        {
            Console.WriteLine($"{what}: {_failures} проверок не прошли");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"{what}: всё в порядке — {summary}");
    }
}
