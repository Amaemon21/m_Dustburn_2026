using System;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;

public sealed class MainMenuTerrainMaps : IDisposable
{
    public NativeArray<float> Heights { get; private set; }
    public int Resolution { get; }

    public MainMenuTerrainMaps(MainMenuTerrainLayout layout, int resolution)
    {
        Resolution = resolution;
        Heights = new NativeArray<float>(resolution * resolution, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        try
        {
            var job = new MainMenuTerrainHeightJob { Layout = layout, Resolution = resolution, Heights = Heights };
            Complete(job.Schedule(resolution, 4), "Building hills, road and house site with Burst", 0.08f);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public MainMenuTerrainMaps(float[,] heights)
    {
        Resolution = heights.GetLength(0);
        if (Resolution != heights.GetLength(1))
            throw new ArgumentException("Terrain heightmap must be square.", nameof(heights));
        var flat = new float[heights.Length];
        Buffer.BlockCopy(heights, 0, flat, 0, flat.Length * sizeof(float));
        Heights = new NativeArray<float>(flat, Allocator.Persistent);
    }

    public float[,] CopyHeights()
    {
        var heights = new float[Resolution, Resolution];
        Buffer.BlockCopy(Heights.ToArray(), 0, heights, 0, Heights.Length * sizeof(float));
        return heights;
    }

    public float[,,] Paint(MainMenuTerrainLayout layout, int resolution)
    {
        using var weights = new NativeArray<float>(resolution * resolution * MainMenuTerrainLayout.LAYER_COUNT, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var job = new MainMenuTerrainPaintJob
        {
            Layout = layout,
            Resolution = resolution,
            HeightResolution = Resolution,
            Heights = Heights,
            Weights = weights
        };
        Complete(job.Schedule(resolution, 4), "Painting forest, burnt ground and road with Burst", 0.24f);
        var splat = new float[resolution, resolution, MainMenuTerrainLayout.LAYER_COUNT];
        Buffer.BlockCopy(weights.ToArray(), 0, splat, 0, weights.Length * sizeof(float));
        return splat;
    }

    public void Dispose()
    {
        if (Heights.IsCreated)
            Heights.Dispose();
        Heights = default;
    }

    public static void Complete(JobHandle handle, string message, float progress)
    {
        try
        {
            JobHandle.ScheduleBatchedJobs();
            MainMenuTerrainGenerator.Progress(message, progress);
            while (!handle.IsCompleted)
            {
                Thread.Sleep(10);
                MainMenuTerrainGenerator.Progress(message, progress);
            }
        }
        finally
        {
            handle.Complete();
        }
    }
}
