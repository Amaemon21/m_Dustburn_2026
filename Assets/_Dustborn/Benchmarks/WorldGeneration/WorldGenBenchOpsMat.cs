using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldGenBenchOpsMat
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["mat.painter"] = Painter;
        registry["mat.surface"] = Surface;
        registry["mat.bakeWorld"] = BakeWorld;
        registry["mat.pack"] = Pack;
        registry["mat.material"] = Material;
        registry["mat.repetitionless"] = Repetitionless;
        registry["mat.oneGround"] = OneGround;
        registry["mat.rebake"] = Rebake;
    }

    private static GroundSplatPainter NewPainter(WorldGenBenchContext context, WorldGenBenchFrozen frozen)
    {
        BiomeWeightField weights = frozen.Weights ?? new BiomeWeightField(frozen.Biomes,
            context.Profile.Biomes.Count, context.Profile.Config.BiomeBlendRadius);

        List<Road> roads = context.Args.Bool("noRoads", false) ? null : frozen.Roads?.Paved();

        return new GroundSplatPainter(context.Profile.Config, context.Profile.Biomes, weights, roads);
    }

    private static WorldGenBenchOp Painter()
    {
        WorldGenBenchFrozen frozen = null;
        GroundSplatPainter painter = null;

        return new WorldGenBenchOp("mat.painter", context =>
        {
            painter?.Dispose();
            painter = NewPainter(context, frozen);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                context.Count("maxLayers", context.Profile.Config.MaxTerrainLayers);
            },
            Verify = context =>
            {
                context.Require(painter.HasLayers, "The painter must register at least one layer.");
                context.Count("layers", painter.Layers.Length);
                context.Count("controlTextures", VoxelSplatBaker.ControlCount(painter.Layers.Length));

                var names = new System.Text.StringBuilder();

                foreach (TerrainLayer layer in painter.Layers)
                    names.Append(layer == null ? "null" : layer.name).Append(';');

                context.Print("layers", WorldGenBenchArgs.Hash(names.ToString()));
            },
            Teardown = context => painter?.Dispose()
        };
    }

    private static WorldGenBenchOp Surface()
    {
        WorldGenBenchFrozen frozen = null;
        int resolution = 2048;

        return new WorldGenBenchOp("mat.surface", context =>
        {
            VoxelSplatBaker.Surface(context.Profile.Config, frozen.CarvedHeights ?? frozen.RawHeights, resolution,
                out float[] steepness, out float[] height);

            context.Put("steepness", steepness);
            context.Put("height", height);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                resolution = context.Args.Int("resolution", 1024);
                context.Count("resolution", resolution);
                context.Count("bytes", (long)resolution * resolution * sizeof(float) * 2);
            },
            Verify = context =>
            {
                var steepness = (float[])context.State["steepness"];
                var height = (float[])context.State["height"];

                double slope = 0d;
                int cliffs = 0;

                foreach (float value in steepness)
                {
                    context.Require(!float.IsNaN(value) && value >= 0f && value <= 90f, "Slope must be a finite angle.");
                    slope += value;

                    if (value > context.Profile.Config.CliffSlopeStart)
                        cliffs++;
                }

                foreach (float value in height)
                    context.Require(!float.IsNaN(value), "Normalised height must be finite.");

                context.Count("meanSlope", slope / steepness.Length);
                context.Count("cliffFraction", cliffs / (double)steepness.Length);
            }
        };
    }

    private static WorldGenBenchOp BakeWorld()
    {
        WorldGenBenchFrozen frozen = null;
        GroundSplatPainter painter = null;
        float[] steepness = null;
        float[] height = null;
        int resolution = 1024;
        NativeArray<float> weights = default;

        return new WorldGenBenchOp("mat.bakeWorld", context =>
        {
            if (weights.IsCreated)
                weights.Dispose();

            weights = painter.BakeWorld(resolution, steepness, height);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                resolution = context.Args.Int("resolution", 1024);
                painter = NewPainter(context, frozen);
                VoxelSplatBaker.Surface(context.Profile.Config, frozen.CarvedHeights, resolution, out steepness, out height);

                context.Count("resolution", resolution);
                context.Count("layers", painter.Layers.Length);
                context.Count("outputBytes", (long)resolution * resolution * painter.Layers.Length * sizeof(float));
            },
            Verify = context =>
            {
                context.Require(weights.IsCreated, "The splat buffer must exist.");
                int layers = painter.Layers.Length;
                double worst = 0d;

                for (int cell = 0; cell < resolution * resolution; cell += 97)
                {
                    double sum = 0d;

                    for (int layer = 0; layer < layers; layer++)
                    {
                        float weight = weights[cell * layers + layer];
                        context.Require(!float.IsNaN(weight) && weight >= -1e-4f, "Splat weights must be finite and non negative.");
                        sum += weight;
                    }

                    worst = Math.Max(worst, Math.Abs(sum - 1d));
                }

                context.Count("worstSumError", worst);
                context.Require(worst < 1e-2, "Splat weights must normalise to one.");
            },
            Teardown = context =>
            {
                if (weights.IsCreated)
                    weights.Dispose();

                painter?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Pack()
    {
        WorldGenBenchFrozen frozen = null;
        GroundSplatPainter painter = null;
        Texture2D[] textures = null;
        int resolution = 512;

        return new WorldGenBenchOp("mat.pack", context =>
        {
            Release(textures);
            textures = VoxelSplatBaker.Bake(context.Profile.Config, painter, frozen.CarvedHeights, resolution);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                resolution = context.Args.Int("resolution", 512);
                painter = NewPainter(context, frozen);
                context.Count("resolution", resolution);
                context.Count("layers", painter.Layers.Length);
            },
            Verify = context =>
            {
                context.Require(textures != null && textures.Length == VoxelSplatBaker.ControlCount(painter.Layers.Length),
                    "The pack must produce one control texture per four layers.");

                context.Count("controlTextures", textures.Length);
                context.Count("textureBytes", (long)textures.Length * resolution * resolution * 4);
            },
            Teardown = context =>
            {
                Release(textures);
                painter?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp OneGround()
    {
        WorldGenBenchFrozen frozen = null;
        GroundSplatPainter painter = null;

        return new WorldGenBenchOp("mat.oneGround", context =>
        {
            painter?.Dispose();
            painter = NewPainter(context, frozen);
        })
        {
            Setup = context =>
            {
                WorldGenBenchProfile.Assign(context.Profile.Config, "OneGroundPerBiome", context.Args.Bool("oneGround", true));

                if (context.Args.Bool("noRoads", false))
                    context.Note("road network deliberately absent");

                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");

                context.Count("oneGround", context.Profile.Config.OneGroundPerBiome ? 1 : 0);
            },
            Verify = context =>
            {
                context.Count("layers", painter.Layers.Length);
                context.Require(painter.HasLayers, "Even the reduced mode must register layers.");
            },
            Teardown = context => painter?.Dispose()
        };
    }

    private static WorldGenBenchOp Material()
    {
#if UNITY_EDITOR
        WorldGenBenchFrozen frozen = null;
        VoxelGroundMaterial.Request request = null;

        return new WorldGenBenchOp("mat.material", context =>
        {
            string result = VoxelGroundMaterial.Bake(request, null);
            context.Put("result", result);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

                if (fixture == null || !fixture.HasBake)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "The ground material needs a valid bake.");

                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");

                if (fixture.World.Material == null)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "The bake has no ground material to copy.");

                string path = WorldGenBenchPaths.Temporary("BenchMaterial.mat");
                WorldGenBenchPaths.Ensure();

                var material = new Material(fixture.World.Material) { name = "BenchMaterial" };
                UnityEditor.AssetDatabase.CreateAsset(material, path);

                request = new VoxelGroundMaterial.Request
                {
                    Config = fixture.World.Config,
                    Biomes = fixture.World.Biomes,
                    BiomeMap = fixture.World.BiomeMap,
                    Roads = fixture.World.Roads == null ? null : fixture.World.Roads.Paved(),
                    Map = frozen.CarvedHeights,
                    ControlResolution = context.Args.Int("resolution", 512),
                    UseRepetitionless = context.Args.Bool("repetitionless", false),
                    MaterialPath = path,
                    Material = material
                };

                context.Count("controlResolution", request.ControlResolution);
                context.Note("baked onto a copy of the project material inside " + WorldGenBenchPaths.Directory);
            },
            Verify = context =>
            {
                var result = (string)context.State["result"];
                context.Note(result);
                context.Require(request.Material != null, "The bake must produce a material.");
            },
            Teardown = context => WorldGenBenchPaths.Clean()
        };
#else
        return new WorldGenBenchOp("mat.material", context =>
        {
            throw new WorldGenBenchBlockedException("blocked_dependency", "Ground material baking is editor only.");
        });
#endif
    }

    private static WorldGenBenchOp Repetitionless()
    {
#if UNITY_EDITOR
        WorldGenBenchFrozen frozen = null;
        VoxelGroundMaterial.Request request = null;

        return new WorldGenBenchOp("mat.repetitionless", context =>
        {
            string result = VoxelGroundMaterial.Bake(request, null);
            context.Put("result", result);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

                if (fixture == null || !fixture.HasBake)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "Repetitionless baking needs a valid bake.");

                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");

                request = new VoxelGroundMaterial.Request
                {
                    Config = fixture.World.Config,
                    Biomes = fixture.World.Biomes,
                    BiomeMap = fixture.World.BiomeMap,
                    Roads = fixture.World.Roads == null ? null : fixture.World.Roads.Paved(),
                    Map = frozen.CarvedHeights,
                    ControlResolution = context.Args.Int("resolution", 256),
                    UseRepetitionless = true,
                    MaterialPath = WorldGenBenchPaths.Temporary("BenchRepetitionless.mat")
                };
            },
            Verify = context =>
            {
                context.Note((string)context.State["result"]);
                context.Require(request.Material != null, "Repetitionless setup must produce a material.");
            },
            Teardown = context =>
            {
                request.Material = null;
                context.Note("the temporary material is left for the next run to clear: the package keeps a reference to it "
                    + "and deleting it here raises a MissingReferenceException after the test");
            }
        };
#else
        return new WorldGenBenchOp("mat.repetitionless", context =>
        {
            throw new WorldGenBenchBlockedException("blocked_dependency", "Repetitionless setup is editor only.");
        });
#endif
    }

    private static WorldGenBenchOp Rebake()
    {
        WorldGenBenchFrozen frozen = null;
        GroundSplatPainter painter = null;
        Texture2D[] textures = null;
        int resolution = 256;

        return new WorldGenBenchOp("mat.rebake", context =>
        {
            Release(textures);
            resolution = resolution == 256 ? 512 : 256;
            textures = VoxelSplatBaker.Bake(context.Profile.Config, painter, frozen.CarvedHeights, resolution);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                painter = NewPainter(context, frozen);
            },
            Verify = context =>
            {
                context.Count("resolution", resolution);
                context.Count("controlTextures", textures.Length);
                context.Count("liveTextures", Resources.FindObjectsOfTypeAll<Texture2D>().Length);
            },
            Teardown = context =>
            {
                Release(textures);
                painter?.Dispose();
            }
        };
    }

    private static void Release(Texture2D[] textures)
    {
        if (textures == null)
            return;

        foreach (Texture2D texture in textures)
        {
            if (texture == null)
                continue;

            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
        }
    }
}
