using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class VariationChecks
{
    private const string CONTENT = "../../Assets/_Dustborn/Content/Configs/World";
    private const int SMALL_WORLD = 2048;
    private const float SHARE_TOLERANCE = 0.04f;

    private static int _failures;

    public static int Run(BiomeDatabase biomes)
    {
        _failures = 0;

        foreach (BiomeLayout layout in Enum.GetValues(typeof(BiomeLayout)))
            Shares(biomes, layout);

        Centre(biomes, BiomeLayout.ForestCenter, BiomeType.PineForest);
        Centre(biomes, BiomeLayout.WastelandCenter, BiomeType.Wasteland);
        Determinism(biomes);
        Slopes(biomes);
        Features();

        Console.WriteLine(_failures == 0 ? "вариации: все проверки пройдены" : $"вариации: {_failures} проверок не пройдено");
        return _failures;
    }

    private static void Shares(BiomeDatabase biomes, BiomeLayout layout)
    {
        var variation = new WorldVariation { Seed = 4242, WorldSize = SMALL_WORLD, PineForest = 45, BurntForest = 0, Desert = 20, Snow = 15, Wasteland = 20, Layout = layout };
        WorldGenerationConfig config = Config(variation);
        BiomeMap map = new BiomeMapGenerator(config, biomes).Generate();
        float[] actual = Actual(map, biomes);
        float[] target = variation.BiomeShares();
        float worst = Enumerable.Range(0, target.Length).Max(i => Math.Abs(target[i] - actual[i]));

        Check(worst <= SHARE_TOLERANCE && actual[(int)BiomeType.BurntForest] == 0f,
            $"{layout}: доли биомов {Describe(actual)}, отклонение {worst:P1}");
    }

    private static void Centre(BiomeDatabase biomes, BiomeLayout layout, BiomeType expected)
    {
        BiomeMap map = new BiomeMapGenerator(Config(new WorldVariation { Seed = 99, WorldSize = SMALL_WORLD, Layout = layout }), biomes).Generate();
        BiomeType middle = biomes.Get(map.Get(map.Resolution / 2, map.Resolution / 2)).Type;
        Check(middle == expected, $"{layout}: в центре {middle}");
    }

    private static void Determinism(BiomeDatabase biomes)
    {
        var variation = new WorldVariation { Seed = 31337, WorldSize = SMALL_WORLD, Craters = WorldAmount.Many, Layout = BiomeLayout.Rings };
        (byte[] firstBiomes, float[] firstHeights) = Relief(variation, biomes);
        (byte[] secondBiomes, float[] secondHeights) = Relief(variation, biomes);
        variation.Seed++;
        (byte[] otherBiomes, _) = Relief(variation, biomes);

        Check(firstBiomes.SequenceEqual(secondBiomes) && firstHeights.SequenceEqual(secondHeights) && !firstBiomes.SequenceEqual(otherBiomes),
            "один сид и одни настройки дают один мир, другой сид даёт другой");
    }

    private static void Slopes(BiomeDatabase biomes)
    {
        var flat = new WorldVariation { Seed = 2024, WorldSize = SMALL_WORLD, Plains = 100, Hills = 0, Mountains = 0, Craters = WorldAmount.None, Rifts = WorldAmount.None, Canyons = WorldAmount.None };
        var rugged = new WorldVariation { Seed = 2024, WorldSize = SMALL_WORLD, Plains = 0, Hills = 40, Mountains = 60, Craters = WorldAmount.None, Rifts = WorldAmount.None, Canyons = WorldAmount.None };
        float flatSlope = MeanSlope(Relief(flat, biomes).Heights);
        float ruggedSlope = MeanSlope(Relief(rugged, biomes).Heights);

        Check(ruggedSlope > flatSlope * 1.3f, $"рельеф: средний уклон равнин {flatSlope * 384f:0.000} м/клетку, гор {ruggedSlope * 384f:0.000}");
    }

    private static void Features()
    {
        WorldGenerationConfig config = Config(new WorldVariation { Seed = 3, Canyons = WorldAmount.Many, Rifts = WorldAmount.Many, Craters = WorldAmount.Many });
        var placed = new List<TerrainStampPlacement>();
        var features = new TerrainFeaturePlacer(config, config.Stamps, placed);
        features.Place();

        Check(features.Craters == config.CraterCount && features.Rifts == config.RiftCount && features.Canyons == config.CanyonCount,
            $"особенности 8 км: кратеры {features.Craters}/{config.CraterCount}, разломы {features.Rifts}/{config.RiftCount}, каньоны {features.Canyons}/{config.CanyonCount}");

        WorldGenerationConfig none = Config(new WorldVariation { Seed = 1, Canyons = WorldAmount.None, Rifts = WorldAmount.None });
        List<TerrainStampPlacement> random = new TerrainStampPlacer(none, none.Stamps, null, null).Place();
        bool clean = random.All(placement =>
        {
            TerrainStampCategory category = none.Stamps.Database.Get(placement.StampIndex).Category;
            return !TerrainFeaturePlacer.IsCanyon(category) && !TerrainFeaturePlacer.IsRift(category);
        });

        Check(clean && random.Count > 0, $"без каньонов и разломов случайные штампы ({random.Count}) их не ставят");
    }

    private static (byte[] Biomes, float[] Heights) Relief(WorldVariation variation, BiomeDatabase biomes)
    {
        WorldGenerationConfig config = Config(variation);
        typeof(WorldGenerationConfig).GetField("<HeightCellSize>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(config, 4);

        BiomeMap map = new BiomeMapGenerator(config, biomes).Generate();
        return (map.Cells, new HeightMapGenerator(config, biomes).Generate(map).Heights);
    }

    private static WorldGenerationConfig Config(WorldVariation variation)
    {
        WorldGenerationConfig config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        StampLoader.Attach(config);
        config.Apply(variation);
        return config;
    }

    private static float[] Actual(BiomeMap map, BiomeDatabase biomes)
    {
        var shares = new float[Enum.GetValues(typeof(BiomeType)).Length];
        foreach (byte cell in map.Cells)
            shares[(int)biomes.Get(cell).Type] += 1f / map.Cells.Length;

        return shares;
    }

    private static string Describe(float[] shares) => string.Join(", ", shares.Select((share, i) => $"{(BiomeType)i} {share:P0}"));

    private static float MeanSlope(float[] heights)
    {
        int resolution = (int)Math.Sqrt(heights.Length);
        double total = 0;
        int count = 0;

        for (int y = 1; y < resolution - 1; y++)
        {
            for (int x = 1; x < resolution - 1; x++)
            {
                int i = y * resolution + x;
                total += Math.Abs(heights[i + 1] - heights[i - 1]) + Math.Abs(heights[i + resolution] - heights[i - resolution]);
                count++;
            }
        }

        return (float)(total / count);
    }

    private static void Check(bool condition, string message)
    {
        Console.WriteLine($"{(condition ? "ok  " : "FAIL")} {message}");
        if (!condition)
            _failures++;
    }
}
