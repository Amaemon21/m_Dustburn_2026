using System.Collections.Generic;
using UnityEngine;

public static partial class WaterShore
{
    private static bool Resurface(WaterMap water)
    {
        bool changed = false;
        var ranges = new List<(int First, int Last)>();

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (!point.Submerged)
                    continue;

                if (water.Covers(point.Position.x, point.Position.y, out short owner) && owner != WaterMap.OWNER_NONE)
                {
                    float level = water.LevelOf(owner);

                    if (Mathf.Abs(level - point.Surface) <= RESURFACE_TOLERANCE)
                        continue;

                    if (level < point.Surface)
                    {
                        ranges.Add(Settle(points, i, level));
                        changed = true;
                        continue;
                    }
                }

                if (water.InsideEarlierRiver(river, point.Position, -0.5f * point.Width, out float trunk) && point.Surface <= trunk + Hydrology.MERGE_TOLERANCE)
                    continue;

                point.Submerged = false;
                points[i] = point;
                changed = true;

                if (ranges.Count > 0 && ranges[^1].Last >= i)
                    ranges[^1] = (ranges[^1].First, Mathf.Max(ranges[^1].Last, Mathf.Min(points.Count - 1, i + 1)));
                else
                    ranges.Add((Mathf.Max(0, i - 1), Mathf.Min(points.Count - 1, i + 1)));
            }

            if (ranges.Count > 0)
                Hydrology.Reprofile(points, ranges);

            ranges.Clear();
        }

        if (changed)
            water.InvalidateIndex();

        return changed;
    }

    private static void TaperSources(WaterMap water)
    {
        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            if (points.Count == 0 || points[0].Submerged || water.StandingAt(points[0].Position.x, points[0].Position.y, out _))
                continue;

            float limit = Hydrology.SOURCE_WIDTH;

            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    limit += Hydrology.WIDTH_RAMP * Vector2.Distance(points[i - 1].Position, points[i].Position);

                if (points[i].Width <= limit)
                    break;

                RiverPoint point = points[i];
                point.Width = limit;
                points[i] = point;
            }
        }

        water.InvalidateIndex();
    }

    private static bool DropDeadRivers(WaterMap water)
    {
        var removed = new HashSet<int>();

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            if (points.Count > 0 && water.Covers(points[0].Position.x, points[0].Position.y, out short owner) && owner == WaterMap.OWNER_SEA || OpenLength(water, points) < MIN_OPEN_LENGTH)
                removed.Add(river);
        }

        water.RemoveRivers(removed);

        return removed.Count > 0;
    }

    private static float OpenLength(WaterMap water, List<RiverPoint> points)
    {
        float length = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            if (water.RiverOpen(points[i - 1]) && water.RiverOpen(points[i]))
                length += Vector2.Distance(points[i - 1].Position, points[i].Position);
        }

        return length;
    }

    private static (int First, int Last) Settle(List<RiverPoint> points, int i, float level)
    {
        RiverPoint point = points[i];
        float was = point.Surface;
        point.Bed -= point.Surface - level;
        point.Surface = level;
        points[i] = point;

        int first = i, last = i;

        while (first > 0 && Mathf.Abs(points[first - 1].Surface - was) <= RESURFACE_TOLERANCE)
        {
            RiverPoint same = points[first - 1];
            same.Bed -= same.Surface - level;
            same.Surface = level;
            points[--first] = same;
        }

        for (int k = i + 1; k < points.Count; k++)
        {
            RiverPoint downstream = points[k];

            if (downstream.Surface <= level)
                break;

            downstream.Bed -= downstream.Surface - level;
            downstream.Surface = level;
            points[k] = downstream;
            last = k;
        }

        for (int k = first - 1; k >= 0; k--)
        {
            RiverPoint upstream = points[k];

            if (upstream.Submerged)
                break;

            float limit = points[k + 1].Surface + SETTLE_GRADE * Vector2.Distance(upstream.Position, points[k + 1].Position);

            if (upstream.Surface <= limit)
                break;

            upstream.Bed -= upstream.Surface - limit;
            upstream.Surface = limit;
            points[k] = upstream;
            first = k;
        }

        return (first, last);
    }
}
