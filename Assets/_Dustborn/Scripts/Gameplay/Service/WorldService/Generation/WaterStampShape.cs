using System;

public sealed class WaterStampShape
{
    private const float INVERSE_MAX = 1f / 65535f;

    private readonly ushort[] _samples;

    public int Resolution { get; }

    public WaterStampShape(ushort[] samples, int resolution)
    {
        if (samples.Length != resolution * resolution)
            throw new ArgumentException($"A water stamp of {resolution}x{resolution} needs {resolution * resolution} samples, got {samples.Length}");

        _samples = samples;
        Resolution = resolution;
    }

    public static WaterStampShape Decode(byte[] bytes, int resolution)
    {
        long expected = (long)resolution * resolution * sizeof(ushort);

        if (bytes == null || bytes.Length != expected)
            throw new ArgumentException($"A RAW16 water stamp of {resolution}x{resolution} is {expected} bytes, got {bytes?.Length ?? 0}");

        var samples = new ushort[resolution * resolution];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));

        return new WaterStampShape(samples, resolution);
    }

    public static WaterStampShape From(WaterStampDefinition definition)
    {
        return Decode(definition.MaskData.bytes, definition.NativeResolution);
    }

    public ushort Raw(int column, int row)
    {
        return _samples[row * Resolution + column];
    }

    public float Sample(float u, float v)
    {
        if (u < 0f || v < 0f || u > 1f || v > 1f)
            return 0f;

        return Bilinear(u, v);
    }

    public float SampleClamped(float u, float v)
    {
        return Bilinear(Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f));
    }

    private float Bilinear(float u, float v)
    {
        int last = Resolution - 1;

        float fx = u * last;
        float fy = v * last;

        int column = Math.Min((int)fx, last - 1);
        int row = Math.Min((int)fy, last - 1);

        float tx = fx - column;
        float ty = fy - row;

        int origin = row * Resolution + column;

        float top = _samples[origin] + (_samples[origin + 1] - (float)_samples[origin]) * tx;
        float bottom = _samples[origin + Resolution] + (_samples[origin + Resolution + 1] - (float)_samples[origin + Resolution]) * tx;

        return (top + (bottom - top) * ty) * INVERSE_MAX;
    }

    public float[] Downsample(int size)
    {
        var grid = new float[size * size];
        int block = Math.Max(1, Resolution / size);
        float share = INVERSE_MAX / (block * block);

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int x0 = Math.Min(Resolution - block, i * Resolution / size);
                int y0 = Math.Min(Resolution - block, j * Resolution / size);
                float sum = 0f;

                for (int y = y0; y < y0 + block; y++)
                {
                    int row = y * Resolution;

                    for (int x = x0; x < x0 + block; x++)
                        sum += _samples[row + x];
                }

                grid[j * size + i] = sum * share;
            }
        }

        return grid;
    }
}
