using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public struct GroundRule
{
    public int Layer;
    public float Opacity;
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
    private const float COVERED = 1e-5f;
    private const float MIN_SHARE = 0.001f;

    [ReadOnly] public NativeArray<float> BiomeWeights;
    [ReadOnly] public NativeArray<GroundRule> Rules;
    [ReadOnly] public NativeArray<int> RuleStart;
    [ReadOnly] public NativeArray<int> RuleCount;
    [ReadOnly] public NativeArray<int> BiomeCliffs;
    [ReadOnly] public NativeArray<RoadPaintSegment> RoadSegments;
    [ReadOnly] public NativeArray<int> RoadCellStart;
    [ReadOnly] public NativeArray<int> RoadCellItems;
    [ReadOnly] public NativeArray<float> Steepness;
    [ReadOnly] public NativeArray<float> Height;

    [NativeDisableParallelForRestriction] public NativeArray<float> Alphamaps;

    public int Resolution;
    public int WeightResolution;
    public int BiomeCount;
    public int LayerCount;

    public int CliffLayer;
    public int RoadLayer;
    public int RoadEdgeLayer;

    public RoadPaint Roads;

    public float CliffSlopeStart;
    public float CliffSlopeFull;

    public BiomeBorder Border;

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

        float steepness = Steepness[index];
        float elevation = Height[index];

        float cliff = CliffWeight(steepness);

        float surface = 0f;
        float verge = 0f;

        if (RoadLayer >= 0)
            Roads.Cover(RoadSegments, RoadCellStart, RoadCellItems, worldX, worldZ, out surface, out verge);

        int origin = index * LayerCount;

        for (int layer = 0; layer < LayerCount; layer++)
            Alphamaps[origin + layer] = 0f;

        float natural = 1f - surface - verge;

        if (natural > 0f)
            PaintBiomes(origin, natural, cliff, steepness, elevation, worldX, worldZ);

        if (RoadLayer >= 0)
            Alphamaps[origin + RoadLayer] += surface;

        if (RoadEdgeLayer >= 0)
            Alphamaps[origin + RoadEdgeLayer] += verge;

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

    private void PaintBiomes(int origin, float share, float cliff, float steepness, float elevation, float worldX, float worldZ)
    {
        BiomeWeightSampler blend = BiomeWeightSampler.At(worldX / WorldSize, worldZ / WorldSize, WeightResolution);

        int dominant = 0;
        float strongest = 0f;

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            float weight = blend.Sample(BiomeWeights, biome, WeightResolution);

            if (weight <= strongest)
                continue;

            strongest = weight;
            dominant = biome;
        }

        if (strongest >= BiomeBorder.PURE)
        {
            PaintBiome(origin, dominant, share, cliff, steepness, elevation, worldX, worldZ);
            return;
        }

        float2 shifted = Border.Displace(worldX, worldZ);
        BiomeWeightSampler border = BiomeWeightSampler.At(shifted.x / WorldSize, shifted.y / WorldSize, WeightResolution);

        float total = 0f;

        for (int biome = 0; biome < BiomeCount; biome++)
            total += Border.Contrast(border.Sample(BiomeWeights, biome, WeightResolution));

        if (total <= 0f)
        {
            PaintBiome(origin, dominant, share, cliff, steepness, elevation, worldX, worldZ);
            return;
        }

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            float part = Border.Contrast(border.Sample(BiomeWeights, biome, WeightResolution)) / total;

            if (part <= MIN_SHARE)
                continue;

            PaintBiome(origin, biome, share * part, cliff, steepness, elevation, worldX, worldZ);
        }
    }

    private void PaintBiome(int origin, int biome, float share, float cliff, float steepness, float elevation, float worldX, float worldZ)
    {
        int biomeCliff = BiomeCliffs.IsCreated ? BiomeCliffs[biome] : CliffLayer;
        float exposed = biomeCliff >= 0 ? cliff : 0f;

        Spread(origin, biome, share * (1f - exposed), steepness, elevation, worldX, worldZ);

        if (biomeCliff >= 0)
            Alphamaps[origin + biomeCliff] += share * exposed;
    }

    private void Spread(int origin, int biome, float share, float steepness, float elevation, float worldX, float worldZ)
    {
        int start = RuleStart[biome];
        int count = RuleCount[biome];

        if (count == 0 || share <= 0f)
            return;

        float remaining = share;

        for (int i = count - 1; i > 0 && remaining > COVERED; i--)
        {
            GroundRule rule = Rules[start + i];
            float laid = remaining * Coverage(rule, steepness, elevation, worldX, worldZ);

            Alphamaps[origin + rule.Layer] += laid;
            remaining -= laid;
        }

        Alphamaps[origin + Rules[start].Layer] += remaining;
    }

    private float Coverage(GroundRule rule, float steepness, float elevation, float worldX, float worldZ)
    {
        float coverage = math.saturate(rule.Opacity) * Band(steepness, rule.MinSlope, rule.MaxSlope, rule.SlopeFade)
            * Band(elevation, rule.MinHeight, rule.MaxHeight, rule.HeightFade);

        if (coverage <= COVERED || rule.PatchThreshold <= 0f)
            return coverage;

        float patch = FractalNoise.Sample01(new float2(worldX, worldZ) / rule.PatchFrequency,
            new float2(NoiseSeed, rule.PatchFrequency), 3, 2.1f, 0.5f);

        return coverage * math.smoothstep(rule.PatchThreshold, rule.PatchThreshold + rule.PatchFade, patch);
    }

    private static float Band(float value, float min, float max, float fade)
    {
        float rise = min <= 0f ? 1f : math.smoothstep(min - fade, min, value);
        float fall = 1f - math.smoothstep(max, max + fade, value);

        return rise * fall;
    }

    private float CliffWeight(float steepness)
    {
        if (CliffSlopeFull <= CliffSlopeStart)
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
