using Unity.Mathematics;

public static class FractalNoise
{
    private const float OFFSET_SPAN = 256f;
    private const float UNIT = 1f / 16777216f;
    private const float TURN = 6.28318531f;

    public static float2 Offset(int seed, int stream)
    {
        uint x = Mix((uint)seed ^ Mix((uint)stream + 0x9E3779B9u));
        uint y = Mix(x ^ 0x85EBCA6Bu);

        return new float2(x >> 8, y >> 8) * (OFFSET_SPAN * UNIT);
    }

    public static float2 Axis(int seed, int stream)
    {
        uint x = Mix(Mix((uint)seed + 0xC2B2AE35u) ^ (uint)stream);
        float angle = (x >> 8) * UNIT * TURN;

        return new float2(math.cos(angle), math.sin(angle));
    }

    public static float2 Turn(float2 position, float2 axis)
    {
        return new float2(position.x * axis.x - position.y * axis.y, position.x * axis.y + position.y * axis.x);
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;

        return value;
    }

    public static float Sample(float2 position, float2 offset, int octaves, float lacunarity, float persistence)
    {
        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float normalization = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += noise.snoise(position * frequency + offset) * amplitude;
            normalization += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        if (normalization <= 0f)
            return 0f;

        return sum / normalization;
    }

    public static float Sample01(float2 position, float2 offset, int octaves, float lacunarity, float persistence)
    {
        return math.saturate(Sample(position, offset, octaves, lacunarity, persistence) * 0.5f + 0.5f);
    }

    public static float Billow(float2 position, float2 offset, int octaves, float lacunarity, float persistence)
    {
        return math.abs(Sample(position, offset, octaves, lacunarity, persistence));
    }

    public static float Ridged(float2 position, float2 offset, int octaves, float lacunarity, float persistence)
    {
        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float normalization = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            float ridge = 1f - math.abs(noise.snoise(position * frequency + offset));
            ridge *= ridge;

            sum += ridge * amplitude;
            normalization += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        if (normalization <= 0f)
            return 0f;

        return sum / normalization;
    }
}
