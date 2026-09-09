using UnityEngine;

public static class WorldGenBenchBudget
{
    public const string ALLOW_ARGUMENT = "allowFullPreview";

    public static void RequireFullPreview(WorldGenBenchContext context, WorldGenBenchFixture fixture)
    {
        if (context.Args.Bool(ALLOW_ARGUMENT, false))
            return;

        WorldGenerationConfig config = fixture.World.Config;
        VoxelConfig voxels = fixture.Settings.Voxels;

        var plan = new VoxelStreamPlan(voxels, fixture.Settings.LodCount, fixture.Settings.NearDistance, config.WorldSize);
        var columns = new System.Collections.Generic.List<VoxelColumnKey>();
        plan.Around(fixture.Settings.PreviewCenter, columns);

        var estimate = new VoxelBudget();

        foreach (VoxelColumnKey key in columns)
        {
            float metres = plan.ChunkMetres(key.Lod);
            estimate += VoxelBudget.Estimate(plan.VoxelSize(key.Lod), metres * metres, fixture.Settings.PreviewColliders);
        }

        context.Count("previewColumns", columns.Count);
        context.Count("previewMegabytes", estimate.Megabytes);
        context.Count("memoryBudget", fixture.Settings.MemoryBudget);

        throw new WorldGenBenchBlockedException("blocked_resource",
            $"A whole world preview of {config.WorldSize} m is estimated at {estimate.Describe()} over {columns.Count} columns, "
            + $"against a {fixture.Settings.MemoryBudget} MiB geometry budget, and the maps and decor are on top of that. "
            + $"Pass {ALLOW_ARGUMENT}=true to run it deliberately rather than let a batch suite trip over it.");
    }
}
