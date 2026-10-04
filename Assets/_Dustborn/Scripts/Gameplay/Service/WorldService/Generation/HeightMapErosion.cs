using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static class HeightMapErosion
{
    public static void Run(WorldGenerationConfig config, NativeArray<float> heights, int resolution)
    {
        if (config.ErosionPasses <= 0)
            return;

        float cellSize = (float)config.WorldSize / (resolution - 1);
        float talus = Mathf.Tan(config.ErosionTalusAngle * Mathf.Deg2Rad) * cellSize / config.MaxHeight;

        var spare = new NativeArray<float>(heights.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            NativeArray<float> source = heights;
            NativeArray<float> target = spare;

            bool swapped = false;

            for (int pass = 0; pass < config.ErosionPasses; pass++)
            {
                new ErosionPassJob
                {
                    Heights = source,
                    Target = target,
                    Resolution = resolution,
                    Talus = talus,
                    Strength = config.ErosionStrength
                }.Schedule(resolution, 1).Complete();

                (source, target) = (target, source);

                swapped = !swapped;
            }

            if (swapped)
                spare.CopyTo(heights);
        }
        finally
        {
            spare.Dispose();
        }
    }
}
