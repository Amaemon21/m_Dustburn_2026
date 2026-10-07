using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class LakeBeds
{
    public const float MAX_DEEPEN = 2f;
    public const float DEEPEN_PER_METRE = 0.08f;
    public const int SHORE_BAND_CELLS = 2;
    public const int SMOOTH_RADIUS = 4;
    public const float KEEP_SIDE = 0.2f;

    public static void Deepen(HeightMap map, WaterMap water)
    {
        if (water == null || water.Bodies.Count == 0)
            return;

        float[] deepen = Field(water);
        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        float[] heights = map.Heights;
        float scale = 1f / map.MaxHeight;

        Parallel.For(0, resolution, row =>
        {
            float z = row * step;

            for (int column = 0; column < resolution; column++)
            {
                float x = column * step;
                float amount = Sample(deepen, water, x, z);

                if (amount <= 0f || !water.Covers(x, z, out short body) || body < 0 || body >= water.Bodies.Count)
                    continue;

                int index = row * resolution + column;

                if (heights[index] < water.Bodies[body].Surface * scale)
                    heights[index] -= amount * scale;
            }
        });
    }

    public static void SmoothShores(HeightMap map, WaterMap water)
    {
        if (water == null || water.Bodies.Count == 0)
            return;

        int[] distance = EdgeDistance(water, out short[] owner);
        float scale = 1f / map.MaxHeight;
        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        float[] source = (float[])map.Heights.Clone();
        float[] heights = map.Heights;

        Parallel.For(0, resolution, row =>
        {
            float z = row * step;

            for (int column = 0; column < resolution; column++)
            {
                int cell = water.CellIndex(column * step, z);

                if (distance[cell] > SHORE_BAND_CELLS)
                    continue;

                float weight = 1f - distance[cell] / (SHORE_BAND_CELLS + 1f);
                float sum = 0f;
                int count = 0;

                for (int dz = -SMOOTH_RADIUS; dz <= SMOOTH_RADIUS; dz++)
                {
                    int r = Math.Clamp(row + dz, 0, resolution - 1) * resolution;

                    for (int dx = -SMOOTH_RADIUS; dx <= SMOOTH_RADIUS; dx++)
                    {
                        sum += source[r + Math.Clamp(column + dx, 0, resolution - 1)];
                        count++;
                    }
                }

                int index = row * resolution + column;
                float smoothed = Mathf.Lerp(source[index], sum / count, weight);
                float level = water.Bodies[owner[cell]].Surface * scale;
                float keep = KEEP_SIDE * scale;

                if (source[index] < level || smoothed > source[index] && water.InsideOtherRiver(-1, new Vector2(column * step, z), WaterMeshes.RIBBON_PAD))
                    continue;

                heights[index] = Mathf.Max(smoothed, Mathf.Min(source[index], level + keep));
            }
        });
    }

    private static int[] EdgeDistance(WaterMap water, out short[] owner)
    {
        int resolution = water.Resolution;
        var distance = new int[water.BodyIds.Length];
        owner = new short[water.BodyIds.Length];
        var queue = new Queue<int>();

        for (int cell = 0; cell < distance.Length; cell++)
        {
            distance[cell] = int.MaxValue;
            short body = water.BodyIds[cell];

            if (body >= 0 && Edge(water, cell, body))
            {
                distance[cell] = 0;
                owner[cell] = body;
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();

            if (distance[cell] >= SHORE_BAND_CELLS)
                continue;

            int x = cell % resolution, z = cell / resolution;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;

                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                        continue;

                    int next = nz * resolution + nx;

                    if (distance[next] <= distance[cell] + 1)
                        continue;

                    distance[next] = distance[cell] + 1;
                    owner[next] = owner[cell];
                    queue.Enqueue(next);
                }
            }
        }

        return distance;
    }

    private static float[] Field(WaterMap water)
    {
        int resolution = water.Resolution;
        var distance = new int[water.Kinds.Length];
        var queue = new Queue<int>();

        for (int cell = 0; cell < distance.Length; cell++)
        {
            bool inside = water.IsFull(cell) && water.CellOwner(cell) >= 0;
            distance[cell] = inside ? int.MaxValue : 0;

            if (!inside)
                queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % resolution, z = cell / resolution;

            Visit(x + 1, z);
            Visit(x - 1, z);
            Visit(x, z + 1);
            Visit(x, z - 1);

            void Visit(int nx, int nz)
            {
                if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                    return;

                int next = nz * resolution + nx;

                if (distance[next] <= distance[cell] + 1)
                    return;

                distance[next] = distance[cell] + 1;
                queue.Enqueue(next);
            }
        }

        var deepen = new float[distance.Length];

        for (int cell = 0; cell < deepen.Length; cell++)
            deepen[cell] = Mathf.Min(MAX_DEEPEN, distance[cell] * water.CellSize * DEEPEN_PER_METRE);

        return deepen;
    }

    private static bool Edge(WaterMap water, int cell, short body)
    {
        int resolution = water.Resolution;
        int x = cell % resolution, z = cell / resolution;

        return x == 0 || z == 0 || x == resolution - 1 || z == resolution - 1
            || water.BodyIds[cell + 1] != body || water.BodyIds[cell - 1] != body
            || water.BodyIds[cell + resolution] != body || water.BodyIds[cell - resolution] != body;
    }

    private static float Sample(float[] field, WaterMap water, float x, float z)
    {
        int resolution = water.Resolution;
        float u = Mathf.Clamp(x / water.CellSize - 0.5f, 0f, resolution - 1);
        float v = Mathf.Clamp(z / water.CellSize - 0.5f, 0f, resolution - 1);
        int x0 = (int)u, z0 = (int)v;
        int x1 = Math.Min(x0 + 1, resolution - 1), z1 = Math.Min(z0 + 1, resolution - 1);
        float fx = u - x0, fz = v - z0;

        float bottom = Mathf.Lerp(field[z0 * resolution + x0], field[z0 * resolution + x1], fx);
        float top = Mathf.Lerp(field[z1 * resolution + x0], field[z1 * resolution + x1], fx);

        return Mathf.Lerp(bottom, top, fz);
    }
}
