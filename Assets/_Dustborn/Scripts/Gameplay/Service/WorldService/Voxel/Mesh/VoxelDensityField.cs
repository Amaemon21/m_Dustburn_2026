using System;
using Unity.Collections;
using UnityEngine;

public class VoxelDensityField : IDisposable
{
    private NativeArray<float> _heights;

    public VoxelDensitySampler Sampler { get; private set; }

    public VoxelDensityField(HeightMap map, VoxelConfig voxels, float lowestSurface)
    {
        _heights = NativeBuffer.From(map.Heights);

        Sampler = new VoxelDensitySampler
        {
            Heights = _heights,
            Resolution = map.Resolution,
            CellSize = (float)map.WorldSize / (map.Resolution - 1),
            MaxHeight = map.MaxHeight,
            Floor = lowestSurface - voxels.Bedrock,
            VoxelSize = voxels.VoxelSize
        };
    }

    public float Floor => Sampler.Floor;

    public float Surface(float x, float z)
    {
        return Sampler.Surface(x, z);
    }

    public float Surface(float x, float z, DecorSurface frame)
    {
        if (frame.IsPlain)
            return Sampler.Surface(x, z);

        return Sampler.Height(x, z, frame.VoxelSize, frame.Morph, frame.Origin.x, frame.Origin.y, frame.Span,
            frame.VoxelSize * 2f);
    }

    public float Drawn(float x, float z, DecorSurface frame)
    {
        return Drawn(x, z, frame, out _);
    }

    public float Drawn(float x, float z, DecorSurface frame, out Vector2 slope)
    {
        if (!frame.IsPlain)
            return Sampler.Drawn(x, z, frame.VoxelSize, frame.Morph, frame.Origin.x, frame.Origin.y, frame.Size, out slope);

        slope = new Vector2(Sampler.Surface(x + 0.5f, z) - Sampler.Surface(x - 0.5f, z), Sampler.Surface(x, z + 0.5f) - Sampler.Surface(x, z - 0.5f));

        return Sampler.Surface(x, z);
    }

    public void ChunkRange(VoxelColumnKey key, float size, float step, out int low, out int high)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
        {
            for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
            {
                float surface = Surface(x, z);

                lowest = Mathf.Min(lowest, surface);
                highest = Mathf.Max(highest, surface);
            }
        }

        low = Mathf.FloorToInt(lowest / size) - 1;
        high = Mathf.FloorToInt(highest / size) + 1;
    }

    public float Sample(float x, float y, float z)
    {
        return Sampler.Sample(x, y, z);
    }

    public Vector3 Normal(float x, float y, float z)
    {
        return Sampler.Normal(x, y, z);
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref _heights);
    }
}
