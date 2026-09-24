using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class TerrainStampApplier
{
    private const float FLOOR_SHARE = 0.01f;

    private readonly struct Footprint
    {
        public readonly int MinX;
        public readonly int MaxX;
        public readonly int MinY;
        public readonly int MaxY;

        public Footprint(int minX, int maxX, int minY, int maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }
    }

    public static int Apply(float[] heights, int resolution, float worldSize, float maxHeight,
        IReadOnlyList<TerrainStampPlacement> placements, IReadOnlyList<TerrainStampShape> shapes, TerrainStampSettings settings)
    {
        if (placements.Count == 0)
            return 0;

        float cell = worldSize / (resolution - 1);
        var footprints = new Footprint[placements.Count];

        int top = int.MaxValue;
        int bottom = int.MinValue;

        for (int i = 0; i < placements.Count; i++)
        {
            placements[i].Bounds(out Vector2 min, out Vector2 max);

            footprints[i] = new Footprint(
                Math.Max(0, (int)Math.Floor(min.x / cell)), Math.Min(resolution - 1, (int)Math.Ceiling(max.x / cell)),
                Math.Max(0, (int)Math.Floor(min.y / cell)), Math.Min(resolution - 1, (int)Math.Ceiling(max.y / cell)));

            top = Math.Min(top, footprints[i].MinY);
            bottom = Math.Max(bottom, footprints[i].MaxY);
        }

        int touched = 0;

        Parallel.For(top, bottom + 1, () => new List<int>(placements.Count), (row, _, active) =>
        {
            active.Clear();

            int left = int.MaxValue;
            int right = int.MinValue;

            for (int i = 0; i < footprints.Length; i++)
            {
                if (row < footprints[i].MinY || row > footprints[i].MaxY)
                    continue;

                active.Add(i);
                left = Math.Min(left, footprints[i].MinX);
                right = Math.Max(right, footprints[i].MaxX);
            }

            if (active.Count == 0)
                return active;

            float worldZ = row * cell;
            int count = 0;

            for (int column = left; column <= right; column++)
            {
                float worldX = column * cell;

                float raisedMax = 0f, raisedSum = 0f, loweredMax = 0f, loweredSum = 0f;

                foreach (int i in active)
                {
                    if (column < footprints[i].MinX || column > footprints[i].MaxX)
                        continue;

                    TerrainStampPlacement placement = placements[i];
                    float displacement = Displacement(placement, shapes[placement.StampIndex], worldX, worldZ, settings.EdgeSoftness);

                    if (displacement <= 0f)
                        continue;

                    if (placement.Operation == TerrainStampOperation.Add)
                    {
                        raisedMax = Math.Max(raisedMax, displacement);
                        raisedSum += displacement;
                    }
                    else
                    {
                        loweredMax = Math.Max(loweredMax, displacement);
                        loweredSum += displacement;
                    }
                }

                if (raisedSum <= 0f && loweredSum <= 0f)
                    continue;

                float raised = raisedMax + settings.OverlapBlend * (raisedSum - raisedMax);
                float lowered = loweredMax + settings.OverlapBlend * (loweredSum - loweredMax);

                int index = row * resolution + column;
                float before = heights[index] * maxHeight;

                heights[index] = Combine(before, raised, lowered, maxHeight, settings.Ceiling) / maxHeight;
                count++;
            }

            if (count > 0)
                System.Threading.Interlocked.Add(ref touched, count);

            return active;
        }, _ => { });

        return touched;
    }

    public static float Displacement(TerrainStampPlacement placement, TerrainStampShape shape, float worldX, float worldZ, float edgeSoftness = 0f)
    {
        Vector2 local = placement.Local(worldX, worldZ);

        float u = local.x / placement.Size.x + 0.5f;
        float v = local.y / placement.Size.y + 0.5f;

        if (u < 0f || v < 0f || u > 1f || v > 1f)
            return 0f;

        float sample = shape.Sample(u, v);

        if (edgeSoftness > 0f)
            sample *= Smooth(0f, edgeSoftness, Math.Min(Math.Min(u, 1f - u), Math.Min(v, 1f - v)));

        return placement.Amplitude * sample;
    }

    public static float Combine(float before, float raised, float lowered, float maxHeight, float ceilingShare)
    {
        float height = before;

        if (raised > 0f)
        {
            float ceiling = Math.Max(ceilingShare * maxHeight, before);
            float target = before + raised;

            if (target > ceiling)
            {
                float room = Math.Max(1e-3f, maxHeight - ceiling);
                target = ceiling + room * (float)Math.Tanh((target - ceiling) / room);
            }

            height = target;
        }

        if (lowered > 0f)
        {
            float floor = Math.Min(FLOOR_SHARE * maxHeight, height);
            float target = height - lowered;

            if (target < floor)
            {
                float room = Math.Max(1e-3f, floor);
                target = floor - room * (float)Math.Tanh((floor - target) / room);
            }

            height = target;
        }

        return Math.Clamp(height, 0f, maxHeight);
    }

    private static float Smooth(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);

        return t * t * (3f - 2f * t);
    }
}
