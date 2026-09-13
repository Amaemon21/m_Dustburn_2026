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

    public float Blend(float x, float z, int morph, float u, float v, float step)
    {
        float weight = Weight(morph, u, v);

        return weight <= 0f ? Surface(x, z) : math.lerp(Surface(x, z), Coarse(x, z, step), weight);
    }

    public static float MorphAt(int sample, int size)
    {
        return (sample - 1) / math.max(1f, size - 1);
    }

    public static float MorphSpan(int size, float voxelSize)
    {
        return math.max(1f, size - 1) * voxelSize;
    }

    private static float Weight(int morph, float u, float v)
    {
        float weight = 0f;

        if ((morph & VoxelColumnKey.MORPH_MIN_X) != 0)
            weight = math.max(weight, 1f - u);

        if ((morph & VoxelColumnKey.MORPH_MAX_X) != 0)
            weight = math.max(weight, u);

        if ((morph & VoxelColumnKey.MORPH_MIN_Z) != 0)
            weight = math.max(weight, 1f - v);

        if ((morph & VoxelColumnKey.MORPH_MAX_Z) != 0)
            weight = math.max(weight, v);

        return math.saturate(weight);
    }

    public Vector3 Normal(float x, float y, float z)
    {
        return Normal(x, y, z, VoxelSize, 0, 0f, 0f, 0f, 0f);
    }

    public Vector3 Normal(float x, float y, float z, float voxelSize, int morph, float originX, float originZ, float span, float coarse)
    {
        if (y < Floor)
            return Vector3.up;

        float blend = morph == 0 ? 0f : Weight(morph, (x - originX) / span, (z - originZ) / span);

        float step = math.lerp(voxelSize, coarse, blend) * 0.5f;

        float dx = Height(x + step, z, voxelSize, morph, originX, originZ, span, coarse)
                   - Height(x - step, z, voxelSize, morph, originX, originZ, span, coarse);

        float dz = Height(x, z + step, voxelSize, morph, originX, originZ, span, coarse)
                   - Height(x, z - step, voxelSize, morph, originX, originZ, span, coarse);

        float dy = 2f * step;

        float length = math.sqrt(dx * dx + dy * dy + dz * dz);

        if (length < 1e-6f)
            return Vector3.up;

        return new Vector3(-dx / length, dy / length, -dz / length);
    }

    public float Height(float x, float z, float voxelSize, int morph, float originX, float originZ, float span, float coarse)
    {
        if (morph == 0 && voxelSize <= CellSize)
            return Surface(x, z);

        float own = voxelSize <= CellSize ? Surface(x, z) : Coarse(x, z, voxelSize);

        if (morph == 0)
            return own;

        float weight = Weight(morph, (x - originX) / span, (z - originZ) / span);

        return weight <= 0f ? own : math.lerp(own, Coarse(x, z, coarse), weight);
    }

    public float Coarse(float x, float z, float step)
    {
        float gx = math.floor(x / step) * step;
        float gz = math.floor(z / step) * step;

        float tx = (x - gx) / step;
        float tz = (z - gz) / step;

        float bottom = math.lerp(Surface(gx, gz), Surface(gx + step, gz), tx);
        float top = math.lerp(Surface(gx, gz + step), Surface(gx + step, gz + step), tx);

        return math.lerp(bottom, top, tz);
    }

    public bool Fill(Vector3 origin, float voxelSize, int samples, NativeArray<float> columns, NativeArray<float> density, int morph = 0, int size = 0)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        float step = voxelSize * 2f;

        for (int z = 0; z < samples; z++)
        {
            for (int x = 0; x < samples; x++)
            {
                float worldX = origin.x + (x - 1) * voxelSize;
                float worldZ = origin.z + (z - 1) * voxelSize;

                float surface = Surface(worldX, worldZ);

                if (morph != 0)
                {
                    float weight = Weight(morph, MorphAt(x, size), MorphAt(z, size));

                    if (weight > 0f)
                        surface = math.lerp(surface, Coarse(worldX, worldZ, step), weight);
                }

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
