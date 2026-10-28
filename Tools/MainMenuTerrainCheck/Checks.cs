using System;
using System.IO;
using Unity.Mathematics;

internal static class Checks
{
    private static int _checks;

    private static void Main(string[] args)
    {
        foreach (int size in new[] { 256, 512, 1024 })
        foreach (int seed in new[] { 2026, 1, -17 })
        foreach (float angle in new[] { 0f, 0.04175f, 0.7854f, 2.31f })
            CheckLayout(size, seed, angle);
        CheckDeterminism();
        CheckReference();
        JobChecks.Run();
        string output = args.Length > 0 ? args[0] : ".codex/MainMenuTerrainCheck";
        Directory.CreateDirectory(output);
        Preview(output);
        Console.WriteLine($"PASS: {_checks} assertions across 36 layouts. Preview: {Path.GetFullPath(output)}");
    }

    private static void CheckLayout(int size, int seed, float angle)
    {
        var layout = Create(size, seed, angle);
        float2 camera = layout.FromLocal(float2.zero);
        Near(layout.Height(camera), MainMenuTerrainLayout.BASE_HEIGHT, 0.001f, "Camera ground height");
        float nearMinimum = float.MaxValue;
        float nearMaximum = float.MinValue;
        var flat = new MainMenuTerrainLayout(seed, size, 36f, 6f, camera, new float2(math.sin(angle), math.cos(angle)), 0f);
        for (float z = 4f; z <= 12f; z += 1f)
        {
            float2 world = layout.FromLocal(new float2(-6f, z));
            float height = layout.Height(world);
            nearMinimum = math.min(nearMinimum, height);
            nearMaximum = math.max(nearMaximum, height);
            Near(flat.Height(world), MainMenuTerrainLayout.BASE_HEIGHT, 0.001f, "Surface relief can be disabled near camera");
        }
        Require(nearMaximum - nearMinimum > 0.05f, "Visible uneven ground near camera outside house site");
        float maximum = 0f;
        var visibleLayers = new bool[MainMenuTerrainLayout.LAYER_COUNT];
        var weights = new float[MainMenuTerrainLayout.LAYER_COUNT];
        for (int z = 0; z <= 64; z++)
        for (int x = 0; x <= 64; x++)
        {
            float2 world = layout.World(x / 64f, z / 64f);
            float height = layout.Height(world);
            Require(math.isfinite(height) && height > 0f && height < MainMenuTerrainLayout.HEIGHT_RANGE, "Finite heights inside terrain vertical range");
            maximum = math.max(maximum, height);
            layout.Paint(world, Slope(layout, world), weights);
            float sum = 0f;
            for (int layer = 0; layer < weights.Length; layer++)
            {
                float weight = weights[layer];
                Require(math.isfinite(weight) && weight >= 0f && weight <= 1f, "Valid layer weight");
                visibleLayers[layer] |= weight > 0.05f;
                sum += weight;
            }
            Near(sum, 1f, 0.0001f, "Normalized alphamap");
            Near(weights[8], 0f, 0f, "Dirt road leaves asphalt available for manual painting");
        }
        Require(maximum > MainMenuTerrainLayout.BASE_HEIGHT + 10f, "Visible edge hills");
        foreach (int layer in new[] { 0, 1, 2, 3, 5, 6, 7, 9 })
            Require(visibleLayers[layer], $"Visible variation for ground layer {layer}");

        for (int z = -12; z <= 12; z += 2)
        for (int x = -14; x <= 14; x += 2)
        {
            float2 world = layout.FromLocal(layout.HouseCenter + new float2(x, z));
            float2 uv = (world - layout.Origin) / size;
            Require(math.all(uv > 0f) && math.all(uv < 1f), "House remains inside terrain at any camera yaw");
            Near(layout.Height(world), MainMenuTerrainLayout.BASE_HEIGHT, 0.002f, "Flat house footprint");
            Near(layout.GrassWeight(world, 32f, 0f, false), 0f, 0f, "No forest grass under house");
            Near(layout.GrassWeight(world, 32f, 0f, true), 0f, 0f, "No burnt grass under house");
            Require(layout.RoadDistance(world) >= 0f, "Finite house distance from road");
        }

        float maxGrade = 0f;
        for (float z = -40f; z <= size * 0.6f; z += 1f)
        {
            float2 world = layout.FromLocal(new float2(layout.RoadX(z), z));
            Near(layout.RoadDistance(world), 0f, 0.001f, "Continuous road centreline");
            Near(layout.GrassWeight(world, 32f, 0f, false), 0f, 0f, "No grass on road");
            layout.Paint(world, Slope(layout, world), weights);
            Require(weights[9] > 0.99f, "Dirt covers road centreline");
            float2 next = layout.FromLocal(new float2(layout.RoadX(z + 1f), z + 1f));
            maxGrade = math.max(maxGrade, math.abs(layout.Height(next) - layout.Height(world)));
        }
        Require(maxGrade < 0.25f, $"Road profile has no abrupt steps: {maxGrade} at size {size}, seed {seed}, yaw {angle}");
        float2 drive = layout.FromLocal(new float2(5f, layout.HouseCenter.y));
        Near(layout.RoadDistance(drive), 0f, 0.001f, "House driveway joins main road");
        Near(layout.GrassWeight(drive, 32f, 0f, false), 0f, 0f, "No grass on driveway");
        layout.Paint(drive, Slope(layout, drive), weights);
        Require(weights[9] > 0.99f, "Dirt paint covers the house driveway");
    }

