using Unity.Mathematics;

public struct BiomeBorder
{
    public const float PURE = 0.998f;
    public const float ABSENT = 0.002f;

    private const int OCTAVES = 3;
    private const float LACUNARITY = 2.1f;
    private const float PERSISTENCE = 0.5f;

    private const float PROFILE_SPAN = 2.56f;
    private const float MIN_WIDTH = 0.5f;

    private const float WARP_X = 11.5f;
    private const float WARP_Z = 73.25f;

    public float Warp;
    public float WarpPeriod;
    public float Exponent;
    public float Seed;

    public static BiomeBorder From(WorldGenerationConfig config)
    {
        float blend = math.max(0f, config.BiomeBlendRadius);

        return new BiomeBorder
        {
            Warp = math.clamp(config.BiomeBorderWarp, 0f, blend),
            WarpPeriod = math.max(1f, config.BiomeBorderWarpPeriod),
            Exponent = blend <= 0f ? 1f : math.max(1f, PROFILE_SPAN * blend / math.max(MIN_WIDTH, config.BiomeBorderWidth)),
            Seed = (config.Seed >> 16) & 0xFFFF
        };
    }

    public float2 Displace(float worldX, float worldZ)
    {
        var point = new float2(worldX, worldZ);

        if (Warp <= 0f)
            return point;

        float2 scaled = point / WarpPeriod;

        float dx = FractalNoise.Sample(scaled, new float2(Seed, WARP_X), OCTAVES, LACUNARITY, PERSISTENCE);
        float dz = FractalNoise.Sample(scaled, new float2(Seed, WARP_Z), OCTAVES, LACUNARITY, PERSISTENCE);

        return point + new float2(dx, dz) * Warp;
    }

    public float Contrast(float weight)
    {
        return weight <= 0f ? 0f : math.pow(weight, Exponent);
    }
}
