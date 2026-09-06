using Unity.Mathematics;

public static class FractalNoise
{
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
