using System;
using System.Collections.Generic;
using UnityEngine;

public static class WorldBuildBudget
{
    public static void Validate(WorldBuildSettings settings, WorldGenerationConfig config, Vector2 center, bool colliders)
    {
        if (config.WorldSize < 256 || config.HeightCellSize < 1 || config.BiomeCellSize < 1
            || config.MaxHeight <= 0f || settings.Voxels.VoxelSize < 0.25f || settings.Voxels.ChunkSize < 8
            || settings.LodCount < 1 || settings.LodCount > 6 || settings.NearDistance < 32f
            || settings.ControlResolution < 256 || settings.ControlResolution > 4096)
            throw new InvalidOperationException("World, map and voxel sizes must be within their supported ranges.");

        long heightResolution = (long)config.WorldSize / config.HeightCellSize + 1;

        if (heightResolution > 16384 || (long)config.WorldSize / config.BiomeCellSize > 16384)
            throw new InvalidOperationException("Generated maps cannot exceed 16384 samples per side. Increase the map cell size.");

        var plan = new VoxelStreamPlan(settings.Voxels, settings.LodCount, settings.NearDistance, config.WorldSize);
        var columns = new List<VoxelColumnKey>();
        plan.Around(center, columns);
        var budget = new VoxelBudget();

        foreach (VoxelColumnKey key in columns)
        {
            float size = plan.ChunkMetres(key.Lod);
            budget += VoxelBudget.Estimate(plan.VoxelSize(key.Lod), size * size, colliders);
        }

        if (budget.Megabytes > settings.MemoryBudget)
            throw new InvalidOperationException($"World geometry requires about {budget.Megabytes:0} MB; its budget is {settings.MemoryBudget} MB. Reduce the detail radius or raise the geometry budget.");
    }
}
