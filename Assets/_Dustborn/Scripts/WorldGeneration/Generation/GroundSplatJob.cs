using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public struct GroundRule
{
    public int Layer;
    public float Weight;
    public float PatchFrequency;
    public float PatchThreshold;
    public float PatchFade;
    public float MinSlope;
    public float MaxSlope;
    public float SlopeFade;
    public float MinHeight;
    public float MaxHeight;
    public float HeightFade;
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct GroundSplatJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> BiomeWeights;
    [ReadOnly] public NativeArray<GroundRule> Rules;
    [ReadOnly] public NativeArray<int> RuleStart;
    [ReadOnly] public NativeArray<int> RuleCount;
    [ReadOnly] public NativeArray<float> RoadMask;
    [ReadOnly] public NativeArray<float> Steepness;
    [ReadOnly] public NativeArray<float> Height;

    [NativeDisableParallelForRestriction] public NativeArray<float> Alphamaps;

    public int Resolution;
    public int WeightResolution;
    public int BiomeCount;
    public int LayerCount;

    public int RoadMaskResolution;
    public int CliffLayer;
    public int RoadLayer;

    public float CliffSlopeStart;
    public float CliffSlopeFull;

    public float TileSize;
    public float WorldSize;
    public float OriginX;
    public float OriginZ;
    public float NoiseSeed;

    public void Execute(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        float worldX = OriginX + (x + 0.5f) / Resolution * TileSize;
        float worldZ = OriginZ + (y + 0.5f) / Resolution * TileSize;

        float u = worldX / WorldSize;
        float v = worldZ / WorldSize;

        float steepness = Steepness[index];
        float elevation = Height[index];

        float cliff = CliffWeight(steepness);
        float road = SampleRoad(u, v);

        float natural = 1f - road;
        float ground = (1f - cliff) * natural;

        int origin = index * LayerCount;

        for (int layer = 0; layer < LayerCount; layer++)
            Alphamaps[origin + layer] = 0f;

        BiomeWeightSampler sampler = BiomeWeightSampler.At(u, v, WeightResolution);

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            float weight = sampler.Sample(BiomeWeights, biome, WeightResolution);

            if (weight <= 0.001f)
                continue;

            Spread(origin, biome, weight * ground, steepness, elevation, worldX, worldZ);
        }

        if (CliffLayer >= 0)
            Alphamaps[origin + CliffLayer] += cliff * natural;

        if (RoadLayer >= 0)
            Alphamaps[origin + RoadLayer] += road;

        float sum = 0f;

        for (int layer = 0; layer < LayerCount; layer++)
            sum += Alphamaps[origin + layer];

        if (sum <= 0f)
        {
            Alphamaps[origin] = 1f;
            return;
        }

        for (int layer = 0; layer < LayerCount; layer++)
            Alphamaps[origin + layer] /= sum;
    }

    private void Spread(int origin, int biome, float share, float steepness, float elevation, float worldX, float worldZ)
    {
        int start = RuleStart[biome];
        int count = RuleCount[biome];

        if (count == 0)
            return;

        float sum = 0f;

        for (int i = 0; i < count; i++)
            sum += RuleWeight(Rules[start + i], steepness, elevation, worldX, worldZ);

        if (sum <= 1e-5f)
        {
            Alphamaps[origin + Rules[start].Layer] += share;
            return;
        }

        float scale = share / sum;

        for (int i = 0; i < count; i++)
        {
            GroundRule rule = Rules[start + i];

            Alphamaps[origin + rule.Layer] += RuleWeight(rule, steepness, elevation, worldX, worldZ) * scale;
        }
    }

    private float RuleWeight(GroundRule rule, float steepness, float elevation, float worldX, float worldZ)
    {
        float weight = rule.Weight * Band(steepness, rule.MinSlope, rule.MaxSlope, rule.SlopeFade)
            * Band(elevation, rule.MinHeight, rule.MaxHeight, rule.HeightFade);

        if (weight <= 1e-5f || rule.PatchThreshold <= 0f)
            return weight;

        float patch = FractalNoise.Sample01(new float2(worldX, worldZ) / rule.PatchFrequency,
            new float2(NoiseSeed, rule.PatchFrequency), 3, 2.1f, 0.5f);

        return weight * math.smoothstep(rule.PatchThreshold, rule.PatchThreshold + rule.PatchFade, patch);
    }

    private static float Band(float value, float min, float max, float fade)
    {
        float rise = min <= 0f ? 1f : math.smoothstep(min - fade, min, value);
        float fall = 1f - math.smoothstep(max, max + fade, value);

        return rise * fall;
    }

    private float SampleRoad(float u, float v)
    {
        if (RoadMaskResolution <= 0 || RoadLayer < 0)
            return 0f;

        int last = RoadMaskResolution - 1;

        float fx = math.clamp(u * last, 0f, last);
        float fy = math.clamp(v * last, 0f, last);

        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = math.min(x0 + 1, last);
        int y1 = math.min(y0 + 1, last);

        float tx = fx - x0;
        float ty = fy - y0;

        float bottom = math.lerp(RoadMask[y0 * RoadMaskResolution + x0], RoadMask[y0 * RoadMaskResolution + x1], tx);
        float top = math.lerp(RoadMask[y1 * RoadMaskResolution + x0], RoadMask[y1 * RoadMaskResolution + x1], tx);

        return math.saturate(math.lerp(bottom, top, ty));
    }

    private float CliffWeight(float steepness)
    {
        if (CliffLayer < 0 || CliffSlopeFull <= CliffSlopeStart)
            return 0f;

        return math.smoothstep(CliffSlopeStart, CliffSlopeFull, steepness);
    }
}

public struct BiomeWeightSampler
{
    private int _x0;
    private int _y0;
    private int _x1;
    private int _y1;
    private float _tx;
    private float _ty;

    public static BiomeWeightSampler At(float u, float v, int resolution)
    {
        int last = resolution - 1;

        float fx = math.clamp(u * resolution - 0.5f, 0f, last);
        float fy = math.clamp(v * resolution - 0.5f, 0f, last);

        var sampler = new BiomeWeightSampler
        {
            _x0 = (int)fx,
            _y0 = (int)fy
        };

        sampler._x1 = math.min(sampler._x0 + 1, last);
        sampler._y1 = math.min(sampler._y0 + 1, last);
        sampler._tx = Fade(fx - sampler._x0);
        sampler._ty = Fade(fy - sampler._y0);

        return sampler;
    }

    public float Sample(NativeArray<float> weights, int biome, int resolution)
    {
        int origin = biome * resolution * resolution;

        float bottom = math.lerp(weights[origin + _y0 * resolution + _x0], weights[origin + _y0 * resolution + _x1], _tx);
        float top = math.lerp(weights[origin + _y1 * resolution + _x0], weights[origin + _y1 * resolution + _x1], _tx);

        return math.lerp(bottom, top, _ty);
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
