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
        if ((morph & VoxelColumnKey.MORPH_CORNERS) != 0)
        {
            float bottom = math.lerp((morph & 16) != 0 ? 1f : 0f, (morph & 32) != 0 ? 1f : 0f, math.saturate(u));
            float top = math.lerp((morph & 64) != 0 ? 1f : 0f, (morph & 128) != 0 ? 1f : 0f, math.saturate(u));
            return math.lerp(bottom, top, math.saturate(v));
        }

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

    public Vector3 MeshVertex(int chunkX, int chunkZ, int size, float voxelSize, int morph, int x, int z)
    {
        float originX = chunkX * size * voxelSize;
        float originZ = chunkZ * size * voxelSize;
        float wx = originX + (x - 0.5f) * voxelSize;
        float wz = originZ + (z - 0.5f) * voxelSize;
        bool edgeX = x == 0 || x == size;
        bool edgeZ = z == 0 || z == size;

        if (edgeX)
        {
            wx = originX + x * voxelSize;

            if (!edgeZ)
            {
                int node = chunkZ * size + z;
                int face = x == 0 ? VoxelColumnKey.MORPH_MIN_X : VoxelColumnKey.MORPH_MAX_X;
                wz = ((morph & face) != 0 ? node & ~1 : node) * voxelSize;
            }
        }

        if (edgeZ)
        {
            wz = originZ + z * voxelSize;

            if (!edgeX)
            {
                int node = chunkX * size + x;
                int face = z == 0 ? VoxelColumnKey.MORPH_MIN_Z : VoxelColumnKey.MORPH_MAX_Z;
                wx = ((morph & face) != 0 ? node & ~1 : node) * voxelSize;
            }
        }

        return new Vector3(wx, Height(wx, wz, voxelSize, morph, originX, originZ, MorphSpan(size, voxelSize), voxelSize * 2f), wz);
    }

    public float Drawn(float x, float z, float voxelSize, int morph, float originX, float originZ, int size, out Vector2 slope)
    {
        float chunk = size * voxelSize;
        int chunkX = (int)math.round(originX / chunk);
        int chunkZ = (int)math.round(originZ / chunk);
        int cellX = DrawnCell((x - originX) / voxelSize, size);
        int cellZ = DrawnCell((z - originZ) / voxelSize, size);

        for (int ring = 0; ring <= 1; ring++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (ring > 0 && dx == 0 && dz == 0)
                        continue;

                    int nearX = cellX + dx;
                    int nearZ = cellZ + dz;

                    if (nearX < 0 || nearZ < 0 || nearX >= size || nearZ >= size)
                        continue;

                    if (DrawnQuad(chunkX, chunkZ, size, voxelSize, morph, nearX, nearZ, x, z, out float height, out slope))
                        return height;
                }
            }
        }

        float span = MorphSpan(size, voxelSize);
        float coarse = voxelSize * 2f;
        float step = voxelSize * 0.5f;

        slope = new Vector2(
            (Height(x + step, z, voxelSize, morph, originX, originZ, span, coarse) - Height(x - step, z, voxelSize, morph, originX, originZ, span, coarse)) / (2f * step),
            (Height(x, z + step, voxelSize, morph, originX, originZ, span, coarse) - Height(x, z - step, voxelSize, morph, originX, originZ, span, coarse)) / (2f * step));

        return Height(x, z, voxelSize, morph, originX, originZ, span, coarse);
    }

    private static int DrawnCell(float local, int size)
    {
        return math.clamp((int)math.floor(local + 0.5f), 0, size - 1);
    }

    private bool DrawnQuad(int chunkX, int chunkZ, int size, float voxelSize, int morph, int cellX, int cellZ, float x, float z, out float height,
        out Vector2 slope)
    {
        Vector3 first = MeshVertex(chunkX, chunkZ, size, voxelSize, morph, cellX, cellZ);
        Vector3 second = MeshVertex(chunkX, chunkZ, size, voxelSize, morph, cellX, cellZ + 1);
        Vector3 third = MeshVertex(chunkX, chunkZ, size, voxelSize, morph, cellX + 1, cellZ + 1);
        Vector3 fourth = MeshVertex(chunkX, chunkZ, size, voxelSize, morph, cellX + 1, cellZ);

        return OnTriangle(first, second, third, x, z, out height, out slope) || OnTriangle(first, third, fourth, x, z, out height, out slope);
    }

    private static bool OnTriangle(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float height, out Vector2 slope)
    {
        const float TOLERANCE = -1e-4f;

        height = 0f;
        slope = Vector2.zero;

        float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);

        if (math.abs(area) < 1e-8f)
            return false;

        float towardB = ((x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z)) / area;
        float towardC = ((b.x - a.x) * (z - a.z) - (x - a.x) * (b.z - a.z)) / area;
        float towardA = 1f - towardB - towardC;

        if (towardA < TOLERANCE || towardB < TOLERANCE || towardC < TOLERANCE)
            return false;

        height = towardA * a.y + towardB * b.y + towardC * c.y;
        slope = new Vector2(
            ((b.y - a.y) * (c.z - a.z) - (b.z - a.z) * (c.y - a.y)) / area,
            ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) / area);

        return true;
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
