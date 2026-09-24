using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

public static class WorldMapPipelineChecks
{
    public static void Run(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois, string problemDirectory)
    {
        Set(config, nameof(config.WorldSize), 512);
        Set(config, nameof(config.HeightCellSize), 4);
        Set(config, nameof(config.BiomeCellSize), 8);
        Set(config, nameof(config.Seed), 1337);
        Set(config, nameof(config.TileSize), 64f);
        Profile(config.CityProfile, 0, 1, 1);
        Profile(config.TownProfile, 1, 2, 3);
        Profile(config.CountryTownProfile, 1, 1, 2);
        Profile(config.GhostTownProfile, 1, 1, 2);
        Set(config, nameof(config.SettlementGap), 40f);
        Set(config, nameof(config.HubEdgeMargin), 16f);
        Set(config, nameof(config.MaxHubRelief), 360f);
        Set(config, nameof(config.MaxTileRelief), 360f);
        Set(config, nameof(config.SiteBuildableShare), 0f);
        Set(config, nameof(config.GatewayApproachLength), 40f);
        Set(config, nameof(config.HighwaySettlementClearance), 12f);
        Set(config, nameof(config.HighwayMinCurveRadius), 30f);
        Set(config, nameof(config.RoadPointSpacing), 8f);
        Set(config, nameof(config.CourtDepth), 20f);
        Set(config, nameof(config.DirtSettlementClearance), 40f);
        Set(config, nameof(config.DirtSpacing), 80f);
        Set(config, nameof(config.DirtMinLength), 20f);
        Set(config, nameof(config.DirtMaxLength), 50f);
        Set(config, nameof(config.LoopSpacing), 100f);
        Set(config, nameof(config.MaxLinkLength), 600f);
        Set(config, nameof(config.ErosionPasses), 1);
        Set(config, nameof(config.HydraulicPasses), 2);
        Set(config, nameof(config.ContinentAmplitude), 0.01f);
        Set(config, nameof(config.ReliefScale), 0.2f);
        Set(config, nameof(config.SeaLevel), 0f);
        StampLoader.Set(config.Water, "RiverStartArea", 0.02f);

        foreach (BiomeDefinition biome in biomes.Biomes)
        {
            Set(biome, nameof(biome.BaseHeight), 0.3f);
            Set(biome, nameof(biome.HillAmplitude), 0.02f);
            Set(biome, nameof(biome.RidgeAmplitude), 0.001f);
            Set(biome, nameof(biome.DuneAmplitude), 0f);
            Set(biome, nameof(biome.DetailAmplitude), 0.001f);
        }

        var pipeline = new WorldMapPipeline(config, biomes, pois);
        var progress = new List<float>();
        WorldMapResult first = pipeline.Generate((stage, fraction) => progress.Add(fraction));
        WorldMapResult second = pipeline.Generate();

        Require(progress.Count == 8 && progress.SequenceEqual(progress.OrderBy(value => value)), "All stages must report increasing progress.");
        Require(first.Biomes.Cells.SequenceEqual(second.Biomes.Cells), "Repeated generation must preserve biome cells.");
        Require(first.Heights.Heights.SequenceEqual(second.Heights.Heights), "Repeated generation must not carve the previous terrain again.");
        Require(first.RoadMask.SequenceEqual(second.RoadMask), "Repeated generation must preserve the road mask.");
        Require(first.Placements.Count == second.Placements.Count && first.Placements.Count > 0, "The single pipeline must generate POI.");
        Require(first.Roads.Hubs.Count > 1 && first.Roads.Roads.Exists(road => road.Kind == RoadKind.Highway), "The single pipeline must generate settlements and highways.");
        Require(first.Settlements.Exists(settlement => settlement.Tiles.Count > 0) && first.Roads.Streets.Count > 0, "Settlements must be laid out in tiles with streets.");
        Require(first.Roads.Streets.Count == second.Roads.Streets.Count && first.Roads.Roads.Count == second.Roads.Roads.Count, "Repeated generation must lay out the same roads and streets.");
        Require(first.Heights.Resolution == config.HeightMapResolution, "The final map must match the saved resolution.");
        Require(first.Heights.Heights.All(value => !float.IsNaN(value) && value >= 0f && value <= 1f), "Heights must remain finite and normalized.");

        for (int index = 0; index < first.Placements.Count; index++)
        {
            PoiPlacement expected = first.Placements[index];
            PoiPlacement actual = second.Placements[index];
            Require(expected.Prefab == actual.Prefab && expected.Position.x == actual.Position.x
                && expected.Position.y == actual.Position.y && expected.Position.z == actual.Position.z
                && expected.Rotation == actual.Rotation, "POI transforms must be deterministic.");
        }


        CheckWater(config, first, second);
        RoadNetworkReport report = RoadNetworkDiagnostics.Measure(config, first.Roads, first.Settlements, first.Placements, null, null);

        if (report.Hard > 0 && problemDirectory != null)
        {
            Directory.CreateDirectory(problemDirectory);

            for (int index = 0; index < Math.Min(8, report.Problems.Count); index++)
            {
                (Vector2 point, string kind) = report.Problems[index];
                Draw.View(Path.Combine(problemDirectory, $"smoke_{index}_{kind}.png"), first.Heights, first.Roads, first.Settlements, first.Placements, point, 160f, 800, false, report);
            }
        }

        Require(report.Hard == 0, "The pipeline road topology must have no hard violations:\n" + report.ToText()
            + string.Join("\n", report.Problems.Take(12).Select(problem => $"  problem {problem.Kind} at ({problem.Point.x:F1}, {problem.Point.y:F1})")));

        using var cancellation = new CancellationTokenSource();
        int visited = 0;

        try
        {
            pipeline.Generate((stage, fraction) =>
            {
                if (++visited == 2)
                    cancellation.Cancel();
            }, cancellation.Token);
            throw new InvalidOperationException("Cancellation did not stop the pipeline.");
        }
        catch (OperationCanceledException)
        {
            Require(visited == 2, "No later stage may run after cancellation.");
        }

        WorldMapResult afterCancel = pipeline.Generate();
        Require(afterCancel.Heights.Heights.SequenceEqual(first.Heights.Heights), "Cancelled work must not corrupt the next run.");
        Set(config, nameof(config.Seed), 424243);
        WorldMapResult otherSeed = pipeline.Generate();
        Require(!otherSeed.Heights.Heights.SequenceEqual(first.Heights.Heights), "Changing the seed must change the world.");
        CheckBudgets();
        Console.WriteLine($"PASS: unified pipeline, deterministic regeneration, cancellation and seed changes; {first.Placements.Count} POI, {first.Roads.Roads.Count} regional roads, {first.Roads.Streets.Count} streets, 0 hard topology violations.");
    }

