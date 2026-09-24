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
    public float2 PatchOffset;
    public float2 PatchAxis;
    public float PatchThreshold;
    public float PatchFade;
    public float MinSlope;
    public float MaxSlope;
    public float SlopeFade;
    public float MinHeight;
    public float MaxHeight;
    public float HeightFade;
    public float MacroBias;
    public float SlopeBias;
    public float ReliefBias;
    public float EdgeBias;
    public float DetailSign;
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
    [ReadOnly] public NativeArray<int> BiomeShores;
    [ReadOnly] public NativeArray<float> Wetness;
    [ReadOnly] public NativeArray<RoadPaintSegment> RoadSegments;
    [ReadOnly] public NativeArray<int> RoadCellStart;
    [ReadOnly] public NativeArray<int> RoadCellItems;
    [ReadOnly] public NativeArray<float> Steepness;
    [ReadOnly] public NativeArray<float> Height;
    [ReadOnly] public NativeArray<float> Relief;

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
    public GroundNoise Noise;

    public float TileSize;
    public float WorldSize;
    public float OriginX;
    public float OriginZ;

    public void Execute(int index)
    {
        int x = index % Resolution;
        int y = index / Resolution;

        float worldX = OriginX + (x + 0.5f) / Resolution * TileSize;
        float worldZ = OriginZ + (y + 0.5f) / Resolution * TileSize;

        float surface = 0f;
        float verge = 0f;

        if (RoadLayer >= 0)
            Roads.Cover(RoadSegments, RoadCellStart, RoadCellItems, worldX, worldZ, out surface, out verge);

        int origin = index * LayerCount;

        for (int layer = 0; layer < LayerCount; layer++)
            Alphamaps[origin + layer] = 0f;

        float natural = 1f - surface - verge;

        if (natural > 0f)
        {
            float relief = Relief.IsCreated ? Relief[index] : 0f;
            GroundSample ground = Noise.Sample(worldX, worldZ, Steepness[index], relief);

            float wet = Wetness.IsCreated ? Wetness[index] : 0f;

            PaintBiomes(origin, natural, Steepness[index], Height[index], wet, ground, worldX, worldZ);
        }

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

    private void PaintBiomes(int origin, float share, float steepness, float elevation, float wet, in GroundSample ground, float worldX, float worldZ)
    {
        float cliff = CliffWeight(Noise.CliffSlope(steepness, ground));

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

            if (strongest >= BiomeBorder.PURE)
                break;
        }

        if (strongest >= BiomeBorder.PURE)
        {
            PaintBiome(origin, dominant, share, cliff, GroundNoise.Edge(strongest), steepness, elevation, wet, ground);
            return;
        }

        float2 shifted = Border.Displace(worldX, worldZ);
        BiomeWeightSampler border = BiomeWeightSampler.At(shifted.x / WorldSize, shifted.y / WorldSize, WeightResolution);

        float total = 0f;

        for (int biome = 0; biome < BiomeCount; biome++)
            total += Border.Part(border.Sample(BiomeWeights, biome, WeightResolution), biome, shifted);

        if (total <= 0f)
        {
            PaintBiome(origin, dominant, share, cliff, GroundNoise.Edge(strongest), steepness, elevation, wet, ground);
            return;
        }

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            float part = Border.Part(border.Sample(BiomeWeights, biome, WeightResolution), biome, shifted) / total;

            if (part <= MIN_SHARE)
                continue;

            float edge = GroundNoise.Edge(blend.Sample(BiomeWeights, biome, WeightResolution));

            PaintBiome(origin, biome, share * part, cliff, edge, steepness, elevation, wet, ground);
        }
    }

    private void PaintBiome(int origin, int biome, float share, float cliff, float edge, float steepness, float elevation, float wet,
        in GroundSample ground)
    {
        int biomeCliff = BiomeCliffs.IsCreated ? BiomeCliffs[biome] : CliffLayer;
        float exposed = biomeCliff >= 0 ? cliff : 0f;

        int shore = BiomeShores.IsCreated ? BiomeShores[biome] : -1;
        float soaked = shore >= 0 ? math.saturate(wet) : 0f;
        float open = share * (1f - exposed);

        Spread(origin, biome, open * (1f - soaked), edge, steepness, elevation, ground);

        if (soaked > 0f)
            Alphamaps[origin + shore] += open * soaked;

        if (biomeCliff >= 0)
            Alphamaps[origin + biomeCliff] += share * exposed;
    }

    private void Spread(int origin, int biome, float share, float edge, float steepness, float elevation, in GroundSample ground)
    {
        int start = RuleStart[biome];
        int count = RuleCount[biome];

        if (count == 0 || share <= 0f)
            return;

        float remaining = share;

        for (int i = count - 1; i > 0 && remaining > COVERED; i--)
        {
            GroundRule rule = Rules[start + i];
            float laid = remaining * Coverage(rule, edge, steepness, elevation, ground);

            Alphamaps[origin + rule.Layer] += laid;
            remaining -= laid;
        }

        Alphamaps[origin + Rules[start].Layer] += remaining;
    }

    private float Coverage(in GroundRule rule, float edge, float steepness, float elevation, in GroundSample ground)
    {
        float coverage = math.saturate(rule.Opacity) * Band(steepness, rule.MinSlope, rule.MaxSlope, rule.SlopeFade)
            * Band(elevation, rule.MinHeight, rule.MaxHeight, rule.HeightFade);

        if (coverage <= COVERED || rule.PatchThreshold <= 0f)
            return coverage;

        return coverage * Noise.Patch(rule, ground, edge);
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
    private const float SIXTH = 1f / 6f;

    private int4 _columns;
    private int4 _rows;
    private float4 _wx;
    private float4 _wy;

    public static BiomeWeightSampler At(float u, float v, int resolution)
    {
        int last = resolution - 1;

        float fx = u * resolution - 0.5f;
        float fy = v * resolution - 0.5f;

        float cellX = math.floor(fx);
        float cellY = math.floor(fy);

        var taps = new int4(-1, 0, 1, 2);

        return new BiomeWeightSampler
        {
            _columns = math.clamp((int)cellX + taps, 0, last),
            _rows = math.clamp((int)cellY + taps, 0, last) * resolution,
            _wx = Spline(fx - cellX),
            _wy = Spline(fy - cellY)
        };
    }

    public float Sample(NativeArray<float> weights, int biome, int resolution)
    {
        int origin = biome * resolution * resolution;
        float sum = 0f;

        for (int j = 0; j < 4; j++)
        {
            int row = origin + _rows[j];

            float line = weights[row + _columns.x] * _wx.x + weights[row + _columns.y] * _wx.y
                         + weights[row + _columns.z] * _wx.z + weights[row + _columns.w] * _wx.w;

            sum += line * _wy[j];
        }

        return sum;
    }

    public float Sample(float[] weights)
    {
        float sum = 0f;

        for (int j = 0; j < 4; j++)
        {
            int row = _rows[j];

            float line = weights[row + _columns.x] * _wx.x + weights[row + _columns.y] * _wx.y
                         + weights[row + _columns.z] * _wx.z + weights[row + _columns.w] * _wx.w;

            sum += line * _wy[j];
        }

        return sum;
    }

    public static float4 Spline(float t)
    {
        float s = 1f - t;
        float t2 = t * t;
        float t3 = t2 * t;

        return new float4(s * s * s, 3f * t3 - 6f * t2 + 4f, -3f * t3 + 3f * t2 + 3f * t + 1f, t3) * SIXTH;
    }
}
