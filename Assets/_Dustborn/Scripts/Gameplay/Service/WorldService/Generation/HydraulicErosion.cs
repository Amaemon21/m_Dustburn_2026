using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static class HydraulicErosion
{
    public static void Run(WorldGenerationConfig config, NativeArray<float> heights, int resolution)
    {
        if (config.HydraulicPasses <= 0 || config.HydraulicStrength <= 0f)
            return;

        float cellSize = (float)config.WorldSize / (resolution - 1);
        int step = Mathf.Max(1, Mathf.RoundToInt(config.HydraulicCellSize / cellSize));
        int coarse = Mathf.Max(4, (resolution - 1) / step + 1);

        int cells = coarse * coarse;

        var terrain = new NativeArray<float>(cells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var outflow = new NativeArray<float>(cells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var flow = new NativeArray<float>(cells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var spare = new NativeArray<float>(cells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            new HydraulicSampleJob
            {
                Heights = heights,
                Coarse = terrain,
                Resolution = resolution,
                CoarseResolution = coarse,
                Step = step
            }.Schedule(coarse, 1).Complete();

            new HydraulicOutflowJob
            {
                Coarse = terrain,
                Outflow = outflow,
                Resolution = coarse
            }.Schedule(coarse, 1).Complete();

            for (int i = 0; i < cells; i++)
                flow[i] = 1f;

            NativeArray<float> source = flow;
            NativeArray<float> target = spare;

            for (int pass = 0; pass < config.HydraulicPasses; pass++)
            {
                new HydraulicFlowJob
                {
                    Coarse = terrain,
                    Outflow = outflow,
                    Flow = source,
                    Target = target,
                    Resolution = coarse
                }.Schedule(coarse, 1).Complete();

                (source, target) = (target, source);
            }

            new HydraulicCarveJob
            {
                Coarse = terrain,
                Flow = source,
                Cut = target,
                Resolution = coarse,
                CellSize = cellSize * step,
                MaxHeight = config.MaxHeight,
                Strength = config.HydraulicStrength,
                FlowExponent = config.HydraulicFlowExponent,
                FlowReference = Mathf.Max(1f, config.HydraulicPasses),
                MaxCut = config.MaxHydraulicCut
            }.Schedule(coarse, 1).Complete();

            new HydraulicApplyJob
            {
                Cut = target,
                Heights = heights,
                Resolution = resolution,
                CoarseResolution = coarse,
                Step = step
            }.Schedule(resolution, 1).Complete();
        }
        finally
        {
            terrain.Dispose();
            outflow.Dispose();
            flow.Dispose();
            spare.Dispose();
        }
    }
}
