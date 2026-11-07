using System;
using Unity.Mathematics;

public static class ReliefBands
{
    public const int MASK_OCTAVES = 3;
    public const float PLAINS_HILL = 0.2f;
    public const float PLAINS_DETAIL = 0.5f;

    private const int SAMPLE_GRID = 256;
    private const float TRANSITION = 0.035f;
    private const float BELOW_ALL = -1f;
    private const float ABOVE_ALL = 2f;

    public static void Apply(ref HeightFieldSettings settings, WorldGenerationConfig config, float2 maskOffset)
    {
        float[] samples = Samples(config.MountainMaskFrequency, maskOffset);
        float plains = Quantile(samples, config.PlainsShare);
        float mountains = Quantile(samples, config.PlainsShare + config.HillsShare);

        settings.HillMaskLow = plains - TRANSITION;
        settings.HillMaskHigh = plains + TRANSITION;
        settings.MountainMaskLow = mountains - TRANSITION;
        settings.MountainMaskHigh = mountains + TRANSITION;
        settings.PlainsHill = PLAINS_HILL;
        settings.PlainsDetail = PLAINS_DETAIL;
    }

    public static float Mask(float2 uv, float frequency, float2 maskOffset)
    {
        return FractalNoise.Sample01(uv * frequency, maskOffset, MASK_OCTAVES, 2f, 0.5f);
    }

    public static float[] Samples(float frequency, float2 maskOffset)
    {
        var samples = new float[SAMPLE_GRID * SAMPLE_GRID];
        float last = SAMPLE_GRID - 1;

        for (int y = 0; y < SAMPLE_GRID; y++)
        {
            for (int x = 0; x < SAMPLE_GRID; x++)
                samples[y * SAMPLE_GRID + x] = Mask(new float2(x / last, y / last), frequency, maskOffset);
        }

        Array.Sort(samples);
        return samples;
    }

    public static float Quantile(float[] sorted, float share)
    {
        if (share <= 0f)
            return BELOW_ALL;

        if (share >= 1f)
            return ABOVE_ALL;

        return sorted[(int)Math.Round(share * (sorted.Length - 1))];
    }
}