    private static void CheckWater(WorldGenerationConfig config, WorldMapResult first, WorldMapResult second)
    {
        WaterMap water = first.Water;

        Require(water != null && second.Water != null, "The pipeline must publish a water map.");
        Require(water.ToBytes().SequenceEqual(second.Water.ToBytes()), "Repeated generation must produce the same water.");

        int flooded = 0;

        foreach (PoiPlacement placement in first.Placements)
        {
            float ground = first.Heights.SampleWorldSmooth(placement.Position.x, placement.Position.z);

            if (water.IsWater(placement.Position.x, placement.Position.z, ground))
                flooded++;
        }

        Require(flooded == 0, $"{flooded} POI stand in water.");

        int wetTiles = 0;

        foreach (SettlementLayout layout in first.Settlements)
        {
            foreach (SettlementTile tile in layout.Tiles)
            {
                Vector2 center = tile.Center;
                float ground = first.Heights.SampleWorldSmooth(center.x, center.y);

                if (water.IsWater(center.x, center.y, ground))
                    wetTiles++;
            }
        }

        Require(wetTiles == 0, $"{wetTiles} settlement tiles stand in water.");

        int samples = 0, drowned = 0;

        foreach (Road road in first.Roads.Roads)
        {
            if (road.Kind != RoadKind.Highway)
                continue;

            for (int i = 0; i + 1 < road.Points.Length; i++)
            {
                for (float t = 0f; t < 1f; t += 0.25f)
                {
                    Vector2 point = Vector2.Lerp(road.Points[i], road.Points[i + 1], t);
                    WaterSample sample = water.Sample(point.x, point.y);
                    float ground = first.Heights.SampleWorldSmooth(point.x, point.y);

                    samples++;

                    if (sample.IsWater && sample.Kind != WaterKind.River && ground < sample.Surface)
                        drowned++;
                }
            }
        }

        float exposure = samples == 0 ? 0f : drowned / (float)samples;

        Require(exposure <= config.MaxWaterExposure, $"Highways run through lakes or sea for {exposure:P1} of their length, over MaxWaterExposure {config.MaxWaterExposure:P0}.");

        Console.WriteLine($"  water: {water.Rivers.Count} rivers, {water.Bodies.Count} lakes and ponds, {water.Crossings.Count} road crossings, "
            + $"0 POI and 0 tiles in water, highways in standing water {exposure:P1}");
    }

    private static void Profile(SettlementTypeProfile profile, int count, int minTiles, int maxTiles)
    {
        Set(profile, nameof(profile.Count), count);
        Set(profile, nameof(profile.MinTiles), minTiles);
        Set(profile, nameof(profile.MaxTiles), maxTiles);
        Set(profile, nameof(profile.Spacing), 60f);
    }

    private static void CheckBudgets()
    {
        var settings = new WorldBuildSettings();
        var config = new WorldGenerationConfig();
        Set(settings, nameof(settings.Voxels), new VoxelConfig());
        WorldBuildBudget.Validate(settings, config, settings.PreviewCenter, true);
        Set(settings, nameof(settings.LodCount), 1);
        Set(settings, nameof(settings.MemoryBudget), 16);
        ExpectRejected(() => WorldBuildBudget.Validate(settings, config, settings.PreviewCenter, true));
        Set(config, nameof(config.WorldSize), 1000000);
        ExpectRejected(() => WorldBuildBudget.Validate(settings, config, settings.PreviewCenter, true));
    }

    private static void ExpectRejected(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("An unsafe world build was accepted.");
    }

    private static void Set(object target, string property, object value)
    {
        target.GetType().GetProperty(property).SetValue(target, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
