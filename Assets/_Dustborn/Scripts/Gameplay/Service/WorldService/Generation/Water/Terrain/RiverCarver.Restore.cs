using System.Collections.Generic;
using UnityEngine;

public static partial class RiverCarver
{
    public static void Restore(HeightMap map, WaterMap water, float bridgeWidth, float[] roadMask = null)
    {
        if (water == null || water.Rivers.Count == 0)
            return;

        float cell = (float)map.WorldSize / (map.Resolution - 1);
        float maxHeight = map.MaxHeight;
        int resolution = map.Resolution;
        float[] heights = map.Heights;

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            ChannelCourse course = ChannelCourse.Of(water, r);
            bool looseStart = points.Count >= 2 && RiverEnds.Start(water, r) == RiverEnd.Loose;
            bool looseEnd = points.Count >= 2 && RiverEnds.End(water, r) == RiverEnd.Loose;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i], b = points[i + 1];

                if (a.Submerged && b.Submerged || NearCulvert(water, a.Position, bridgeWidth))
                    continue;

                float half = 0.5f * Mathf.Max(a.Width, b.Width);
                int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - half) / cell));
                int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.x, b.Position.x) + half) / cell));
                int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - half) / cell));
                int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.y, b.Position.y) + half) / cell));
                Vector2 axis = b.Position - a.Position;
                float length = axis.sqrMagnitude;

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var point = new Vector2(x * cell, z * cell);
                        float along = length < 1e-8f ? 0f : Vector2.Dot(point - a.Position, axis) / length;

                        if (looseStart && i == 0 && along < 0f || looseEnd && i + 2 == points.Count && along > 1f)
                            continue;

                        float t = Mathf.Clamp01(along);
                        float distance = Vector2.Distance(point, a.Position + axis * t);
                        float width = course.Half(0.5f * Mathf.Lerp(a.Width, b.Width, t), a.Position + axis * t, Side(axis, point - a.Position), Mathf.Lerp(course.Along[i], course.Along[i + 1], t));

                        if (distance > width)
                            continue;

                        int index = z * resolution + x;

                        if (roadMask != null && roadMask[index] > ROAD_KEEP && !WaterCrossings.OverSpan(water, point))
                            continue;

                        float ground = heights[index] * maxHeight;
                        float target = Profile(distance, width, 0f, Mathf.Lerp(a.Bed, b.Bed, t), Mathf.Lerp(a.Surface, b.Surface, t), ground);

                        if (target < ground)
                            heights[index] = target / maxHeight;
                    }
                }
            }
        }
    }

    private static bool NearCulvert(WaterMap water, Vector2 point, float bridgeWidth)
    {
        foreach (WaterCrossing crossing in water.Crossings)
        {
            if (crossing.Bridge || crossing.RiverWidth >= bridgeWidth)
                continue;

            float reach = crossing.RoadWidth + CROSSING_GUARD;

            if ((crossing.Position - point).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
    }

    private static short[] NearLakes(WaterMap water, out byte[] reached)
    {
        int resolution = water.Resolution;
        var near = new short[water.BodyIds.Length];
        var distance = new int[near.Length];
        var queue = new Queue<int>();
        int reach = LAKE_GUARD_CELLS;

        for (int cell = 0; cell < near.Length; cell++)
        {
            near[cell] = water.BodyIds[cell];
            distance[cell] = near[cell] >= 0 ? 0 : int.MaxValue;

            if (near[cell] >= 0)
                queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();

            if (distance[cell] >= reach)
                continue;

            int cx = cell % resolution, cz = cell / resolution;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, nz = cz + dz;

                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                        continue;

                    int next = nz * resolution + nx;

                    if (distance[next] <= distance[cell] + 1)
                        continue;

                    distance[next] = distance[cell] + 1;
                    near[next] = near[cell];
                    queue.Enqueue(next);
                }
            }
        }

        reached = new byte[near.Length];

        for (int cell = 0; cell < near.Length; cell++)
            reached[cell] = (byte)Mathf.Min(distance[cell], byte.MaxValue);

        return near;
    }
}
