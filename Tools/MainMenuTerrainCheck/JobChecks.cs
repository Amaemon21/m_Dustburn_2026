using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

internal static class JobChecks
{
    public static void Run()
    {
        Require(!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<MainMenuTerrainLayout>(), "Layout has no managed references for Burst");
        CheckSampler();
        CheckMaps();
        CheckRecolor();
        Benchmark();
        Console.WriteLine("PASS: job buffers, terrain gradients, paint packing, grass exclusion, row-order determinism and grass recoloring.");
    }

    private static void CheckSampler()
    {
        const int RESOLUTION = 17;
        var heights = new NativeArray<float>(RESOLUTION * RESOLUTION, Allocator.Persistent);
        try
        {
            for (int y = 0; y < RESOLUTION; y++)
            for (int x = 0; x < RESOLUTION; x++)
                heights[y * RESOLUTION + x] = (3f + x * 0.2f + y * 0.4f) / MainMenuTerrainLayout.HEIGHT_RANGE;
            var sampler = new MainMenuHeightSampler { Heights = heights, Resolution = RESOLUTION, Size = 16f };
            float expected = math.degrees(math.atan(math.sqrt(0.2f * 0.2f + 0.4f * 0.4f)));
            for (int y = 0; y <= 16; y++)
            for (int x = 0; x <= 16; x++)
                Near(sampler.Slope(x / 16f, y / 16f), expected, 0.0002f, "Plane slope including map edges");
        }
        finally
        {
            heights.Dispose();
        }
    }

    private static void CheckMaps()
    {
        const int HEIGHT_RESOLUTION = 257;
        const int RESOLUTION = 64;
        var layout = new MainMenuTerrainLayout(2026, 256f, 36f, 6f, new float2(0f, -10f), new float2(0.3f, 1f));
        float2 restoredOrigin = layout.Origin + new float2(17f, -9f);
        MainMenuTerrainLayout restored = layout.WithOrigin(restoredOrigin);
        Near(math.lengthsq(restored.World(0f, 0f) - restoredOrigin), 0f, 0f, "Restored paint follows the selected terrain origin");
        Near(math.lengthsq(restored.FromLocal(restored.HouseCenter) - layout.FromLocal(layout.HouseCenter)), 0f, 0f, "Restoring paint keeps the composition camera frame");
        using var heights = new NativeArray<float>(HEIGHT_RESOLUTION * HEIGHT_RESOLUTION, Allocator.Persistent);
        var heightJob = new MainMenuTerrainHeightJob { Layout = layout, Resolution = HEIGHT_RESOLUTION, Heights = heights };
        for (int row = HEIGHT_RESOLUTION - 1; row >= 0; row--)
            heightJob.Execute(row);
        for (int y = 0; y < HEIGHT_RESOLUTION; y += 7)
        for (int x = 0; x < HEIGHT_RESOLUTION; x += 7)
            Near(heights[y * HEIGHT_RESOLUTION + x] * MainMenuTerrainLayout.HEIGHT_RANGE, layout.Height(layout.World(x / 256f, y / 256f)), 0.00001f, "Height job indexing");

        using var weights = new NativeArray<float>(RESOLUTION * RESOLUTION * MainMenuTerrainLayout.LAYER_COUNT, Allocator.Persistent);
        var paint = new MainMenuTerrainPaintJob { Layout = layout, Resolution = RESOLUTION, HeightResolution = HEIGHT_RESOLUTION, Heights = heights, Weights = weights };
        for (int row = RESOLUTION - 1; row >= 0; row--)
            paint.Execute(row);
        var sampler = new MainMenuHeightSampler { Heights = heights, Resolution = HEIGHT_RESOLUTION, Size = layout.Size };
        for (int y = 0; y < RESOLUTION; y++)
        for (int x = 0; x < RESOLUTION; x++)
        {
            float u = x / 63f;
            float v = y / 63f;
            MainMenuTerrainWeights expected = layout.Paint(layout.World(u, v), sampler.Slope(u, v));
            for (int layer = 0; layer < MainMenuTerrainLayout.LAYER_COUNT; layer++)
                Near(weights[(y * RESOLUTION + x) * MainMenuTerrainLayout.LAYER_COUNT + layer], expected[layer], 0f, "Paint job interleaving");
        }

        using var surface = new NativeArray<float3>(RESOLUTION * RESOLUTION, Allocator.Persistent);
        var surfaceJob = new MainMenuGrassSurfaceJob { Layout = layout, Resolution = RESOLUTION, HeightResolution = HEIGHT_RESOLUTION, Heights = heights, Surface = surface };
        for (int row = 0; row < RESOLUTION; row++)
            surfaceJob.Execute(row);
        using var settings = new NativeArray<MainMenuGrassSettings>(new[]
        {
            new MainMenuGrassSettings { MaxSlope = 32f, PatchFrequency = 24f, PatchThreshold = 0.4f, Density = 1.2f },
            new MainMenuGrassSettings { MaxSlope = 32f, PatchFrequency = 29f, PatchThreshold = 0.4f, Density = 0.6f, Burnt = 1 }
        }, Allocator.Persistent);
        using var first = new NativeArray<int>(RESOLUTION * RESOLUTION * 2, Allocator.Persistent);
        using var repeat = new NativeArray<int>(first.Length, Allocator.Persistent);
        using var counts = new NativeArray<int>(RESOLUTION * 2, Allocator.Persistent);
        var scatter = new MainMenuGrassScatterJob { Layout = layout, Resolution = RESOLUTION, Seed = 2026u, Density = 1.4f, Layers = settings, Surface = surface, Instances = first, RowCounts = counts };
        for (int row = 0; row < counts.Length; row++)
            scatter.Execute(row);
        int expectedCount = 0;
        for (int row = 0; row < counts.Length; row++)
            expectedCount += counts[row];
        scatter.Instances = repeat;
        for (int row = counts.Length - 1; row >= 0; row--)
            scatter.Execute(row);
        int count = 0;
        for (int i = 0; i < first.Length; i++)
        {
            Near(first[i], repeat[i], 0f, "Grass does not depend on execution order");
            Require(first[i] >= 0 && first[i] <= 16, "Grass count is within Terrain limits");
            if (surface[i % (RESOLUTION * RESOLUTION)].y == 0f)
                Near(first[i], 0f, 0f, "No grass in excluded cells");
            count += first[i];
        }
        Require(count > 0 && count == expectedCount, "Grass row count reduction");
        scatter.Density = 0f;
        for (int row = 0; row < counts.Length; row++)
            scatter.Execute(row);
        for (int i = 0; i < repeat.Length; i++)
            Near(repeat[i], 0f, 0f, "Zero grass density clears every cell");
    }

