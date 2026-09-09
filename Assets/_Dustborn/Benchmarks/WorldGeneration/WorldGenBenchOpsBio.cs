using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsBio
{
    public delegate void SeedStep(ref Random random);

    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["bio.generate"] = Generate;
        registry["bio.classify"] = Classify;
        registry["bio.smooth"] = Smooth;
        registry["bio.regions"] = Regions;
        registry["bio.weights"] = Weights;
        registry["bio.sample"] = Sample;
        registry["bio.native"] = Native;
        registry["bio.edges"] = Edges;
    }

    private static WorldGenBenchOp Generate()
    {
        return new WorldGenBenchOp("bio.generate", context =>
        {
            BiomeMap map = new BiomeMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate();
            context.Put("map", map);
        })
        {
            Verify = context =>
            {
                var map = context.Get<BiomeMap>("map");
                WorldGenerationConfig config = context.Profile.Config;

                context.Require(map.Resolution == config.BiomeMapResolution, "The biome map must match the configured resolution.");
                context.Count("cells", map.Cells.Length);
                context.Count("resolution", map.Resolution);

                var histogram = new int[256];
                int biomes = context.Profile.Biomes.Count;

                foreach (byte cell in map.Cells)
                {
                    context.Require(cell < biomes, "Every cell must hold a valid biome index.");
                    histogram[cell]++;
                }

                for (int index = 0; index < biomes; index++)
                    context.Count("biome" + index, histogram[index]);

                context.Print("biomeCells", WorldGenBenchArgs.Hash(map.Cells));

                BiomeMap again = new BiomeMapGenerator(config, context.Profile.Biomes).Generate();
                context.Require(WorldGenBenchArgs.Hash(again.Cells) == WorldGenBenchArgs.Hash(map.Cells),
                    "The biome map must be deterministic for one seed.");
            }
        };
    }

    private static WorldGenBenchOp Classify()
    {
        BiomeMapGenerator generator = null;
        SeedStep seeds = null;
        Action<BiomeMap> classify = null;
        BiomeMap map = null;

        return new WorldGenBenchOp("bio.classify", context => classify(map))
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;

                if (context.Args.Has("seedsPerBiome"))
                    WorldGenBenchProfile.Assign(config, "SeedsPerBiome", context.Args.Int("seedsPerBiome", 1));

                if (context.Args.Has("warp"))
                    WorldGenBenchProfile.Assign(config, "WarpStrength", context.Args.Bool("warp", true) ? config.WarpStrength : 0f);

                generator = new BiomeMapGenerator(config, context.Profile.Biomes);
                seeds = WorldGenBenchReflect.Bind<SeedStep>(generator, "PlaceSeeds");
                classify = WorldGenBenchReflect.Bind<Action<BiomeMap>>(generator, "Classify");

                var random = new Random((uint)config.Seed | 1u);
                WorldGenBenchReflect.SetField(generator, "_warpOffset", random.NextFloat2(-100f, 100f));
                seeds(ref random);

                map = new BiomeMap(config.BiomeMapResolution, config.WorldSize);
                context.Count("seeds", ((Array)WorldGenBenchReflect.Field(generator, "_seeds")).Length);
                context.Count("cells", map.Cells.Length);
            },
            Prepare = context =>
            {
                for (int index = 0; index < map.Cells.Length; index++)
                    map.Cells[index] = 255;
            },
            Verify = context =>
            {
                int biomes = context.Profile.Biomes.Count;

                foreach (byte cell in map.Cells)
                    context.Require(cell < biomes, "Classification must leave no uninitialised cell.");

                context.Print("classified", WorldGenBenchArgs.Hash(map.Cells));
            }
        };
    }

    private static WorldGenBenchOp Smooth()
    {
        BiomeMapGenerator generator = null;
        Action<BiomeMap, byte[]> smooth = null;
        BiomeMap pristine = null;
        BiomeMap map = null;
        byte[] buffer = null;
        int passes = 1;

        return new WorldGenBenchOp("bio.smooth", context =>
        {
            for (int pass = 0; pass < passes; pass++)
                smooth(map, buffer);
        })
        {
            Setup = context =>
            {
                passes = context.Args.Int("passes", 1);
                generator = new BiomeMapGenerator(context.Profile.Config, context.Profile.Biomes);
                smooth = WorldGenBenchReflect.Bind<Action<BiomeMap, byte[]>>(generator, "Smooth");

                pristine = context.Args.Text("shape", "real") == "uniform"
                    ? Uniform(context)
                    : WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();

                map = new BiomeMap(pristine.Resolution, pristine.WorldSize);
                buffer = new byte[pristine.Cells.Length];
                context.Count("cells", buffer.Length);
                context.Count("passes", passes);
            },
            Prepare = context => Array.Copy(pristine.Cells, map.Cells, map.Cells.Length),
            Verify = context =>
            {
                int biomes = context.Profile.Biomes.Count;

                foreach (byte cell in map.Cells)
                    context.Require(cell < biomes, "Smoothing must keep biome indices valid.");

                if (passes == 0)
                {
                    context.Require(WorldGenBenchArgs.Hash(map.Cells) == WorldGenBenchArgs.Hash(pristine.Cells),
                        "Zero passes must leave the input untouched.");
                }

                context.Print("smoothed", WorldGenBenchArgs.Hash(map.Cells));
            }
        };
    }

    private static WorldGenBenchOp Regions()
    {
        BiomeMapGenerator generator = null;
        Action<BiomeMap> remove = null;
        BiomeMap pristine = null;
        BiomeMap map = null;

        return new WorldGenBenchOp("bio.regions", context => remove(map))
        {
            Setup = context =>
            {
                generator = new BiomeMapGenerator(context.Profile.Config, context.Profile.Biomes);
                remove = WorldGenBenchReflect.Bind<Action<BiomeMap>>(generator, "RemoveSmallRegions");

                pristine = context.Args.Text("shape", "islands") == "uniform"
                    ? Uniform(context)
                    : Islands(context);

                map = new BiomeMap(pristine.Resolution, pristine.WorldSize);
                context.Count("cells", map.Cells.Length);
            },
            Prepare = context => Array.Copy(pristine.Cells, map.Cells, map.Cells.Length),
            Verify = context =>
            {
                var seen = new HashSet<byte>();

                foreach (byte cell in map.Cells)
                    seen.Add(cell);

                context.Require(seen.Count > 0, "Region cleanup must not empty the map.");
                context.Count("distinctBiomes", seen.Count);
                context.Print("regions", WorldGenBenchArgs.Hash(map.Cells));

                var again = new BiomeMap(map.Resolution, map.WorldSize);
                Array.Copy(map.Cells, again.Cells, map.Cells.Length);
                remove(again);
                context.Count("secondPassChanged", Changed(map.Cells, again.Cells));
            }
        };
    }

    private static WorldGenBenchOp Weights()
    {
        BiomeMap map = null;
        float radius = 28f;
        BiomeWeightField field = null;

        return new WorldGenBenchOp("bio.weights", context =>
        {
            field = new BiomeWeightField(map, context.Profile.Biomes.Count, radius);
        })
        {
            Setup = context =>
            {
                map = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();
                radius = context.Args.Float("radius", context.Profile.Config.BiomeBlendRadius);
                context.Count("radius", radius);
                context.Count("cells", map.Cells.Length);
            },
            Verify = context =>
            {
                int biomes = context.Profile.Biomes.Count;
                var sample = new float[biomes];
                double worst = 0d;

                for (int step = 0; step <= 64; step++)
                {
                    float u = step / 64f;

                    for (int inner = 0; inner <= 64; inner++)
                    {
                        float v = inner / 64f;
                        field.Sample(u, v, sample);
                        double sum = 0d;

                        foreach (float weight in sample)
                        {
                            context.Require(!float.IsNaN(weight) && weight >= -1e-5f, "Biome weights must be finite and non negative.");
                            sum += weight;
                        }

                        worst = Math.Max(worst, Math.Abs(sum - 1d));
                    }
                }

                context.Count("worstSumError", worst);
                context.Require(worst < 1e-3, "Biome weights must sum to one within the stated epsilon.");

                double coverage = 0d;

                for (int index = 0; index < biomes; index++)
                {
                    double share = field.Coverage(index);
                    coverage += share;
                    context.Count("coverage" + index, share);
                }

                context.Count("coverageSum", coverage);
            }
        };
    }

    private static WorldGenBenchOp Sample()
    {
        BiomeWeightField field = null;
        float[] points = null;
        float[] scratch = null;
        int count = 100000;

        return new WorldGenBenchOp("bio.sample", context =>
        {
            double total = 0d;

            for (int index = 0; index < count; index++)
            {
                field.Sample(points[index * 2], points[index * 2 + 1], scratch);
                total += scratch[0];
            }

            context.Count("checksum", total);
        })
        {
            Setup = context =>
            {
                count = context.Args.Int("points", 100000);
                field = WorldGenBenchFixtures.Frozen(context.Profile, "weights").Weights;
                scratch = new float[context.Profile.Biomes.Count];
                points = new float[count * 2];

                var random = new Random(9871u);

                for (int index = 0; index < count; index++)
                {
                    points[index * 2] = random.NextFloat();
                    points[index * 2 + 1] = random.NextFloat();
                }

                context.Count("points", count);
            },
            Verify = context =>
            {
                double worst = 0d;

                for (int index = 0; index < 4096; index++)
                {
                    float u = points[index * 2];
                    float v = points[index * 2 + 1];
                    field.Sample(u, v, scratch);

                    for (int biome = 0; biome < scratch.Length; biome++)
                        worst = Math.Max(worst, Math.Abs(scratch[biome] - field.SampleOne(u, v, biome)));
                }

                context.Count("sampleAgreement", worst);
                context.Require(worst < 1e-5, "Sample and SampleOne must agree.");
            }
        };
    }

    private static WorldGenBenchOp Native()
    {
        BiomeWeightField field = null;
        NativeArray<float> copy = default;

        return new WorldGenBenchOp("bio.native", context =>
        {
            if (copy.IsCreated)
                copy.Dispose();

            copy = field.ToNativeArray(Allocator.Persistent);
        })
        {
            Setup = context =>
            {
                field = WorldGenBenchFixtures.Frozen(context.Profile, "weights").Weights;
                long bytes = (long)field.Resolution * field.Resolution * field.BiomeCount * sizeof(float);
                context.Count("bytes", bytes);
            },
            Verify = context =>
            {
                context.Require(copy.IsCreated, "The native copy must exist.");
                context.Require(copy.Length == field.Resolution * field.Resolution * field.BiomeCount,
                    "The native copy must hold every weight.");

                var scratch = new float[field.BiomeCount];
                field.Sample(0.5f, 0.5f, scratch);
                context.Count("length", copy.Length);
            },
            Teardown = context =>
            {
                if (copy.IsCreated)
                    copy.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Edges()
    {
        return new WorldGenBenchOp("bio.edges", context =>
        {
            WorldGenerationConfig config = context.Profile.Config;
            BiomeMap map = new BiomeMapGenerator(config, context.Profile.Biomes).Generate();
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                WorldGenBenchProfile.Assign(config, "Seed", context.Args.Int("seed", 0));
                WorldGenBenchProfile.Assign(config, "SeedJitter", context.Args.Float("jitter", 1f));
                WorldGenBenchProfile.Assign(config, "WarpStrength", context.Args.Float("warp", 0.9f));
            },
            Verify = context =>
            {
                var map = context.Get<BiomeMap>("map");
                int biomes = context.Profile.Biomes.Count;

                foreach (byte cell in map.Cells)
                    context.Require(cell < biomes, "Extreme settings must not produce an out of range biome.");

                context.Count("cells", map.Cells.Length);
                context.Print("edgeCells", WorldGenBenchArgs.Hash(map.Cells));
            }
        };
    }

    private static BiomeMap Uniform(WorldGenBenchContext context)
    {
        WorldGenerationConfig config = context.Profile.Config;
        var map = new BiomeMap(config.BiomeMapResolution, config.WorldSize);

        for (int index = 0; index < map.Cells.Length; index++)
            map.Cells[index] = 0;

        return map;
    }

    private static BiomeMap Islands(WorldGenBenchContext context)
    {
        WorldGenerationConfig config = context.Profile.Config;
        var map = new BiomeMap(config.BiomeMapResolution, config.WorldSize);
        var random = new Random(4242u);
        int biomes = Mathf.Max(1, context.Profile.Biomes.Count);

        for (int index = 0; index < map.Cells.Length; index++)
            map.Cells[index] = (byte)random.NextInt(biomes);

        return map;
    }

    private static int Changed(byte[] first, byte[] second)
    {
        int changed = 0;

        for (int index = 0; index < first.Length; index++)
        {
            if (first[index] != second[index])
                changed++;
        }

        return changed;
    }
}
