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