    private static void Benchmark()
    {
        var camera = new float2(0f, -10f);
        var forward = new float2(0.04175f, 1f);
        var reference = new ReferenceLayout(2026, 512f, 36f, 6f, camera, forward);
        var optimized = new MainMenuTerrainLayout(2026, 512f, 36f, 6f, camera, forward);
        var buffer = new float[257 * 257];
        Action before = () =>
        {
            for (int y = 0; y < 257; y++)
            for (int x = 0; x < 257; x++)
                buffer[y * 257 + x] = reference.Height(reference.World(x / 256f, y / 256f));
        };
        Action after = () =>
        {
            for (int y = 0; y < 257; y++)
            for (int x = 0; x < 257; x++)
                buffer[y * 257 + x] = optimized.Height(optimized.World(x / 256f, y / 256f));
        };
        double oldTime = Measure(before);
        double newTime = Measure(after);
        Console.WriteLine($"Managed height math, 257 x 257, median of 5: {oldTime:0.0} ms -> {newTime:0.0} ms ({oldTime / newTime:0.00}x). Burst and Unity asset work are excluded.");
        GC.KeepAlive(buffer);
    }

    private static void CheckRecolor()
    {
        using var source = new NativeArray<UnityEngine.Color>(new[]
        {
            new UnityEngine.Color(1f, 1f, 1f, 1f), new UnityEngine.Color(1f, 1f, 1f, 0.5f),
            new UnityEngine.Color(1f, 1f, 1f, 0f), new UnityEngine.Color(1f, 1f, 1f, 0.25f)
        }, Allocator.Persistent);
        using var mask = new NativeArray<UnityEngine.Color>(new[]
        {
            new UnityEngine.Color(1f, 0f, 0f, 1f), new UnityEngine.Color(0f, 1f, 0f, 1f),
            new UnityEngine.Color(0f, 0f, 1f, 1f), new UnityEngine.Color(0f, 0f, 0f, 1f)
        }, Allocator.Persistent);
        using var pixels = new NativeArray<UnityEngine.Color32>(4, Allocator.Persistent);
        var job = new MainMenuGrassRecolorJob
        {
            Width = 2,
            Height = 2,
            HasMask = true,
            Leaves = new float3(1f, 0f, 0f),
            Stems = new float3(0f, 1f, 0f),
            Flowers = new float3(0f, 0f, 1f),
            Tip = new float3(1f),
            TextureShading = 1f,
            Source = source,
            Mask = mask,
            Pixels = pixels
        };
        for (int i = 0; i < 4; i++)
            job.Execute(i);
        Require(pixels[0].r == 255 && pixels[0].g == 0 && pixels[0].b == 0 && pixels[0].a == 255, "Leaves mask");
        Require(pixels[1].r == 0 && pixels[1].g == 255 && pixels[1].b == 0 && pixels[1].a == 128, "Stems mask and alpha");
        Require(pixels[2].r == 0 && pixels[2].g == 0 && pixels[2].b == 255 && pixels[2].a == 0, "Flowers mask and alpha");
        Require(pixels[3].r == 255 && pixels[3].g == 0 && pixels[3].b == 0 && pixels[3].a == 64, "Unmasked pixel uses leaves");
        Near(MainMenuGrassRecolorJob.ToSrgb(new float3(0.003f)).x, 0.003f * 12.92f, 0.000001f, "Linear sRGB branch");
        Near(MainMenuGrassRecolorJob.ToSrgb(new float3(0.5f)).x, 0.735357f, 0.000001f, "Gamma sRGB branch");
        job.HasMask = false;
        job.Execute(1);
        Require(pixels[1].r == 255 && pixels[1].g == 0 && pixels[1].b == 0, "Missing mask uses leaves");
    }

    private static double Measure(Action action)
    {
        action();
        var values = new double[5];
        for (int i = 0; i < values.Length; i++)
        {
            var timer = Stopwatch.StartNew();
            action();
            values[i] = timer.Elapsed.TotalMilliseconds;
        }
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void Near(float actual, float expected, float tolerance, string message)
    {
        Require(math.isfinite(actual) && math.abs(actual - expected) <= tolerance, $"{message}: expected {expected}, got {actual}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
