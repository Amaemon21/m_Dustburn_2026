using Unity.Mathematics;

public struct GroundSample
{
    public float2 Warped;
    public float Macro;
    public float Detail;
    public float Slope;
    public float Relief;
}

public struct GroundNoise
{
    public const float LARGE_RELIEF_RADIUS = 4f;
    public const float LARGE_RELIEF_RANGE = 4.5f;

    private const float NEAR_RELIEF_SHARE = 0.35f;

    private const int WARP_X = 11;
    private const int WARP_Z = 12;
    private const int MACRO = 13;
    private const int DETAIL = 14;

    private const int PATCH_OCTAVES = 2;
    private const float PATCH_LACUNARITY = 2.03f;
    private const float PATCH_PERSISTENCE = 0.5f;

    private const int MACRO_OCTAVES = 2;
    private const float MACRO_GAIN = 1.6f;

    public float2 WarpX;
    public float2 WarpZ;
    public float2 MacroOffset;
    public float2 DetailOffset;

    public float WarpScale;
    public float WarpStrength;
    public float MacroScale;
    public float DetailScale;
    public float DetailStrength;
    public float SlopeMid;
    public float SlopeSpan;
    public float CliffJitter;

    public static GroundNoise From(WorldGenerationConfig config)
    {
        return new GroundNoise
        {
            WarpX = FractalNoise.Offset(config.Seed, WARP_X),
            WarpZ = FractalNoise.Offset(config.Seed, WARP_Z),
            MacroOffset = FractalNoise.Offset(config.Seed, MACRO),
            DetailOffset = FractalNoise.Offset(config.Seed, DETAIL),
            WarpScale = math.max(1f, config.GroundWarpScale),
            WarpStrength = math.max(0f, config.GroundWarpStrength),
            MacroScale = math.max(1f, config.GroundMacroScale),
            DetailScale = math.max(1f, config.GroundDetailScale),
            DetailStrength = math.max(0f, config.GroundDetailStrength),
            SlopeMid = config.GroundSlopeMid,
            SlopeSpan = math.max(1f, config.GroundSlopeSpan),
            CliffJitter = math.max(0f, config.CliffJitter)
        };
    }

    public static float ReliefSignal(float height, float nearRing, float farRing, float range)
    {
        float near = math.clamp((height - nearRing) / range, -1f, 1f);
        float far = math.clamp((height - farRing) / (range * LARGE_RELIEF_RANGE), -1f, 1f);

        return NEAR_RELIEF_SHARE * near + (1f - NEAR_RELIEF_SHARE) * far;
    }

    public GroundSample Sample(float worldX, float worldZ, float slope, float relief)
    {
        var point = new float2(worldX, worldZ);
        float2 scaled = point / WarpScale;

        float2 bend = new float2(noise.snoise(scaled + WarpX), noise.snoise(scaled + WarpZ)) * WarpStrength;
        float macro = FractalNoise.Sample(point / MacroScale, MacroOffset, MACRO_OCTAVES, 2f, 0.5f) * MACRO_GAIN;

        return new GroundSample
        {
            Warped = point + bend,
            Macro = math.clamp(macro, -1f, 1f),
            Detail = noise.snoise(point / DetailScale + DetailOffset),
            Slope = math.clamp((slope - SlopeMid) / SlopeSpan, -1f, 1f),
            Relief = math.clamp(relief, -1f, 1f)
        };
    }

    public float CliffSlope(float slope, in GroundSample sample)
    {
        return slope + CliffJitter * 0.5f * (sample.Detail + sample.Relief);
    }

    public float Patch(in GroundRule rule, in GroundSample sample, float edge)
    {
        float2 turned = FractalNoise.Turn(sample.Warped, rule.PatchAxis) / rule.PatchFrequency;
        float patch = FractalNoise.Sample01(turned, rule.PatchOffset, PATCH_OCTAVES, PATCH_LACUNARITY, PATCH_PERSISTENCE);

        float score = patch
                      + rule.MacroBias * sample.Macro
                      + rule.SlopeBias * sample.Slope
                      + rule.ReliefBias * sample.Relief
                      + rule.EdgeBias * edge
                      + DetailStrength * rule.DetailSign * sample.Detail;

        return math.smoothstep(rule.PatchThreshold, rule.PatchThreshold + rule.PatchFade, score);
    }

    public static float Edge(float blendWeight)
    {
        return math.saturate(2f * (1f - blendWeight));
    }
}
