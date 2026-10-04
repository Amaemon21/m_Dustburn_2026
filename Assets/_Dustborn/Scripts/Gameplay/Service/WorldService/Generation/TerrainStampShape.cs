using System;

public sealed class TerrainStampShape
{
    private const float INVERSE_MAX = 1f / 65535f;

    private readonly ushort[] _samples;

    public int Resolution { get; }
    public TerrainStampOperation Operation { get; }

    public TerrainStampShape(ushort[] samples, int resolution, TerrainStampOperation operation)
    {
        if (samples.Length != resolution * resolution)
            throw new ArgumentException($"A stamp of {resolution}x{resolution} needs {resolution * resolution} samples, got {samples.Length}");

        _samples = samples;
        Resolution = resolution;
        Operation = operation;
    }

    public static TerrainStampShape Decode(byte[] bytes, int resolution, TerrainStampOperation operation)
    {
        long expected = (long)resolution * resolution * sizeof(ushort);

        if (bytes == null || bytes.Length != expected)
            throw new ArgumentException($"A RAW16 stamp of {resolution}x{resolution} is {expected} bytes, got {bytes?.Length ?? 0}");

        var samples = new ushort[resolution * resolution];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));

        return new TerrainStampShape(samples, resolution, operation);
    }

    public static TerrainStampShape From(TerrainStampDefinition definition)
    {
        return Decode(definition.HeightData.bytes, definition.NativeResolution, definition.Operation);
    }

    public ushort Raw(int column, int row)
    {
        return _samples[row * Resolution + column];
    }

    public float Sample(float u, float v)
    {
        if (u < 0f || v < 0f || u > 1f || v > 1f)
            return 0f;

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
}
