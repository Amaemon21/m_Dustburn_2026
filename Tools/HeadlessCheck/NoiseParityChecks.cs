using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct NoiseParityProbe
{
    public static float Strict(float2 position, float2 offset)
    {
        return FractalNoise.Sample01(position, offset, 2, 2f, 0.5f);
    }
}

public static class NoiseParityChecks
{
    public static void Run()
    {
        var random = new System.Random(12345);
        float2 left = FractalNoise.Offset(7919, 1);
        long differ = 0;
        const int COUNT = 2000000;

        for (int i = 0; i < COUNT; i++)
        {
            var foot = new Vector2((float)random.NextDouble() * 8192f, (float)random.NextDouble() * 8192f);
            float period = Mathf.Max(28f, 3f * 2f * (float)random.NextDouble() * 12f);
            float2 position = new float2(foot.x, foot.y) / period;
            float mono = FractalNoise.Sample01(position, left, 2, 2f, 0.5f);
            float strict = NoiseParityProbe.Strict(position, left);

            if (BitConverter.SingleToInt32Bits(mono) != BitConverter.SingleToInt32Bits(strict))
                differ++;
        }

        Console.WriteLine($"noise parity: {differ} of {COUNT} samples differ between Mono and strict float");
    }
}
