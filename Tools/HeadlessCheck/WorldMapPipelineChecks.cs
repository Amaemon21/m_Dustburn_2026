using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public static class WorldMapPipelineChecks
{
    public static void Run(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois)
    {
        Set(config, nameof(config.WorldSize), 512);
        Set(config, nameof(config.HeightCellSize), 4);
        Set(config, nameof(config.BiomeCellSize), 8);
        Set(config, nameof(config.Seed), 1337);
        Set(config, nameof(config.HubCount), 4);
        Set(config, nameof(config.CityCount), 0);
        Set(config, nameof(config.TownCount), 4);
        Set(config, nameof(config.HubEdgeMargin), 100f);
        Set(config, nameof(config.MinHubDistance), 96f);
        Set(config, nameof(config.MinHubRadius), 64f);
        Set(config, nameof(config.MaxHubRadius), 80f);
        Set(config, nameof(config.MaxHubRelief), 360f);
        Set(config, nameof(config.ErosionPasses), 1);
        Set(config, nameof(config.HydraulicPasses), 2);
        Set(config, nameof(config.ContinentAmplitude), 0.01f);
        Set(config, nameof(config.ReliefScale), 0.2f);
        Set(config, nameof(config.SeaLevel), 0f);

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

        Require(progress.Count == 7 && progress.SequenceEqual(progress.OrderBy(value => value)), "All stages must report increasing progress.");
        Require(first.Biomes.Cells.SequenceEqual(second.Biomes.Cells), "Repeated generation must preserve biome cells.");
        Require(first.Heights.Heights.SequenceEqual(second.Heights.Heights), "Repeated generation must not carve the previous terrain again.");
        Require(first.RoadMask.SequenceEqual(second.RoadMask), "Repeated generation must preserve the road mask.");
        Require(first.Placements.Count == second.Placements.Count && first.Placements.Count > 0, "The single pipeline must generate POI.");
        Require(first.Roads.Hubs.Count > 1 && first.Roads.Roads.Count > 0, "The single pipeline must generate settlements and roads.");
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
        Console.WriteLine($"PASS: unified pipeline, deterministic regeneration, cancellation and seed changes; {first.Placements.Count} POI, {first.Roads.Roads.Count} roads.");
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
