using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private void Confluences(List<RiverCourse> courses, RiverPath[] shapedPaths)
    {
        for (int round = 0; round < CONFLUENCE_ROUNDS; round++)
        {
            bool changed = false;

            for (int c = 0; c < courses.Count; c++)
            {
                RiverCourse course = courses[c];

                if (course.JoinPath < 0 || course.JoinPath >= c || shapedPaths[c] == null || shapedPaths[course.JoinPath] == null)
                    continue;

                RiverPath tributary = shapedPaths[c];
                RiverPath trunk = shapedPaths[course.JoinPath];
                RiverPoint end = tributary.Points[^1];
                int at = NearestPoint(trunk, end.Position);
                float level = trunk.Points[at].Surface;

                if (trunk.Points[at].Submerged)
                    continue;

                if (end.Surface > level + MERGE_TOLERANCE)
                {
                    Join(tributary, level, course.Stamp?.DepthScale);
                    changed = true;
                }
                else if (end.Surface < level - CONFLUENCE_TOLERANCE)
                {
                    changed |= Drawdown(trunk, at, end.Surface);
                }
            }

            if (!changed)
                break;
        }
    }

    private static bool Drawdown(RiverPath trunk, int from, float level)
    {
        List<RiverPoint> points = trunk.Points;
        int last = from;

        while (last + 1 < points.Count && !points[last + 1].Submerged)
            last++;

        if (last + 1 < points.Count && points[last + 1].Surface > level)
            return false;

        for (int i = from; i <= last; i++)
        {
            RiverPoint point = points[i];

            if (point.Surface <= level)
                break;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            points[i] = point;
        }

        for (int i = from - 1; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface <= limit)
                break;

            point.Bed -= point.Surface - limit;
            point.Surface = limit;
            points[i] = point;
        }

        return true;
    }

    private static int NearestPoint(RiverPath path, Vector2 position)
    {
        int best = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < path.Points.Count; i++)
        {
            float distance = (path.Points[i].Position - position).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = i;
        }

        return best;
    }

    private static void Cap(RiverPath path, float level)
    {
        for (int i = 0; i < path.Points.Count; i++)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                continue;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            path.Points[i] = point;
        }
    }

    private void Join(RiverPath path, float joined, float[] depthScale = null)
    {
        float level = joined;
        bool lowered = false;

        for (int i = path.Points.Count - 1; i >= 0; i--)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                break;

            point.Surface = level;
            point.Bed = Mathf.Min(point.Bed, level - ChannelDepth(point.Flow, depthScale == null ? 1f : depthScale[i]));
            path.Points[i] = point;
            lowered = true;

            level += MAX_JOIN_GRADE * (i > 0 ? Vector2.Distance(path.Points[i - 1].Position, point.Position) : 0f);
        }

        if (lowered)
            Pools(path.Points);
    }

    private static float SurfaceNear(RiverPath path, float x, float z)
    {
        float best = float.MaxValue;
        float surface = 0f;
        var point = new Vector2(x, z);

        foreach (RiverPoint candidate in path.Points)
        {
            float distance = (candidate.Position - point).sqrMagnitude;

            if (distance >= best)
                continue;

            best = distance;
            surface = candidate.Surface;
        }

        return surface;
    }
}
