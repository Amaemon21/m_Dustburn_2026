using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

public class BiomeWeightField
{
    private const int PASSES = 3;

    private readonly float[][] _weights;

    public int Resolution { get; }
    public int BiomeCount { get; }

    public BiomeWeightField(BiomeMap map, int biomeCount, float blendRadius)
    {
        Resolution = map.Resolution;
        BiomeCount = biomeCount;

        _weights = new float[biomeCount][];

        for (int biome = 0; biome < biomeCount; biome++)
            _weights[biome] = new float[map.Cells.Length];

        byte[] cells = map.Cells;
        int resolution = Resolution;
        int radius = BoxRadius(blendRadius / map.CellSize);

        if (radius <= 0)
        {
            for (int i = 0; i < cells.Length; i++)
                _weights[cells[i]][i] = 1f;

            return;
        }

        long total = 1;

        for (int pass = 0; pass < 2 * PASSES; pass++)
            total *= 2 * radius + 1;

        Parallel.For(0, biomeCount, () => (new long[cells.Length], new long[cells.Length]), (biome, state, buffers) =>
        {
            (long[] counts, long[] buffer) = buffers;

            for (int i = 0; i < cells.Length; i++)
                counts[i] = cells[i] == biome ? 1L : 0L;

            for (int pass = 0; pass < PASSES; pass++)
                Blur(counts, buffer, resolution, radius);

            float[] weights = _weights[biome];

            for (int i = 0; i < counts.Length; i++)
                weights[i] = (float)((double)counts[i] / total);

            return buffers;
        }, _ => { });
    }

    private static int BoxRadius(float sigmaCells)
    {
        if (sigmaCells <= 0f)
            return 0;

        float exact = 0.5f * (Mathf.Sqrt(1f + 4f * sigmaCells * sigmaCells) - 1f);

        return Mathf.Max(1, Mathf.RoundToInt(exact));
    }

    public float Coverage(int biome)
    {
        float[] weights = _weights[biome];
        double sum = 0;

        for (int i = 0; i < weights.Length; i++)
            sum += weights[i];

        return (float)(sum / weights.Length);
    }

    public NativeArray<float> ToNativeArray(Allocator allocator)
    {
        int cellCount = Resolution * Resolution;
        var array = new NativeArray<float>(BiomeCount * cellCount, allocator, NativeArrayOptions.UninitializedMemory);

        for (int biome = 0; biome < BiomeCount; biome++)
            NativeArray<float>.Copy(_weights[biome], 0, array, biome * cellCount, cellCount);

        return array;
    }

    public void Sample(float u, float v, float[] result)
    {
        BiomeWeightSampler sampler = BiomeWeightSampler.At(u, v, Resolution);

        for (int biome = 0; biome < BiomeCount; biome++)
            result[biome] = sampler.Sample(_weights[biome]);
    }

    public float SampleOne(float u, float v, int biome)
    {
        return BiomeWeightSampler.At(u, v, Resolution).Sample(_weights[biome]);
    }

    private static void Blur(long[] values, long[] buffer, int resolution, int radius)
    {
        for (int y = 0; y < resolution; y++)
        {
            int row = y * resolution;
            long sum = values[row] * (radius + 1);

            for (int offset = 1; offset <= radius; offset++)
                sum += values[row + Mathf.Min(offset, resolution - 1)];

            for (int x = 0; x < resolution; x++)
            {
                buffer[row + x] = sum;

                sum += values[row + Mathf.Min(x + radius + 1, resolution - 1)]
                       - values[row + Mathf.Max(x - radius, 0)];
            }
        }

        for (int x = 0; x < resolution; x++)
        {
            long sum = buffer[x] * (radius + 1);

            for (int offset = 1; offset <= radius; offset++)
                sum += buffer[Mathf.Min(offset, resolution - 1) * resolution + x];

            for (int y = 0; y < resolution; y++)
            {
                values[y * resolution + x] = sum;

                sum += buffer[Mathf.Min(y + radius + 1, resolution - 1) * resolution + x]
                       - buffer[Mathf.Max(y - radius, 0) * resolution + x];
            }
        }
    }
}
