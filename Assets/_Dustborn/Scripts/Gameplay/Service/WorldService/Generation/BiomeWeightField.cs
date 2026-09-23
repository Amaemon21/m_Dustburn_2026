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

        for (int i = 0; i < cells.Length; i++)
            _weights[cells[i]][i] = 1f;

        int resolution = Resolution;
        int radius = BoxRadius(blendRadius / map.CellSize);

        if (radius <= 0)
        {
            Normalize();
            return;
        }

        Parallel.For(0, biomeCount, () => new float[cells.Length], (biome, state, buffer) =>
        {
            for (int pass = 0; pass < PASSES; pass++)
                Blur(_weights[biome], buffer, resolution, radius);

            return buffer;
        }, _ => { });

        Normalize();
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

    private void Normalize()
    {
        int cellCount = Resolution * Resolution;
        int biomeCount = BiomeCount;
        float[][] weights = _weights;

        Parallel.For(0, Resolution, row =>
        {
            int end = Mathf.Min(cellCount, (row + 1) * Resolution);

            for (int i = row * Resolution; i < end; i++)
            {
                float sum = 0f;

                for (int biome = 0; biome < biomeCount; biome++)
                    sum += weights[biome][i];

                if (sum <= 0f)
                    continue;

                for (int biome = 0; biome < biomeCount; biome++)
                    weights[biome][i] /= sum;
            }
        });
    }

    private static void Blur(float[] values, float[] buffer, int resolution, int radius)
    {
        float scale = 1f / (radius * 2 + 1);

        for (int y = 0; y < resolution; y++)
        {
            int row = y * resolution;
            float sum = values[row] * (radius + 1);

            for (int offset = 1; offset <= radius; offset++)
                sum += values[row + Mathf.Min(offset, resolution - 1)];

            for (int x = 0; x < resolution; x++)
            {
                buffer[row + x] = sum * scale;

                sum += values[row + Mathf.Min(x + radius + 1, resolution - 1)]
                       - values[row + Mathf.Max(x - radius, 0)];
            }
        }

        for (int x = 0; x < resolution; x++)
        {
            float sum = buffer[x] * (radius + 1);

            for (int offset = 1; offset <= radius; offset++)
                sum += buffer[Mathf.Min(offset, resolution - 1) * resolution + x];

            for (int y = 0; y < resolution; y++)
            {
                values[y * resolution + x] = sum * scale;

                sum += buffer[Mathf.Min(y + radius + 1, resolution - 1) * resolution + x]
                       - buffer[Mathf.Max(y - radius, 0) * resolution + x];
            }
        }
    }
}
