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

        var terrain = new NativeArray<float>(cells, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var outflow = new NativeArray<float>(cells, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var flow = new NativeArray<float>(cells, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var spare = new NativeArray<float>(cells, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);

        try
        {
            GenerationJobs.Run(new HydraulicSampleJob
            {
                Heights = heights,
                Coarse = terrain,
                Resolution = resolution,
                CoarseResolution = coarse,
                Step = step
            }, coarse, 1);

            GenerationJobs.Run(new HydraulicOutflowJob
            {
                Coarse = terrain,
                Outflow = outflow,
                Resolution = coarse
            }, coarse, 1);

            for (int i = 0; i < cells; i++)
                flow[i] = 1f;

            NativeArray<float> source = flow;
            NativeArray<float> target = spare;

            for (int pass = 0; pass < config.HydraulicPasses; pass++)
            {
                GenerationJobs.Run(new HydraulicFlowJob
                {
                    Coarse = terrain,
                    Outflow = outflow,
                    Flow = source,
                    Target = target,
                    Resolution = coarse
                }, coarse, 1);

                (source, target) = (target, source);
            }

            StreamPower(source, Mathf.Max(1f, config.HydraulicPasses), config.HydraulicFlowExponent);

            GenerationJobs.Run(new HydraulicCarveJob
            {
                Coarse = terrain,
                Water = source,
                Cut = target,
                Resolution = coarse,
                CellSize = cellSize * step,
                MaxHeight = config.MaxHeight,
                Strength = config.HydraulicStrength,
                MaxCut = config.MaxHydraulicCut
            }, coarse, 1);

            GenerationJobs.Run(new HydraulicApplyJob
            {
                Cut = target,
                Heights = heights,
                Resolution = resolution,
                CoarseResolution = coarse,
                Step = step
            }, resolution, 1);
        }
        finally
        {
            terrain.Dispose();
            outflow.Dispose();
            flow.Dispose();
            spare.Dispose();
        }
    }

    private static void StreamPower(NativeArray<float> flow, float reference, float exponent)
    {
        for (int i = 0; i < flow.Length; i++)
            flow[i] = Mathf.Pow(Mathf.Max(1f, flow[i]) / reference, exponent);
    }
}
