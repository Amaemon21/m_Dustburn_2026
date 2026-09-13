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
        Taps(u, v, out int bottomLeft, out int bottomRight, out int topLeft, out int topRight, out float tx, out float ty);

        for (int biome = 0; biome < BiomeCount; biome++)
            result[biome] = Blend(_weights[biome], bottomLeft, bottomRight, topLeft, topRight, tx, ty);
    }

    public float SampleOne(float u, float v, int biome)
    {
        Taps(u, v, out int bottomLeft, out int bottomRight, out int topLeft, out int topRight, out float tx, out float ty);

        return Blend(_weights[biome], bottomLeft, bottomRight, topLeft, topRight, tx, ty);
    }

    private void Taps(float u, float v, out int bottomLeft, out int bottomRight, out int topLeft, out int topRight, out float tx, out float ty)
    {
        int last = Resolution - 1;

        float fx = Mathf.Clamp(u * Resolution - 0.5f, 0f, last);
        float fy = Mathf.Clamp(v * Resolution - 0.5f, 0f, last);

        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = Mathf.Min(x0 + 1, last);
        int y1 = Mathf.Min(y0 + 1, last);

        int rowBottom = y0 * Resolution;
        int rowTop = y1 * Resolution;

        bottomLeft = rowBottom + x0;
        bottomRight = rowBottom + x1;
        topLeft = rowTop + x0;
        topRight = rowTop + x1;

        tx = Fade(fx - x0);
        ty = Fade(fy - y0);
    }

    private static float Blend(float[] weights, int bottomLeft, int bottomRight, int topLeft, int topRight, float tx, float ty)
    {
        float bottom = Mathf.LerpUnclamped(weights[bottomLeft], weights[bottomRight], tx);
        float top = Mathf.LerpUnclamped(weights[topLeft], weights[topRight], tx);

        return Mathf.LerpUnclamped(bottom, top, ty);
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

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
