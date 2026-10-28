using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
public struct MainMenuTerrainHeightJob : IJobParallelFor
{
    public MainMenuTerrainLayout Layout;
    public int Resolution;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Heights;

    public void Execute(int row)
    {
        float v = (float)row / (Resolution - 1);
        for (int x = 0; x < Resolution; x++)
            Heights[row * Resolution + x] = Layout.Height(Layout.World((float)x / (Resolution - 1), v)) / MainMenuTerrainLayout.HEIGHT_RANGE;
    }
}

public struct MainMenuHeightSampler
{
    public NativeArray<float> Heights;
    public int Resolution;
    public float Size;

    public float Slope(float u, float v)
    {
        float step = 1f / (Resolution - 1);
        float left = math.max(0f, u - step);
        float right = math.min(1f, u + step);
        float back = math.max(0f, v - step);
        float front = math.min(1f, v + step);
        float dx = (Sample(right, v) - Sample(left, v)) / ((right - left) * Size);
        float dz = (Sample(u, front) - Sample(u, back)) / ((front - back) * Size);
        return math.degrees(math.atan(math.sqrt(dx * dx + dz * dz)));
    }

    public float Sample(float u, float v)
    {
        float2 point = math.saturate(new float2(u, v)) * (Resolution - 1);
        int2 first = (int2)math.floor(point);
        int2 next = math.min(first + 1, Resolution - 1);
        float2 fraction = point - first;
        float low = math.lerp(Heights[first.y * Resolution + first.x], Heights[first.y * Resolution + next.x], fraction.x);
        float high = math.lerp(Heights[next.y * Resolution + first.x], Heights[next.y * Resolution + next.x], fraction.x);
        return math.lerp(low, high, fraction.y) * MainMenuTerrainLayout.HEIGHT_RANGE;
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
public struct MainMenuTerrainPaintJob : IJobParallelFor
{
    public MainMenuTerrainLayout Layout;
    public int Resolution;
    public int HeightResolution;
    [ReadOnly] public NativeArray<float> Heights;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Weights;

    public void Execute(int row)
    {
        var sampler = new MainMenuHeightSampler { Heights = Heights, Resolution = HeightResolution, Size = Layout.Size };
        float v = (float)row / (Resolution - 1);
        for (int x = 0; x < Resolution; x++)
        {
            float u = (float)x / (Resolution - 1);
            MainMenuTerrainWeights weights = Layout.Paint(Layout.World(u, v), sampler.Slope(u, v));
            int start = (row * Resolution + x) * MainMenuTerrainLayout.LAYER_COUNT;
            for (int layer = 0; layer < MainMenuTerrainLayout.LAYER_COUNT; layer++)
                Weights[start + layer] = weights[layer];
        }
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
public struct MainMenuGrassSurfaceJob : IJobParallelFor
{
    public MainMenuTerrainLayout Layout;
    public int Resolution;
    public int HeightResolution;
    [ReadOnly] public NativeArray<float> Heights;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> Surface;

    public void Execute(int row)
    {
        var sampler = new MainMenuHeightSampler { Heights = Heights, Resolution = HeightResolution, Size = Layout.Size };
        float v = (row + 0.5f) / Resolution;
        for (int x = 0; x < Resolution; x++)
        {
            float u = (x + 0.5f) / Resolution;
            float2 cover = Layout.GrassSurface(Layout.World(u, v));
            Surface[row * Resolution + x] = new float3(sampler.Slope(u, v), cover.x, cover.y);
        }
    }
}

public struct MainMenuGrassSettings
{
    public float MaxSlope;
    public float PatchFrequency;
    public float PatchThreshold;
    public float Density;
    public int Burnt;
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
public struct MainMenuGrassScatterJob : IJobParallelFor
{
    public MainMenuTerrainLayout Layout;
    public int Resolution;
    public uint Seed;
    public float Density;
    [ReadOnly] public NativeArray<MainMenuGrassSettings> Layers;
    [ReadOnly] public NativeArray<float3> Surface;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Instances;
    [WriteOnly] public NativeArray<int> RowCounts;

    public void Execute(int index)
    {
        int layerIndex = index / Resolution;
        int row = index - layerIndex * Resolution;
        MainMenuGrassSettings grass = Layers[layerIndex];
        float area = Layout.Size * Layout.Size / (Resolution * Resolution);
        float v = (row + 0.5f) / Resolution;
        int count = 0;
        for (int x = 0; x < Resolution; x++)
        {
            int cell = row * Resolution + x;
            float3 surface = Surface[cell];
            float biome = grass.Burnt != 0 ? surface.z : 1f - surface.z;
            float slope = 1f - math.smoothstep(math.max(0f, grass.MaxSlope - 8f), grass.MaxSlope, surface.x);
            float weight = surface.y * biome * slope;
            int instances = 0;
            if (weight > 0f && grass.Density > 0f && Density > 0f)
            {
                float2 world = Layout.World((x + 0.5f) / Resolution, v);
                float patch = Layout.Noise(world, math.max(1f, grass.PatchFrequency), layerIndex + 20f);
                weight *= math.lerp(0.3f, 1f, math.smoothstep(grass.PatchThreshold - 0.16f, grass.PatchThreshold + 0.12f, patch));
                float expected = math.min(16f, grass.Density * Density * (grass.Burnt != 0 ? 2f : 1f) * area * weight);
                instances = (int)expected;
                float random = (math.hash(new uint3(Seed, (uint)layerIndex, (uint)cell)) >> 8) * (1f / 16777216f);
                if (random < expected - instances)
                    instances++;
            }
            Instances[layerIndex * Resolution * Resolution + cell] = instances;
            count += instances;
        }
        RowCounts[index] = count;
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
public struct MainMenuGrassRecolorJob : IJobParallelFor
{
    public int Width;
    public int Height;
    public bool HasMask;
    public float3 Leaves;
    public float3 Stems;
    public float3 Flowers;
    public float3 Tip;
    public float RootDarkening;
    public float TextureShading;
    [ReadOnly] public NativeArray<Color> Source;
    [ReadOnly] public NativeArray<Color> Mask;
    [WriteOnly] public NativeArray<Color32> Pixels;

    public void Execute(int index)
    {
        Color source = Source[index];
        Color mask = HasMask ? Mask[index] : new Color(1f, 0f, 0f, 1f);
        float total = mask.r + mask.g + mask.b;
        float3 tint = total > 0.001f ? (Leaves * mask.r + Stems * mask.g + Flowers * mask.b) / total : Leaves;
        float v = (index / Width + 0.5f) / Height;
        tint *= math.lerp(new float3(1f), Tip, v);
        float root = 1f - RootDarkening * (1f - math.smoothstep(0f, 0.55f, v));
        float3 color = ToSrgb(tint * math.lerp(new float3(1f), new float3(source.r, source.g, source.b), TextureShading) * root);
        Pixels[index] = new Color32(ToByte(color.x), ToByte(color.y), ToByte(color.z), ToByte(source.a));
    }

    public static float3 ToSrgb(float3 linear)
    {
        return math.select(linear * 12.92f, 1.055f * math.pow(math.max(linear, 0f), 1f / 2.4f) - 0.055f, linear > 0.0031308f);
    }

    private static byte ToByte(float value) => (byte)math.clamp(math.round(value * 255f), 0f, 255f);
}