    private static void CheckDeterminism()
    {
        var first = Create(512, 2026, 0f);
        var repeat = Create(512, 2026, 0f);
        var other = Create(512, 2027, 0f);
        bool different = false;
        for (int i = 0; i <= 100; i++)
        {
            float2 world = first.World(i / 100f, 0.95f);
            Near(first.Height(world), repeat.Height(world), 0f, "Reproducible seed");
            different |= math.abs(first.Height(world) - other.Height(world)) > 0.1f;
        }
        Require(different, "Changing seed changes terrain");
    }

    private static void CheckReference()
    {
        var weights = new float[MainMenuTerrainLayout.LAYER_COUNT];
        foreach (int seed in new[] { 2026, 1, -17 })
        foreach (float angle in new[] { 0f, 0.7854f, 2.31f })
        {
            var layout = Create(512, seed, angle);
            var reference = new ReferenceLayout(seed, 512f, 36f, 6f, new float2(0f, -10f), new float2(math.sin(angle), math.cos(angle)));
            for (int y = 0; y <= 32; y++)
            for (int x = 0; x <= 32; x++)
            {
                float2 point = layout.World(x / 32f, y / 32f);
                Near(layout.Height(point), reference.Height(point), 0.00002f, "Optimized height preserves reference terrain");
                float slope = Slope(layout, point);
                reference.Paint(point, slope, weights);
                MainMenuTerrainWeights packed = layout.Paint(point, slope);
                for (int layer = 0; layer < weights.Length; layer++)
                    Near(packed[layer], weights[layer], 0.000002f, "Packed paint preserves reference weights");
            }
        }
    }

    private static MainMenuTerrainLayout Create(int size, int seed, float angle)
    {
        return new MainMenuTerrainLayout(seed, size, 36f, 6f, new float2(0f, -10f), new float2(math.sin(angle), math.cos(angle)));
    }

    private static float Slope(MainMenuTerrainLayout layout, float2 point)
    {
        float x = layout.Height(point + new float2(0.5f, 0f)) - layout.Height(point - new float2(0.5f, 0f));
        float z = layout.Height(point + new float2(0f, 0.5f)) - layout.Height(point - new float2(0f, 0.5f));
        return math.degrees(math.atan(math.sqrt(x * x + z * z)));
    }

    private static void Preview(string output)
    {
        var layout = Create(512, 2026, 0.04175f);
        const int RESOLUTION = 768;
        var pixels = new byte[RESOLUTION * RESOLUTION * 3];
        var heights = new byte[pixels.Length];
        var weights = new float[MainMenuTerrainLayout.LAYER_COUNT];
        float3[] palette =
        {
            new(100, 111, 70), new(80, 100, 50), new(65, 75, 47), new(104, 87, 62), new(116, 119, 109),
            new(75, 64, 54), new(119, 111, 101), new(43, 40, 36), new(50, 50, 50), new(137, 112, 80), new(106, 100, 69)
        };
        for (int y = 0; y < RESOLUTION; y++)
        for (int x = 0; x < RESOLUTION; x++)
        {
            float2 point = layout.World((float)x / (RESOLUTION - 1), 1f - (float)y / (RESOLUTION - 1));
            float slope = Slope(layout, point);
            layout.Paint(point, slope, weights);
            float3 color = float3.zero;
            for (int layer = 0; layer < weights.Length; layer++)
                color += palette[layer] * weights[layer];
            float shade = math.clamp(0.85f + (layout.Height(point + new float2(-1f, 1f)) - layout.Height(point)) * 0.8f, 0.5f, 1.3f);
            color *= shade;
            if (layout.HouseClearance(point) < 0.01f)
                color = math.lerp(color, new float3(190, 170, 105), 0.35f);
            if (math.lengthsq(layout.ToLocal(point)) < 4f)
                color = new float3(80, 180, 235);
            int index = (y * RESOLUTION + x) * 3;
            pixels[index] = (byte)math.clamp(color.x, 0f, 255f);
            pixels[index + 1] = (byte)math.clamp(color.y, 0f, 255f);
            pixels[index + 2] = (byte)math.clamp(color.z, 0f, 255f);
            byte height = (byte)math.clamp((layout.Height(point) - 5f) / 40f * 255f, 0f, 255f);
            heights[index] = heights[index + 1] = heights[index + 2] = height;
        }
        Png.Write(Path.Combine(output, "Layout.png"), pixels, RESOLUTION, RESOLUTION);
        Png.Write(Path.Combine(output, "Height.png"), heights, RESOLUTION, RESOLUTION);
    }

    private static void Near(float actual, float expected, float tolerance, string message)
    {
        Require(math.abs(actual - expected) <= tolerance, $"{message}: expected {expected}, got {actual}");
    }

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
