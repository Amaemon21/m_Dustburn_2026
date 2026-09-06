using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct VoxelDensitySampler
{
    [ReadOnly] public NativeArray<float> Heights;

    public int Resolution;
    public float CellSize;
    public float MaxHeight;
    public float Floor;
    public float VoxelSize;

    public float Surface(float x, float z)
    {
        float u = math.clamp(x / CellSize, 0f, Resolution - 1.001f);
        float v = math.clamp(z / CellSize, 0f, Resolution - 1.001f);

        int column = (int)u;
        int row = (int)v;

        float fx = u - column;
        float fz = v - row;

        int origin = row * Resolution + column;

        float bottom = math.lerp(Heights[origin], Heights[origin + 1], fx);
        float top = math.lerp(Heights[origin + Resolution], Heights[origin + Resolution + 1], fx);

        return math.lerp(bottom, top, fz) * MaxHeight;
    }

    public float Sample(float x, float y, float z)
    {
        if (y < Floor)
            return Floor - y;

        return Surface(x, z) - y;
    }

    public Vector3 Normal(float x, float y, float z)
    {
        if (y < Floor)
            return Vector3.up;

        float step = VoxelSize * 0.5f;

        float dx = Surface(x + step, z) - Surface(x - step, z);
        float dz = Surface(x, z + step) - Surface(x, z - step);
        float dy = 2f * step;

        float length = math.sqrt(dx * dx + dy * dy + dz * dz);

        if (length < 1e-6f)
            return Vector3.up;

        return new Vector3(-dx / length, dy / length, -dz / length);
    }

    public bool Fill(Vector3 origin, float voxelSize, int samples, NativeArray<float> columns, NativeArray<float> density)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (int z = 0; z < samples; z++)
        {
            for (int x = 0; x < samples; x++)
            {
                float surface = Surface(origin.x + (x - 1) * voxelSize, origin.z + (z - 1) * voxelSize);

                columns[z * samples + x] = surface;

                if (surface < lowest)
                    lowest = surface;

                if (surface > highest)
                    highest = surface;
            }
        }

        if (origin.y + (samples - 2) * voxelSize < lowest || origin.y - voxelSize > highest)
            return false;

        for (int z = 0; z < samples; z++)
        {
            for (int y = 0; y < samples; y++)
            {
                float height = origin.y + (y - 1) * voxelSize;

                int row = (z * samples + y) * samples;
                int column = z * samples;

                for (int x = 0; x < samples; x++)
                    density[row + x] = height < Floor ? Floor - height : columns[column + x] - height;
            }
        }

        return true;
    }
}
