using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsHgt
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["hgt.generate"] = Generate;
        registry["hgt.noise"] = Noise;
        registry["hgt.hydraulic"] = Hydraulic;
        registry["hgt.hydraulicStages"] = HydraulicStages;
        registry["hgt.thermal"] = Thermal;
        registry["hgt.passes"] = Passes;
        registry["hgt.sample"] = Sample;
        registry["hgt.path"] = Path;
        registry["hgt.scaling"] = Scaling;
        registry["hgt.extreme"] = Extreme;
    }

    private static WorldGenBenchOp Generate()
    {
        BiomeMap biomes = null;

        return new WorldGenBenchOp("hgt.generate", context =>
        {
            HeightMap map = new HeightMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate(biomes);
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                biomes = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();
                context.Count("resolution", context.Profile.Config.HeightMapResolution);
            },
            Verify = context => Inspect(context, context.Get<HeightMap>("map"), "heights")
        };
    }

    private static WorldGenBenchOp Noise()
    {
        BiomeMap biomes = null;

        return new WorldGenBenchOp("hgt.noise", context =>
        {
            HeightMap map = new HeightMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate(biomes);
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                WorldGenBenchProfile.Assign(context.Profile.Config, "ErosionPasses", 0);
                WorldGenBenchProfile.Assign(context.Profile.Config, "HydraulicPasses", 0);
                biomes = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();
                context.Count("cells", (long)context.Profile.Config.HeightMapResolution * context.Profile.Config.HeightMapResolution);
            },
            Verify = context => Inspect(context, context.Get<HeightMap>("map"), "noise")
        };
    }

    private static WorldGenBenchOp Hydraulic()
    {
        return Erosion("hgt.hydraulic", true);
    }

    private static WorldGenBenchOp Thermal()
    {
        return Erosion("hgt.thermal", false);
    }

    private static WorldGenBenchOp Erosion(string id, bool hydraulic)
    {
        HeightMap pristine = null;
        NativeArray<float> heights = default;
        int passes = 0;

        return new WorldGenBenchOp(id, context =>
        {
            if (hydraulic)
                HydraulicErosion.Run(context.Profile.Config, heights, pristine.Resolution);
            else
                HeightMapErosion.Run(context.Profile.Config, heights, pristine.Resolution);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                passes = context.Args.Int("passes", hydraulic ? config.HydraulicPasses : config.ErosionPasses);
                WorldGenBenchProfile.Assign(config, hydraulic ? "HydraulicPasses" : "ErosionPasses", passes);

                if (hydraulic && context.Args.Has("coarse"))
                    WorldGenBenchProfile.Assign(config, "HydraulicCellSize", context.Args.Float("coarse", config.HydraulicCellSize));

                pristine = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                heights = new NativeArray<float>(pristine.Heights.Length, Allocator.Persistent);
                context.Count("passes", passes);
                context.Count("cells", pristine.Heights.Length);
            },
            Prepare = context => heights.CopyFrom(pristine.Heights),
            Verify = context =>
            {
                var map = new HeightMap(pristine.Resolution, pristine.WorldSize, pristine.MaxHeight);
                heights.CopyTo(map.Heights);
                Inspect(context, map, "eroded");

                if (passes != 0)
                    return;

                context.Require(WorldGenBenchArgs.Hash(map.Heights) == WorldGenBenchArgs.Hash(pristine.Heights),
                    "Zero passes must leave the height map untouched.");
            },
            Teardown = context =>
            {
                if (heights.IsCreated)
                    heights.Dispose();
            }
        };
    }

    private static WorldGenBenchOp HydraulicStages()
    {
        HeightMap pristine = null;
        NativeArray<float> heights = default;

        return new WorldGenBenchOp("hgt.hydraulicStages", context =>
        {
            HydraulicErosion.Run(context.Profile.Config, heights, pristine.Resolution);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                WorldGenBenchProfile.Assign(config, "HydraulicCellSize", context.Args.Float("coarse", config.HydraulicCellSize));
                WorldGenBenchProfile.Assign(config, "HydraulicPasses", context.Args.Int("passes", config.HydraulicPasses));

                pristine = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                heights = new NativeArray<float>(pristine.Heights.Length, Allocator.Persistent);

                float cellSize = (float)config.WorldSize / (pristine.Resolution - 1);
                int step = Mathf.Max(1, Mathf.RoundToInt(config.HydraulicCellSize / cellSize));
                int coarse = Mathf.Max(4, (pristine.Resolution - 1) / step + 1);

                context.Count("coarseResolution", coarse);
                context.Count("coarseCells", (long)coarse * coarse);
                context.Count("step", step);
                context.Count("bufferBytes", (long)coarse * coarse * sizeof(float) * 4);
            },
            Prepare = context => heights.CopyFrom(pristine.Heights),
            Verify = context =>
            {
                var map = new HeightMap(pristine.Resolution, pristine.WorldSize, pristine.MaxHeight);
                heights.CopyTo(map.Heights);
                Inspect(context, map, "hydraulic");
            },
            Teardown = context =>
            {
                if (heights.IsCreated)
                    heights.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Passes()
    {
        HeightMap pristine = null;
        NativeArray<float> heights = default;
        string first = null;

        return new WorldGenBenchOp("hgt.passes", context =>
        {
            HydraulicErosion.Run(context.Profile.Config, heights, pristine.Resolution);
            HeightMapErosion.Run(context.Profile.Config, heights, pristine.Resolution);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                WorldGenBenchProfile.Assign(config, "ErosionPasses", context.Args.Int("thermal", config.ErosionPasses));
                WorldGenBenchProfile.Assign(config, "HydraulicPasses", context.Args.Int("hydraulic", config.HydraulicPasses));

                pristine = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                heights = new NativeArray<float>(pristine.Heights.Length, Allocator.Persistent);
                context.Count("thermalPasses", config.ErosionPasses);
                context.Count("hydraulicPasses", config.HydraulicPasses);
            },
            Prepare = context => heights.CopyFrom(pristine.Heights),
            Verify = context =>
            {
                var map = new HeightMap(pristine.Resolution, pristine.WorldSize, pristine.MaxHeight);
                heights.CopyTo(map.Heights);
                string hash = WorldGenBenchArgs.Hash(map.Heights);
                context.Print("passes", hash);
                context.Require(first == null || first == hash, "Repeating erosion from a pristine input must give one result.");
                first = hash;
                Inspect(context, map, "passes");
            },
            Teardown = context =>
            {
                if (heights.IsCreated)
                    heights.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Sample()
    {
        HeightMap map = null;
        float[] points = null;
        int count = 200000;
        bool smooth = true;

        return new WorldGenBenchOp("hgt.sample", context =>
        {
            double total = 0d;

            if (smooth)
            {
                for (int index = 0; index < count; index++)
                    total += map.SampleWorldSmooth(points[index * 2], points[index * 2 + 1]);
            }
            else
            {
                for (int index = 0; index < count; index++)
                    total += map.SampleWorld(new Vector3(points[index * 2], 0f, points[index * 2 + 1]));
            }

            context.Count("checksum", total);
        })
        {
            Setup = context =>
            {
                count = context.Args.Int("points", 200000);
                smooth = context.Args.Bool("smooth", true);
                map = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                points = new float[count * 2];

                var random = new Random(5150u);

                for (int index = 0; index < count; index++)
                {
                    points[index * 2] = random.NextFloat(0f, map.WorldSize);
                    points[index * 2 + 1] = random.NextFloat(0f, map.WorldSize);
                }

                context.Count("points", count);
            }
        };
    }

    private static WorldGenBenchOp Path()
    {
        BiomeMap biomes = null;

        return new WorldGenBenchOp("hgt.path", context =>
        {
            HeightMap map = new HeightMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate(biomes);
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                biomes = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();
                WorldGenerationConfig config = context.Profile.Config;
                long cells = (long)config.HeightMapResolution * config.HeightMapResolution;

                context.Count("heightBytes", cells * sizeof(float));
                context.Count("weightBytes", (long)config.BiomeMapResolution * config.BiomeMapResolution * context.Profile.Biomes.Count * sizeof(float));
                context.Count("thermalTempBytes", config.ErosionPasses > 0 ? cells * sizeof(float) * 3 : 0);
            },
            Verify = context =>
            {
                Inspect(context, context.Get<HeightMap>("map"), "path");
                context.Note("managed heap after the run: " + GC.GetTotalMemory(false) + " bytes, "
                    + "a reading rather than a work count, so it never makes two runs non comparable");
            }
        };
    }

    private static WorldGenBenchOp Scaling()
    {
        BiomeMap biomes = null;

        return new WorldGenBenchOp("hgt.scaling", context =>
        {
            HeightMap map = new HeightMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate(biomes);
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                int world = context.Args.Int("world", config.WorldSize);
                int cell = context.Args.Int("cell", config.HeightCellSize);

                WorldGenBenchProfile.Assign(config, "WorldSize", world);
                WorldGenBenchProfile.Assign(config, "HeightCellSize", cell);
                WorldGenBenchProfile.Assign(config, "ErosionPasses", context.Args.Int("thermal", 0));
                WorldGenBenchProfile.Assign(config, "HydraulicPasses", context.Args.Int("hydraulic", 0));

                biomes = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();

                context.Count("worldSize", world);
                context.Count("heightCellSize", cell);
                context.Count("resolution", config.HeightMapResolution);
                context.Count("cells", (long)config.HeightMapResolution * config.HeightMapResolution);
            },
            Verify = context => Inspect(context, context.Get<HeightMap>("map"), "scaling")
        };
    }

    private static WorldGenBenchOp Extreme()
    {
        BiomeMap biomes = null;

        return new WorldGenBenchOp("hgt.extreme", context =>
        {
            HeightMap map = new HeightMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate(biomes);
            context.Put("map", map);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                string shape = context.Args.Text("shape", "ridge");

                WorldGenBenchProfile.Assign(config, "ContinentOctaves", 8);
                WorldGenBenchProfile.Assign(config, "HillOctaves", 8);
                WorldGenBenchProfile.Assign(config, "RidgeOctaves", 8);
                WorldGenBenchProfile.Assign(config, "DuneOctaves", 8);
                WorldGenBenchProfile.Assign(config, "DetailOctaves", 8);

                if (shape == "plane")
                {
                    WorldGenBenchProfile.Assign(config, "ReliefScale", 0f);
                    WorldGenBenchProfile.Assign(config, "ContinentAmplitude", 0f);
                }

                if (shape == "sea")
                    WorldGenBenchProfile.Assign(config, "SeaLevel", config.MaxHeight * 0.5f);

                biomes = WorldGenBenchFixtures.Frozen(context.Profile, "biomes").CopyBiomes();
                context.Count("octaves", 8);
            },
            Verify = context => Inspect(context, context.Get<HeightMap>("map"), "extreme")
        };
    }

    public static void Inspect(WorldGenBenchContext context, HeightMap map, string label)
    {
        context.Require(map != null, "The height map must exist.");
        context.Require(map.Resolution == context.Profile.Config.HeightMapResolution,
            "The height map must match the configured resolution.");

        float low = float.MaxValue;
        float high = float.MinValue;
        double sum = 0d;

        foreach (float value in map.Heights)
        {
            context.Require(!float.IsNaN(value) && value >= 0f && value <= 1f, "Heights must be finite and normalised.");
            low = Mathf.Min(low, value);
            high = Mathf.Max(high, value);
            sum += value;
        }

        context.Count("minHeight", low);
        context.Count("maxHeight", high);
        context.Count("meanHeight", sum / map.Heights.Length);
        context.Count("rangeMetres", (high - low) * map.MaxHeight);
        context.Print(label, WorldGenBenchArgs.Hash(map.Heights));
    }
}
