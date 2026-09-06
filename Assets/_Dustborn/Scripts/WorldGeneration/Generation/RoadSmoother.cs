using System.Collections.Generic;
using UnityEngine;

public class RoadSmoother
{
    private const float MIN_SEGMENT = 0.05f;

    private readonly HeightMap _map;

    public float SimplifyTolerance { get; set; } = 12f;
    public int ChaikinPasses { get; set; } = 3;
    public int RelaxPasses { get; set; } = 20;
    public float RelaxStrength { get; set; } = 0.4f;
    public float Corridor { get; set; } = 40f;
    public float MaxGrade { get; set; } = 0.1f;
    public float Spacing { get; set; } = 12f;

    public RoadSmoother(HeightMap map)
    {
        _map = map;
    }

    public Vector2[] Smooth(Vector2[] points)
    {
        if (points == null || points.Length < 3)
            return points;

        Vector2[] anchors = Simplify(points, SimplifyTolerance);
        Vector2[] curve = Resample(Chaikin(anchors, ChaikinPasses), Spacing);

        Relax(curve);

        return Resample(curve, Spacing);
    }

    private void Relax(Vector2[] points)
    {
        if (points.Length < 3 || RelaxPasses <= 0 || RelaxStrength <= 0f)
            return;

        var anchors = (Vector2[])points.Clone();

        for (int pass = 0; pass < RelaxPasses; pass++)
        {
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 target = (points[i - 1] + points[i + 1]) * 0.5f;
                Vector2 moved = Vector2.Lerp(points[i], target, RelaxStrength);

                moved = ClampToCorridor(anchors[i], moved);

                if (IsSteeper(points[i - 1], points[i], points[i + 1], moved))
                    continue;

                points[i] = moved;
            }
        }
    }

    private Vector2 ClampToCorridor(Vector2 anchor, Vector2 moved)
    {
        Vector2 delta = moved - anchor;

        if (delta.sqrMagnitude <= Corridor * Corridor)
            return moved;

        return anchor + delta.normalized * Corridor;
    }

    private bool IsSteeper(Vector2 previous, Vector2 current, Vector2 next, Vector2 moved)
    {
        float before = Mathf.Max(Grade(previous, current), Grade(current, next));
        float after = Mathf.Max(Grade(previous, moved), Grade(moved, next));

        return after > MaxGrade && after > before;
    }

    private float Grade(Vector2 from, Vector2 to)
    {
        float distance = Vector2.Distance(from, to);

        if (distance <= MIN_SEGMENT)
            return 0f;

        float fromHeight = _map.SampleWorld(new Vector3(from.x, 0f, from.y));
        float toHeight = _map.SampleWorld(new Vector3(to.x, 0f, to.y));

        return Mathf.Abs(toHeight - fromHeight) / distance;
    }

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
