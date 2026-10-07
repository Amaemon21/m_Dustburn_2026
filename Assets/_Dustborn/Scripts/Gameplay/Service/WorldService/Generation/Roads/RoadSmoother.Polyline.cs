using System.Collections.Generic;
using UnityEngine;

public partial class RoadSmoother
{
    public static Vector2[] Simplify(Vector2[] points, float tolerance)
    {
        if (points.Length < 3 || tolerance <= 0f)
            return points;

        var keep = new bool[points.Length];

        keep[0] = true;
        keep[^1] = true;

        Divide(points, keep, 0, points.Length - 1, tolerance);

        var result = new List<Vector2>(points.Length);

        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }

        return result.ToArray();
    }

    private static void Divide(Vector2[] points, bool[] keep, int from, int to, float tolerance)
    {
        if (to - from < 2)
            return;

        Vector2 axis = points[to] - points[from];
        float length = axis.magnitude;

        int worst = -1;
        float worstDistance = tolerance;

        for (int i = from + 1; i < to; i++)
        {
            float distance = length <= MIN_SEGMENT
                ? Vector2.Distance(points[i], points[from])
                : Mathf.Abs((points[i].x - points[from].x) * axis.y - (points[i].y - points[from].y) * axis.x) / length;

            if (distance <= worstDistance)
                continue;

            worstDistance = distance;
            worst = i;
        }

        if (worst < 0)
            return;

        keep[worst] = true;

        Divide(points, keep, from, worst, tolerance);
        Divide(points, keep, worst, to, tolerance);
    }

    public static Vector2[] Chaikin(Vector2[] points, int passes)
    {
        Vector2[] current = points;

        for (int pass = 0; pass < passes; pass++)
        {
            if (current.Length < 3)
                break;

            var next = new Vector2[(current.Length - 1) * 2];
            int index = 0;

            next[index++] = current[0];

            for (int i = 1; i < current.Length - 1; i++)
            {
                next[index++] = current[i - 1] * 0.25f + current[i] * 0.75f;
                next[index++] = current[i] * 0.75f + current[i + 1] * 0.25f;
            }

            next[index] = current[^1];
            current = next;
        }

        return current;
    }

    public static Vector2[] Resample(Vector2[] points, float spacing)
    {
        if (points.Length < 2 || spacing <= 0f)
            return points;

        var result = new List<Vector2> { points[0] };

        float travelled = 0f;
        float target = spacing;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 from = points[i];
            Vector2 to = points[i + 1];

            float length = Vector2.Distance(from, to);

            if (length <= MIN_SEGMENT)
                continue;

            while (target <= travelled + length)
            {
                result.Add(Vector2.Lerp(from, to, (target - travelled) / length));
                target += spacing;
            }

            travelled += length;
        }

        Vector2 last = points[^1];

        if (result.Count > 1 && Vector2.Distance(result[^1], last) < spacing * 0.5f)
            result[^1] = last;
        else
            result.Add(last);

        return result.ToArray();
    }

    public static Vector2[] ResampleEven(Vector2[] points, float spacing)
    {
        if (points.Length < 2 || spacing <= 0f)
            return points;

        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        float total = distance[^1];

        if (total <= MIN_SEGMENT)
            return new[] { points[0], points[^1] };

        int count = Mathf.Max(1, Mathf.CeilToInt(total / spacing - SPACING_EPSILON));
        var result = new Vector2[count + 1];
        int segment = 0;

        result[0] = points[0];

        for (int k = 1; k < count; k++)
        {
            float target = total * k / count;

            while (segment < points.Length - 2 && distance[segment + 1] < target)
                segment++;

            float span = distance[segment + 1] - distance[segment];
            float t = span > 1e-6f ? (target - distance[segment]) / span : 0f;

            result[k] = Vector2.Lerp(points[segment], points[segment + 1], t);
        }

        result[count] = points[^1];

        return result;
    }

    public static Vector2[] EnforceRadius(Vector2[] points, float minRadius, float pinStart, float pinEnd, int passes)
    {
        if (points.Length < 3 || minRadius <= 0f)
            return points;

        var result = (Vector2[])points.Clone();
        var distance = new float[result.Length];

        for (int pass = 0; pass < passes; pass++)
        {
            distance[0] = 0f;

            for (int i = 1; i < result.Length; i++)
                distance[i] = distance[i - 1] + Vector2.Distance(result[i - 1], result[i]);

            float total = distance[^1];
            bool changed = false;

            for (int i = 1; i < result.Length - 1; i++)
            {
                if (Circumradius(result[i - 1], result[i], result[i + 1]) >= minRadius)
                    continue;

                bool pinnedStart = distance[i] < pinStart;
                bool pinnedEnd = total - distance[i] < pinEnd;

                if (pinnedStart && !pinnedEnd && i + 1 < result.Length - 1 && distance[i + 1] >= pinStart)
                    changed |= PullTowardTangent(result, i, i - 1, i + 1);

                if (pinnedEnd && !pinnedStart && i - 1 > 0 && total - distance[i - 1] >= pinEnd)
                    changed |= PullTowardTangent(result, i, i + 1, i - 1);

                if (pinnedStart || pinnedEnd)
                    continue;

                result[i] = Vector2.Lerp(result[i], (result[i - 1] + result[i + 1]) * 0.5f, RADIUS_STEP);
                changed = true;
            }

            if (!changed)
                break;
        }

        return result;
    }

    private static bool PullTowardTangent(Vector2[] points, int pinned, int behind, int free)
    {
        Vector2 back = points[pinned] - points[behind];

        if (back.sqrMagnitude < 1e-4f)
            return false;

        Vector2 target = points[pinned] + back.normalized * Vector2.Distance(points[pinned], points[free]);
        points[free] = Vector2.Lerp(points[free], target, RADIUS_STEP);

        return true;
    }

    public static float MinRadius(Vector2[] points, float spacing, float skipStart, float skipEnd)
    {
        if (points == null || points.Length < 3)
            return float.MaxValue;

        Vector2[] even = Resample(points, spacing);
        float total = 0f;

        for (int i = 1; i < even.Length; i++)
            total += Vector2.Distance(even[i - 1], even[i]);

        float travelled = 0f;
        float smallest = float.MaxValue;

        for (int i = 1; i < even.Length - 1; i++)
        {
            travelled += Vector2.Distance(even[i - 1], even[i]);

            if (travelled < skipStart || total - travelled < skipEnd)
                continue;

            smallest = Mathf.Min(smallest, Circumradius(even[i - 1], even[i], even[i + 1]));
        }

        return smallest;
    }

    public static float Circumradius(Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Vector2.Distance(a, b);
        float bc = Vector2.Distance(b, c);
        float ca = Vector2.Distance(c, a);
        float doubleArea = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));

        if (doubleArea <= 1e-6f)
            return float.MaxValue;

        return ab * bc * ca / (2f * doubleArea);
    }

    public static Vector2[] RoundCorners(Vector2[] points, float radius)
    {
        if (points.Length < 3 || radius <= 0f)
            return points;

        var result = new List<Vector2>(points.Length * 3) { points[0] };

        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector2 previous = points[i - 1];
            Vector2 corner = points[i];
            Vector2 next = points[i + 1];

            Vector2 back = previous - corner;
            Vector2 forward = next - corner;

            float backLength = back.magnitude;
            float forwardLength = forward.magnitude;

            if (backLength <= MIN_SEGMENT || forwardLength <= MIN_SEGMENT)
            {
                result.Add(corner);
                continue;
            }

            float reach = Mathf.Min(radius, backLength * 0.5f, forwardLength * 0.5f);

            Vector2 entry = corner + back / backLength * reach;
            Vector2 exit = corner + forward / forwardLength * reach;

            result.Add(entry);
            result.Add(Vector2.Lerp(Vector2.Lerp(entry, corner, 0.5f), Vector2.Lerp(corner, exit, 0.5f), 0.5f));
            result.Add(exit);
        }

        result.Add(points[^1]);

        return result.ToArray();
    }
}
