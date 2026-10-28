using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class VariationRun
{
    public static void Run(string spec, WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois, string previewPath)
    {
        WorldVariation variation = Parse(spec, config.Seed);
        config.Apply(variation);

        Console.WriteLine($"вариация: мир {config.WorldSize} м, seed {config.Seed}, ReliefScale {config.ReliefScale:0.###}, горы {config.MountainMaskLow:0.##}..{config.MountainMaskHigh:0.##}, "
            + $"частоты континент {config.ContinentFrequency:0.##} холмы {config.HillFrequency:0.##} хребты {config.RidgeFrequency:0.##}, {config.SettlementMix}, "
            + $"RiverStartArea {config.Water.RiverStartArea:0.##}, озёра {config.Water.LakeDensity:0.##}, пруды {config.Water.PondDensity:0.##}, хутора {config.DirtChance:0.##}");

        var clock = Stopwatch.StartNew();
        TimeSpan previous = TimeSpan.Zero;

        WorldMapResult result = new WorldMapPipeline(config, biomes, pois).Generate((title, _) =>
        {
            TimeSpan now = clock.Elapsed;
            Console.WriteLine($"  {now:mm\\:ss} (+{(now - previous).TotalSeconds:0} с) {title}");
            previous = now;
        });

        Console.WriteLine($"готово за {clock.Elapsed:mm\\:ss}");
        Report(config, result);
        ReportBiomes(biomes, result.Biomes);

        if (previewPath != null)
            Preview(previewPath, biomes, result);
    }

    private static void ReportBiomes(BiomeDatabase biomes, BiomeMap map)
    {
        var counts = new int[biomes.Count];
        foreach (byte cell in map.Cells)
            counts[cell]++;

        var shares = Enumerable.Range(0, biomes.Count).Select(i => $"{biomes.Get(i).Type} {counts[i] * 100f / map.Cells.Length:0}%");
        Console.WriteLine($"биомы: {string.Join(", ", shares)}");
    }

    private static void Preview(string path, BiomeDatabase biomes, WorldMapResult result)
    {
        PaintMapColors(biomes);
        var clock = Stopwatch.StartNew();
        Color32[] pixels = WorldPreviewPainter.Paint(biomes, result.Biomes, result.Heights, result.Water, result.Roads.Paved(), result.Placements);
        int size = WorldPreviewPainter.RESOLUTION;
        var rgb = new byte[pixels.Length * 3];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 pixel = pixels[(size - 1 - y) * size + x];
                int offset = (y * size + x) * 3;
                rgb[offset] = pixel.r;
                rgb[offset + 1] = pixel.g;
                rgb[offset + 2] = pixel.b;
            }
        }

        Png.Write(path, rgb, size, size);
        Console.WriteLine($"предпросмотр {path} за {clock.Elapsed.TotalSeconds:0.0} с");
    }

    private static void PaintMapColors(BiomeDatabase biomes)
    {
        FieldInfo field = typeof(BiomeDefinition).GetField("<MapColor>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        for (int i = 0; i < biomes.Count; i++)
        {
            (byte r, byte g, byte b) = Draw.Palette(biomes.Get(i).Type);
            field.SetValue(biomes.Get(i), new Color32(r, g, b, 255));
        }
    }

    private static WorldVariation Parse(string spec, int seed)
    {
        var variation = new WorldVariation { Seed = seed };

        foreach (string pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=');
            FieldInfo field = typeof(WorldVariation).GetField(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new ArgumentException($"WorldVariation has no field '{parts[0]}'");

            object value = field.FieldType.IsEnum
                ? Enum.Parse(field.FieldType, parts[1], true)
                : Convert.ChangeType(parts[1], field.FieldType, CultureInfo.InvariantCulture);
            field.SetValue(variation, value);
        }

        return variation;
    }

    private static void Report(WorldGenerationConfig config, WorldMapResult result)
    {
        float[] heights = result.Heights.Heights;
        float low = heights.Min() * result.Heights.MaxHeight;
        float high = heights.Max() * result.Heights.MaxHeight;
        int flat = heights.Count(height => height * result.Heights.MaxHeight >= result.Heights.MaxHeight - 0.5f);

        Console.WriteLine($"высоты {low:0.0}..{high:0.0} м, у потолка {flat} точек");

        var types = result.Roads.Hubs.GroupBy(hub => hub.Type).Select(group => $"{group.Key} {group.Count()}");
        Console.WriteLine($"поселения: {result.Roads.Hubs.Count} ({string.Join(", ", types)})");

        float Kilometres(RoadKind kind) => result.Roads.Roads.Concat(result.Roads.Streets).Where(road => road.Kind == kind)
            .Sum(road => Length(road.Points)) / 1000f;

        Console.WriteLine($"дороги: шоссе {Kilometres(RoadKind.Highway):0.0} км, грунтовки {Kilometres(RoadKind.DirtAccess):0.0} км, "
            + $"улицы {Kilometres(RoadKind.Arterial) + Kilometres(RoadKind.LocalStreet):0.0} км");
        Console.WriteLine($"здания {result.Placements.Count}, реквизит {result.Props.Count}");
        Console.WriteLine($"вода: рек {result.Water.Rivers.Count} ({result.Water.Rivers.Sum(river => river.Length) / 1000f:0.0} км), водоёмов {result.Water.Bodies.Count}");
    }

    private static float Length(Vector2[] points)
    {
        float length = 0f;
        for (int i = 1; i < points.Length; i++)
            length += Vector2.Distance(points[i - 1], points[i]);

        return length;
    }
}
