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

    private const int WARP_X = 1;
    private const int WARP_Z = 2;
    private const int JITTER_STREAM = 100;
    private const float JITTER_PERIOD_SHARE = 0.3f;
    private const float JITTER_GRADIENT = 0.82f;

    public float Warp;
    public float WarpPeriod;
    public float Exponent;
    public float2 SeedX;
    public float2 SeedZ;
    public float Jitter;
    public float JitterPeriod;
    public int Seed;

    public static BiomeBorder From(WorldGenerationConfig config)
    {
        float blend = math.max(0f, config.BiomeBlendRadius);
        float jitter = math.max(0f, config.BiomeBorderJitter);
        float jitterPeriod = math.max(1f, config.BiomeBorderWarpPeriod * JITTER_PERIOD_SHARE);
        float steepening = 1f + JITTER_GRADIENT * jitter * blend / jitterPeriod;

        return new BiomeBorder
        {
            Warp = math.clamp(config.BiomeBorderWarp, 0f, blend),
            WarpPeriod = math.max(1f, config.BiomeBorderWarpPeriod),
            Exponent = blend <= 0f ? 1f : math.max(1f, PROFILE_SPAN * blend / math.max(MIN_WIDTH, config.BiomeBorderWidth) / steepening),
            SeedX = FractalNoise.Offset(config.Seed, WARP_X),
            SeedZ = FractalNoise.Offset(config.Seed, WARP_Z),
            Jitter = jitter,
            JitterPeriod = jitterPeriod,
            Seed = config.Seed
        };
    }

    public float2 Displace(float worldX, float worldZ)
    {
        var point = new float2(worldX, worldZ);

        if (Warp <= 0f)
            return point;

        float2 scaled = point / WarpPeriod;

        float dx = FractalNoise.Sample(scaled, SeedX, OCTAVES, LACUNARITY, PERSISTENCE);
        float dz = FractalNoise.Sample(scaled, SeedZ, OCTAVES, LACUNARITY, PERSISTENCE);

        return point + new float2(dx, dz) * Warp;
    }

    public float Part(float weight, int biome, float2 displaced)
    {
        if (weight <= 0f)
            return 0f;

        float level = math.log(weight);

        if (Jitter > 0f)
            level += Jitter * noise.snoise(displaced / JitterPeriod + FractalNoise.Offset(Seed, JITTER_STREAM + biome));

        return math.exp(Exponent * level);
    }
}
