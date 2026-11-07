using System;
using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[Category("WorldGen.Variation")]
public class WorldVariationTests
{
    private const string CONTENT = "Assets/_Dustborn/Content/Configs/World";
    private const int SMALL_WORLD = 2048;
    private const int COARSE_CELL = 4;
    private const float SHARE_TOLERANCE = 0.04f;

    private readonly List<Object> _created = new();

    [TearDown]
    public void Clean()
    {
        foreach (Object created in _created)
        {
            if (created != null)
                Object.DestroyImmediate(created);
        }

        _created.Clear();
    }

    [TestCase(1024)]
    [TestCase(3000)]
    [TestCase(11264)]
    public void SizeOutsideTheStepsIsRefused(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldVariation { WorldSize = size }.Validate());
    }

    [Test]
    public void EverySizeStepIsAccepted()
    {
        for (int size = WorldVariation.MIN_SIZE; size <= WorldVariation.MAX_SIZE; size += WorldVariation.SIZE_STEP)
            Assert.DoesNotThrow(() => new WorldVariation { WorldSize = size }.Validate(), $"{size} m must be a valid world");
    }

    [Test]
    public void SharesOutsideZeroToHundredAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldVariation { Plains = -5 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldVariation { Desert = 120 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldVariation { PineForest = 0, BurntForest = 0, Desert = 0, Snow = 0, Wasteland = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldVariation { Craters = (WorldAmount)9 }.Validate());
    }

    [Test]
    public void ReliefSharesAreNormalizedAndMountainsAreCapped()
    {
        ReliefShares shares = new WorldVariation { Plains = 10, Hills = 10, Mountains = 80 }.Relief();

        Assert.AreEqual(1f, shares.Plains + shares.Hills + shares.Mountains, 1e-5f);
        Assert.AreEqual(WorldVariation.MAX_MOUNTAINS / 100f, shares.Mountains, 1e-5f);
        Assert.AreEqual(shares.Plains, shares.Hills, 1e-5f, "The excess is spread over plains and hills in proportion");
    }

    [Test]
    public void BiomeSharesAreNormalized()
    {
        float[] shares = new WorldVariation { PineForest = 20, BurntForest = 20, Desert = 20, Snow = 20, Wasteland = 0 }.BiomeShares();

        Assert.AreEqual(0.25f, shares[(int)BiomeType.PineForest], 1e-5f);
        Assert.AreEqual(0f, shares[(int)BiomeType.Wasteland], 1e-5f);
    }

    [Test]
    public void SeedTextIsStableAndNumbersPassThrough()
    {
        Assert.AreEqual(12345, WorldVariation.SeedOf("12345"));
        Assert.AreEqual(WorldVariation.SeedOf("Navezgane"), WorldVariation.SeedOf(" Navezgane "));
        Assert.AreNotEqual(WorldVariation.SeedOf("Navezgane"), WorldVariation.SeedOf("Pregen"));
        Assert.GreaterOrEqual(WorldVariation.SeedOf("Navezgane"), 0);
    }

    [Test]
    public void ApplyMapsEveryParameterOntoTheConfig()
    {
        WorldGenerationConfig config = Config();
        float riverStart = config.Water.RiverStartArea;

        config.Apply(new WorldVariation
        {
            Seed = 77,
            WorldSize = 4096,
            Towns = WorldAmount.None,
            Wilderness = WorldAmount.None,
            Rivers = WorldAmount.None,
            Lakes = WorldAmount.None,
            Craters = WorldAmount.Many,
            Rifts = WorldAmount.None,
            Canyons = WorldAmount.Few,
            Plains = 50,
            Hills = 30,
            Mountains = 20,
            Layout = BiomeLayout.Stripes
        });

        Assert.AreEqual(77, config.Seed);
        Assert.AreEqual(4096, config.WorldSize);
        Assert.AreEqual(0, config.CityProfile.Count + config.TownProfile.Count + config.CountryTownProfile.Count + config.GhostTownProfile.Count,
            "No towns means no settlement of any type");
        Assert.AreEqual(0f, config.DirtChance, "No wilderness POI means no dirt spurs to put them on");
        Assert.AreEqual(0f, config.FarmsteadChance);
        Assert.Greater(config.Water.RiverStartArea, riverStart * 100f, "No rivers means no catchment is large enough");
        Assert.IsFalse(config.Water.ThroughRiver);
        Assert.AreEqual(0f, config.Water.LakeDensity);
        Assert.AreEqual(0f, config.Water.PondDensity);
        Assert.Greater(config.CraterCount, 0);
        Assert.AreEqual(0, config.RiftCount);
        Assert.IsFalse(config.RandomRifts, "No rifts must also keep the random stamps from placing one");
        Assert.IsTrue(config.RandomCanyons);
        Assert.Greater(config.CanyonCount, 0);
        Assert.IsTrue(config.ReliefSharesEnabled);
        Assert.AreEqual(0.5f, config.PlainsShare, 1e-5f);
        Assert.AreEqual(BiomeLayout.Stripes, config.BiomeLayout);
        Assert.IsTrue(config.HasBiomeShares);
    }

    [Test]
    public void FeatureCountsGrowWithTheAmount()
    {
        int few = WorldVariation.Count(WorldAmount.Few, 3, 1f);
        int normal = WorldVariation.Count(WorldAmount.Normal, 3, 1f);
        int many = WorldVariation.Count(WorldAmount.Many, 3, 1f);

        Assert.AreEqual(0, WorldVariation.Count(WorldAmount.None, 3, 1f));
        Assert.Less(few, normal);
        Assert.Less(normal, many);
        Assert.AreEqual(1, WorldVariation.Count(WorldAmount.Few, 3, 0.0625f), "A small world still gets one feature when asked for any");
    }

    [Test]
    public void ApplyLeavesTheSharedAssetsUntouched()
    {
        var asset = AssetDatabase.LoadAssetAtPath<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        Assert.IsNotNull(asset);

        string before = JsonUtility.ToJson(asset);
        WorldGenerationConfig copy = Object.Instantiate(asset);
        _created.Add(copy);
        copy.Apply(new WorldVariation { Seed = 5, WorldSize = 3072, Towns = WorldAmount.Many, Mountains = 60, Plains = 0, Hills = 40 });

        Assert.AreEqual(before, JsonUtility.ToJson(asset), "Applying a variation must only touch the instantiated copy");
    }

    [TestCase(BiomeLayout.Random)]
    [TestCase(BiomeLayout.ForestCenter)]
    [TestCase(BiomeLayout.WastelandCenter)]
    [TestCase(BiomeLayout.Rings)]
    [TestCase(BiomeLayout.Stripes)]
    public void BiomeSharesAreHonoured(BiomeLayout layout)
    {
        BiomeDatabase biomes = Biomes();
        WorldGenerationConfig config = Config();
        var variation = new WorldVariation
        {
            Seed = 4242,
            WorldSize = SMALL_WORLD,
            PineForest = 45,
            BurntForest = 0,
            Desert = 20,
            Snow = 15,
            Wasteland = 20,
            Layout = layout
        };

        config.Apply(variation);
        BiomeMap map = new BiomeMapGenerator(config, biomes).Generate();
        float[] actual = Shares(map, biomes);
        float[] target = variation.BiomeShares();

        for (int i = 0; i < target.Length; i++)
            Assert.AreEqual(target[i], actual[i], SHARE_TOLERANCE, $"{(BiomeType)i} under the {layout} layout");

        Assert.AreEqual(0f, actual[(int)BiomeType.BurntForest], "A biome at zero percent must not appear");
    }

    [TestCase(BiomeLayout.ForestCenter, BiomeType.PineForest)]
    [TestCase(BiomeLayout.WastelandCenter, BiomeType.Wasteland)]
    [TestCase(BiomeLayout.Rings, BiomeType.PineForest)]
    public void CentredLayoutsPutTheirBiomeInTheMiddle(BiomeLayout layout, BiomeType centre)
    {
        BiomeDatabase biomes = Biomes();
        WorldGenerationConfig config = Config();
        config.Apply(new WorldVariation { Seed = 99, WorldSize = SMALL_WORLD, Layout = layout });

        BiomeMap map = new BiomeMapGenerator(config, biomes).Generate();
        byte middle = map.Get(map.Resolution / 2, map.Resolution / 2);

        Assert.AreEqual(centre, biomes.Get(middle).Type);
    }

    [Test]
    public void TheSameVariationGivesTheSameWorld()
    {
        BiomeDatabase biomes = Biomes();
        var variation = new WorldVariation { Seed = 31337, WorldSize = SMALL_WORLD, Craters = WorldAmount.Many, Layout = BiomeLayout.Rings };

        (byte[] firstBiomes, float[] firstHeights) = Relief(variation, biomes);
        (byte[] secondBiomes, float[] secondHeights) = Relief(variation, biomes);

        CollectionAssert.AreEqual(firstBiomes, secondBiomes);
        CollectionAssert.AreEqual(firstHeights, secondHeights);

        variation.Seed++;
        (byte[] otherBiomes, _) = Relief(variation, biomes);
        CollectionAssert.AreNotEqual(firstBiomes, otherBiomes, "Another seed must give another world");
    }

    [Test]
    public void ReliefBandsFollowTheShares()
    {
        var samples = new float[4096];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Mathf.Pow(i / (samples.Length - 1f), 2f);

        float plains = ReliefBands.Quantile(samples, 0.3f);
        float mountains = ReliefBands.Quantile(samples, 0.75f);

        Assert.AreEqual(0.3f, Below(samples, plains), 0.01f);
        Assert.AreEqual(0.25f, 1f - Below(samples, mountains), 0.01f);
        Assert.Greater(ReliefBands.Quantile(samples, 1f), samples[samples.Length - 1], "No mountains puts the threshold above every sample");
        Assert.Less(ReliefBands.Quantile(samples, 0f), samples[0], "No plains puts the threshold below every sample");
    }

    [Test]
    public void MoreMountainsMakeSteeperGround()
    {
        BiomeDatabase biomes = Biomes();
        var flat = new WorldVariation { Seed = 2024, WorldSize = SMALL_WORLD, Plains = 100, Hills = 0, Mountains = 0, Craters = WorldAmount.None, Rifts = WorldAmount.None, Canyons = WorldAmount.None };
        var rugged = new WorldVariation { Seed = 2024, WorldSize = SMALL_WORLD, Plains = 0, Hills = 40, Mountains = 60, Craters = WorldAmount.None, Rifts = WorldAmount.None, Canyons = WorldAmount.None };

        float flatSlope = MeanSlope(Relief(flat, biomes).Heights);
        float ruggedSlope = MeanSlope(Relief(rugged, biomes).Heights);

        Assert.Greater(ruggedSlope, flatSlope * 1.3f, $"Mountains {ruggedSlope:0.####} against plains {flatSlope:0.####}");
    }

    [Test]
    public void CraterProfileIsABowlWithARim()
    {
        Assert.AreEqual(1f, TerrainStampShapes.BowlProfile(0f), 1e-5f);
        Assert.AreEqual(0f, TerrainStampShapes.BowlProfile(TerrainStampShapes.CRATER_RADIUS), 1e-5f);
        Assert.AreEqual(1f, TerrainStampShapes.RimProfile(TerrainStampShapes.CRATER_RADIUS), 1e-3f);
        Assert.Less(TerrainStampShapes.RimProfile(0f), 0.01f);
        Assert.AreEqual(0f, TerrainStampShapes.RimProfile(1f), 1e-5f, "The rim must fade out before the stamp edge");
    }

    [Test]
    public void CratersCutABowlAndRaiseARim()
    {
        WorldGenerationConfig config = Config();
        config.Apply(new WorldVariation { Seed = 8, WorldSize = SMALL_WORLD, Craters = WorldAmount.Many, Rifts = WorldAmount.None, Canyons = WorldAmount.None });
        SerializedFields.Set(config.Stamps, "Database", null);

        var placements = new List<TerrainStampPlacement>();
        var placer = new TerrainFeaturePlacer(config, config.Stamps, placements);
        placer.Place();

        Assert.AreEqual(config.CraterCount, placer.Craters);
        Assert.AreEqual(2 * placer.Craters, placements.Count, "A crater is a bowl and a rim");

        const int resolution = 513;
        const float maxHeight = 384f;
        const float ground = 0.5f;
        var heights = new float[resolution * resolution];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = ground;

        TerrainStampShape[] shapes = TerrainStampShapes.For(null, placements);
        TerrainStampApplier.Apply(heights, resolution, SMALL_WORLD, maxHeight, placements, shapes, config.Stamps);

        TerrainStampPlacement bowl = placements[0];
        float cell = SMALL_WORLD / (float)(resolution - 1);
        float centre = heights[Index(bowl.Center, cell, resolution)];
        float rimRadius = bowl.Size.x * 0.5f * TerrainStampShapes.CRATER_RADIUS;
        float rim = heights[Index(bowl.Center + new Vector2(rimRadius, 0f), cell, resolution)];

        Assert.Less(centre * maxHeight, ground * maxHeight - bowl.Amplitude * 0.8f, "The crater floor sits well under the ground");
        Assert.Greater(rim, ground, "The rim stands above the ground");
    }

    [Test]
    public void DisabledCanyonsKeepRandomStampsOffCanyons()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        WorldGenerationConfig config = Object.Instantiate(settings.Config);
        _created.Add(config);
        config.Apply(new WorldVariation { Seed = 1, Canyons = WorldAmount.None, Rifts = WorldAmount.None });

        TerrainStampDatabase database = config.Stamps.Database;
        Assume.That(database != null && database.Count > 0, "The project has no terrain stamp database");

        List<TerrainStampPlacement> placed = new TerrainStampPlacer(config, config.Stamps, null, null).Place();
        var features = new TerrainFeaturePlacer(config, config.Stamps, placed);
        features.Place();

        foreach (TerrainStampPlacement placement in placed)
        {
            TerrainStampCategory category = database.Get(placement.StampIndex).Category;
            Assert.IsFalse(TerrainFeaturePlacer.IsCanyon(category) || TerrainFeaturePlacer.IsRift(category), $"{placement.Name} must not be placed");
        }

        Assert.AreEqual(0, features.Rifts + features.Canyons);
    }

    [Test]
    public void CanyonsAndRiftsComeFromTheStampDatabase()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        WorldGenerationConfig config = Object.Instantiate(settings.Config);
        _created.Add(config);
        config.Apply(new WorldVariation { Seed = 3, Canyons = WorldAmount.Many, Rifts = WorldAmount.Many, Craters = WorldAmount.None });

        TerrainStampDatabase database = config.Stamps.Database;
        Assume.That(database != null && database.Count > 0, "The project has no terrain stamp database");

        var placed = new List<TerrainStampPlacement>();
        var features = new TerrainFeaturePlacer(config, config.Stamps, placed);
        features.Place();

        Assert.Greater(features.Canyons, 0);
        Assert.Greater(features.Rifts, 0);

        foreach (TerrainStampPlacement placement in placed)
        {
            TerrainStampCategory category = database.Get(placement.StampIndex).Category;
            Assert.IsTrue(TerrainFeaturePlacer.IsCanyon(category) || TerrainFeaturePlacer.IsRift(category));
        }
    }

    [Test, Explicit("Runs the whole map pipeline on a 2 km world, a couple of minutes")]
    public void AWorldWithoutTownsOrWaterHasNeither()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        WorldGenerationConfig config = Object.Instantiate(settings.Config);
        _created.Add(config);
        config.Apply(new WorldVariation { Seed = 11, WorldSize = SMALL_WORLD, Towns = WorldAmount.None, Rivers = WorldAmount.None, Lakes = WorldAmount.None });

        WorldMapResult result = new WorldMapPipeline(config, settings.Biomes, settings.Pois, settings.Props).Generate();

        Assert.AreEqual(0, result.Roads.Hubs.Count);
        Assert.AreEqual(0, result.Placements.Count);
        Assert.AreEqual(0, result.Water.Rivers.Count);
        Assert.AreEqual(0, result.Water.Bodies.Count);
    }

    private (byte[] Biomes, float[] Heights) Relief(WorldVariation variation, BiomeDatabase biomes)
    {
        WorldGenerationConfig config = Config();
        config.Apply(variation);
        SerializedFields.Set(config, "HeightCellSize", COARSE_CELL);

        BiomeMap map = new BiomeMapGenerator(config, biomes).Generate();
        HeightMap heights = new HeightMapGenerator(config, biomes).Generate(map);
        return (map.Cells, heights.Heights);
    }

    private WorldGenerationConfig Config()
    {
        var config = ScriptableObject.CreateInstance<WorldGenerationConfig>();
        _created.Add(config);
        return config;
    }

    private static BiomeDatabase Biomes()
    {
        var biomes = AssetDatabase.LoadAssetAtPath<BiomeDatabase>($"{CONTENT}/BiomeDatabase.asset");
        Assert.IsNotNull(biomes, "BiomeDatabase.asset is missing");
        return biomes;
    }

    private static float[] Shares(BiomeMap map, BiomeDatabase biomes)
    {
        var shares = new float[Enum.GetValues(typeof(BiomeType)).Length];

        foreach (byte cell in map.Cells)
            shares[(int)biomes.Get(cell).Type] += 1f / map.Cells.Length;

        return shares;
    }

    private static float Below(float[] sorted, float threshold)
    {
        int count = 0;
        foreach (float sample in sorted)
        {
            if (sample < threshold)
                count++;
        }

        return count / (float)sorted.Length;
    }

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

    private static int Index(Vector2 point, float cell, int resolution)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(point.x / cell), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(point.y / cell), 0, resolution - 1);
        return y * resolution + x;
    }
}
