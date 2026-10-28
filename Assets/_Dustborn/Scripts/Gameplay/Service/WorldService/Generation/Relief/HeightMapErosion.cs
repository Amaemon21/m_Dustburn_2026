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
        int tiles = (resolution + ErosionPassJob.TILE - 1) / ErosionPassJob.TILE;
        int tileCount = tiles * tiles;

        var spare = new NativeArray<float>(heights.Length, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var active = new NativeArray<byte>(tileCount, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var changed = new NativeArray<byte>(tileCount, GenerationJobs.ALLOCATOR, NativeArrayOptions.UninitializedMemory);
        var count = new NativeArray<int>(1, GenerationJobs.ALLOCATOR);

        try
        {
            GenerationJobs.Run(new ErosionActiveJob { Changed = changed, Active = active, Count = count, Tiles = tiles, All = true });

            NativeArray<float> source = heights;
            NativeArray<float> target = spare;

            bool swapped = false;
            long visited = 0;
            int passes = 0;

            for (int pass = 0; pass < config.ErosionPasses && count[0] > 0; pass++)
            {
                visited += count[0];

                GenerationJobs.Run(new ErosionPassJob
                {
                    Heights = source,
                    Active = active,
                    Target = target,
                    Changed = changed,
                    Resolution = resolution,
                    Tiles = tiles,
                    Talus = talus,
                    Strength = config.ErosionStrength
                }, tileCount, 16);

                (source, target) = (target, source);

                swapped = !swapped;
                passes++;

                GenerationJobs.Run(new ErosionActiveJob { Changed = changed, Active = active, Count = count, Tiles = tiles });
            }

            if (swapped)
                spare.CopyTo(heights);

            Debug.Log($"Thermal erosion: {passes} of {config.ErosionPasses} passes, {visited * 100f / ((long)tileCount * config.ErosionPasses):0.#}% of tile passes computed, the rest unchanged since the pass before");
        }
        finally
        {
            spare.Dispose();
            active.Dispose();
            changed.Dispose();
            count.Dispose();
        }
    }
}
