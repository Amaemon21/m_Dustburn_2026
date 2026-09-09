using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldGenBenchOpsPre
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["pre.environment"] = Environment;
        registry["pre.fixture"] = Fixture;
        registry["pre.selftest"] = SelfTest;
        registry["pre.overhead"] = Overhead;
        registry["pre.budget"] = Budget;
        registry["life.cancel"] = Cancel;
        registry["life.invalid"] = Invalid;
    }

    private static WorldGenBenchOp Environment()
    {
        return new WorldGenBenchOp("pre.environment", context =>
        {
            context.Count("processorCount", SystemInfo.processorCount);
            context.Count("systemMemoryMB", SystemInfo.systemMemorySize);
        })
        {
            Verify = context =>
            {
                context.Note("unity " + Application.unityVersion);
                context.Note("platform " + Application.platform);
                context.Note("graphics " + SystemInfo.graphicsDeviceType);
                context.Note("cpu " + SystemInfo.processorType);
                context.Note("burst " + (Unity.Burst.BurstCompiler.IsEnabled ? "enabled" : "disabled"));

                context.Require(Application.unityVersion.StartsWith("6000.3", StringComparison.Ordinal),
                    "The benchmark expects the Unity 6.3 line recorded in ProjectVersion.txt.");

                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();
                context.Require(fixture != null, "The benchmark fixture asset must exist.");
                context.Require(fixture.HasSources, "The fixture must reference the real config, biome, POI and voxel assets: " + fixture.Describe());
                context.Note(fixture.Describe());
                context.Count("bakePresent", fixture.HasBake ? 1 : 0);
                context.Count("biomes", fixture.Biomes.Count);
                context.Count("pois", fixture.Pois.Count);
            }
        };
    }

    private static WorldGenBenchOp Fixture()
    {
        return new WorldGenBenchOp("pre.fixture", context =>
        {
            WorldGenBenchProfile clone = WorldGenBenchProfile.Create(context.Profile.Id, context.Args);
            context.Put("clone", clone);
        })
        {
            Verify = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();
                var clone = context.Get<WorldGenBenchProfile>("clone");

                context.Require(!ReferenceEquals(clone.Config, fixture.Config), "The profile must clone the source config.");
                context.Require(!ReferenceEquals(clone.Biomes, fixture.Biomes), "The profile must clone the biome database.");
                context.Require(clone.Config.Seed == context.Profile.Config.Seed, "Two clones of one profile must agree.");
                context.Require(fixture.Config.WorldSize != 0, "The source config must stay readable after cloning.");

                context.Print("profile", clone.Fingerprint);
                context.Count("configWorldSize", clone.Config.WorldSize);
                context.Count("sourceWorldSize", fixture.Config.WorldSize);
                clone.Release();
            }
        };
    }

    private static WorldGenBenchOp SelfTest()
    {
        return new WorldGenBenchOp("pre.selftest", context =>
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapTotal))
            {
            }
        })
        {
            Verify = context =>
            {
                var values = new List<double>();

                for (int index = 1; index <= 100; index++)
                    values.Add(index);

                WorldGenBenchStats stats = WorldGenBenchStats.Of(values);
                context.Require(Math.Abs(stats.P50 - 50.5) < 1e-6, "The median of one to a hundred must be 50.5.");
                context.Require(Math.Abs(stats.Min - 1d) < 1e-9 && Math.Abs(stats.Max - 100d) < 1e-9, "Extremes must be exact.");
                context.Require(Math.Abs(stats.Mean - 50.5) < 1e-9, "The mean must be exact.");

                var single = new List<double> { 7d };
                context.Require(Math.Abs(WorldGenBenchStats.Of(single).P95 - 7d) < 1e-9, "A single sample must not interpolate.");

                WorldGenProbe.BeginRun(true);

                using (WorldGenProbe.Measure(WorldGenStage.DecorStep))
                    Thread.Sleep(12);

                WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Requested, 1, 42L, 1);
                WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Scheduled, 1, 42L, 1);
                WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Observed, 1, 42L, 0);
                WorldGenProbe.Mark(WorldGenMilestone.FirstTerrain);

                double hitch = WorldGenProbe.ToMs(WorldGenProbe.Stat(WorldGenStage.DecorStep).MaxTicks);
                int spans = WorldGenProbe.SpanCount;
                int queue = WorldGenProbe.QueueCount;
                bool overflow = WorldGenProbe.Overflow;
                double milestone = WorldGenProbe.MilestoneMs(WorldGenMilestone.FirstTerrain);
                double missing = WorldGenProbe.MilestoneMs(WorldGenMilestone.WorldReady);
                WorldGenProbe.EndRun();

                context.Count("syntheticHitchMs", hitch);
                context.Require(hitch >= 10d, "A synthetic twelve millisecond hitch must be recorded.");
                context.Require(spans == 1, "A capture must record exactly one span for one measured scope.");
                context.Require(queue == 3, "Three queue events must be recorded without duplicates.");
                context.Require(!overflow, "A three event capture must not overflow.");
                context.Require(!double.IsNaN(milestone), "A reached milestone must report a time.");
                context.Require(double.IsNaN(missing), "An unreached milestone must report null rather than zero.");

                WorldGenProbe.BeginRun(false);

                using (WorldGenProbe.Measure(WorldGenStage.DecorStep))
                {
                }

                context.Require(WorldGenProbe.SpanCount == 0, "Capture off must record no events.");
                context.Require(WorldGenProbe.Stat(WorldGenStage.DecorStep).Count == 1, "Aggregate counters must run even with capture off.");
                WorldGenProbe.EndRun();

                context.Count("timerFrequency", System.Diagnostics.Stopwatch.Frequency);
                context.Count("highResolution", System.Diagnostics.Stopwatch.IsHighResolution ? 1 : 0);
            }
        };
    }

    private static WorldGenBenchOp Overhead()
    {
        BiomeMap map = null;
        bool capture = false;

        return new WorldGenBenchOp("pre.overhead", context =>
        {
            WorldGenProbe.BeginRun(capture);
            map = new BiomeMapGenerator(context.Profile.Config, context.Profile.Biomes).Generate();
            WorldGenProbe.EndRun();
        })
        {
            Setup = context =>
            {
                capture = context.Args.Bool("capture", false);
                context.Count("capture", capture ? 1 : 0);
            },
            Verify = context =>
            {
                context.Print("output", WorldGenBenchArgs.Hash(map.Cells));
                context.Count("cells", map.Cells.Length);
                context.Note("compare this fingerprint between capture on and off: the output must not move");
            }
        };
    }

    private static WorldGenBenchOp Budget()
    {
        return new WorldGenBenchOp("pre.budget", context =>
        {
            WorldBuildBudget.Validate(context.Profile.Settings, context.Profile.Config,
                context.Profile.Settings.PreviewCenter, true);
        })
        {
            Verify = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                WorldBuildSettings settings = context.Profile.Settings;

                VoxelBudget estimate = VoxelBudget.Estimate(context.Profile.Voxels.VoxelSize,
                    (float)config.WorldSize * config.WorldSize, true);

                context.Count("geometryMegabytes", estimate.Megabytes);
                context.Count("memoryBudget", settings.MemoryBudget);

                long heightBytes = (long)config.HeightMapResolution * config.HeightMapResolution * sizeof(float);
                long weightBytes = (long)config.BiomeMapResolution * config.BiomeMapResolution * context.Profile.Biomes.Count * sizeof(float);
                long splatBytes = (long)settings.ControlResolution * settings.ControlResolution * config.MaxTerrainLayers * sizeof(float);

                context.Count("heightBytes", heightBytes);
                context.Count("weightBytes", weightBytes);
                context.Count("splatBufferBytes", splatBytes);
                context.Count("workingSetMegabytes", (heightBytes * 3 + weightBytes * 2 + splatBytes) / 1048576.0 + estimate.Megabytes);

                WorldGenerationConfig huge = Object.Instantiate(config);
                huge.hideFlags = HideFlags.HideAndDontSave;
                WorldGenBenchProfile.Assign(huge, "WorldSize", 1000000);

                long start = WorldGenProbe.Now;
                bool refused = false;

                try
                {
                    WorldBuildBudget.Validate(settings, huge, settings.PreviewCenter, true);
                }
                catch (InvalidOperationException exception)
                {
                    refused = true;
                    context.Note("refused: " + exception.Message);
                }

                context.Count("refusalMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));
                Object.DestroyImmediate(huge);

                context.Require(refused, "An impossible world size must be refused before any build starts.");
            }
        };
    }

    private static WorldGenBenchOp Cancel()
    {
        WorldMapPipeline pipeline = null;
        int stopAt = 2;

        return new WorldGenBenchOp("life.cancel", context =>
        {
            using var cancellation = new CancellationTokenSource();
            int visited = 0;
            long requested = 0L;
            long observed = 0L;

            try
            {
                pipeline.Generate((stage, fraction) =>
                {
                    if (++visited != stopAt)
                        return;

                    requested = WorldGenProbe.Now;
                    cancellation.Cancel();
                }, cancellation.Token);

                context.Put("outcome", "not cancelled");
            }
            catch (OperationCanceledException)
            {
                observed = WorldGenProbe.Now;
                context.Put("outcome", "cancelled");
            }

            context.Count("visitedStages", visited);
            context.Count("cancelLatencyMs", observed == 0L ? double.NaN : WorldGenProbe.ToMs(observed - requested));
        })
        {
            Setup = context =>
            {
                stopAt = context.Args.Int("stopAt", 2);
                pipeline = new WorldMapPipeline(context.Profile.Config, context.Profile.Biomes, context.Profile.Pois);
                context.Count("stopAt", stopAt);
            },
            Verify = context =>
            {
                context.Require((string)context.State["outcome"] == "cancelled", "Cancellation must stop the pipeline.");
                context.Require(context.Work["visitedStages"] == stopAt, "No later stage may run after cancellation.");

                WorldMapResult after = pipeline.Generate();
                context.Require(after.Placements != null, "A cancelled run must not corrupt the next one.");
                context.Print("afterCancel", WorldGenBenchArgs.Hash(after.Heights.Heights));

                double longest = 0d;

                for (int index = 0; index <= (int)WorldGenStage.MapApplyHeights; index++)
                {
                    WorldGenStageStat stat = WorldGenProbe.Stat((WorldGenStage)index);

                    if (stat.Count > 0)
                        longest = Math.Max(longest, WorldGenProbe.ToMs(stat.MaxTicks));
                }

                context.Count("longestUncancellableSpanMs", longest);
            }
        };
    }

    private static WorldGenBenchOp Invalid()
    {
        return new WorldGenBenchOp("life.invalid", context =>
        {
            var host = new GameObject("WorldGenBench Invalid") { hideFlags = HideFlags.HideAndDontSave };

            try
            {
                bool refused = false;

                try
                {
                    var loader = new WorldRuntimeLoader(context.Profile.Settings, null, host.transform, host.transform);
                    loader.Dispose();
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }

                context.Count("missingBakeRefused", refused ? 1 : 0);

                refused = false;

                try
                {
                    WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();
                    var loader = new WorldRuntimeLoader(context.Profile.Settings, fixture.World, null, host.transform);
                    loader.Dispose();
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }

                context.Count("missingViewerRefused", refused ? 1 : 0);
                context.Count("leftoverChildren", host.transform.childCount);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        })
        {
            Verify = context =>
            {
                context.Require(context.Work["missingBakeRefused"] == 1d, "Loading without a bake must be refused.");
                context.Require(context.Work["missingViewerRefused"] == 1d, "Loading without a viewer must be refused.");
                context.Require(context.Work["leftoverChildren"] == 0d, "A refused load must leave no partial objects.");
            }
        };
    }
}
