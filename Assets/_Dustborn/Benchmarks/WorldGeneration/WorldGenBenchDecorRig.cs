using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class WorldGenBenchDecorRig : IDisposable
{
    public VoxelDensityField Field { get; }

    public VoxelDecorPlacer Placer { get; }

    public DecorFilter Filter { get; }

    public BiomeWeightField Weights { get; }

    public HeightMap Heights { get; }

    public float[] RoadMask { get; }

    public IReadOnlyList<PoiPlacement> Placements { get; }

    public WorldGenBenchDecorRig(WorldGenBenchContext context, float grassScale)
    {
        WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements");

        Heights = frozen.FinalHeights ?? frozen.CarvedHeights ?? frozen.RawHeights;
        RoadMask = frozen.RoadMask;
        Placements = frozen.Placements;

        Weights = new BiomeWeightField(frozen.Biomes, context.Profile.Biomes.Count, context.Profile.Config.BiomeBlendRadius);

        Field = new VoxelDensityField(Heights, context.Profile.Voxels, WorldGenBenchOpsVox.Lowest(Heights));

        Filter = new DecorFilter(context.Profile.Config, Weights, context.Profile.Biomes.Count,
            RoadMask, Heights.Resolution, Placements, Widest(context.Profile.Biomes));

        Placer = new VoxelDecorPlacer(context.Profile.Config, context.Profile.Biomes, Field, Filter, grassScale);
    }

    public static float Widest(BiomeDatabase biomes)
    {
        float widest = 0f;

        foreach (BiomeDefinition definition in biomes.Biomes)
        {
            if (definition == null)
                continue;

            foreach (ScatterLayer layer in definition.Trees)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.Footprint);

            foreach (ScatterLayer layer in definition.Rocks)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.Footprint);
        }

        return widest;
    }

    public void Dispose()
    {
        Field?.Dispose();
    }
}
