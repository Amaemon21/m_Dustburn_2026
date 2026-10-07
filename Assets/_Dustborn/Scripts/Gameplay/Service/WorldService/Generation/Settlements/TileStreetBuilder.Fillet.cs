using System.Collections.Generic;
using UnityEngine;

public sealed partial class TileStreetBuilder
{
    public static Vector2[] Fillet(List<Vector2> points, float radius)
    {
        if (points.Count < 3 || radius <= 0f)
            return points.ToArray();

        float[] tangents = DesiredTangents(points, radius);
        ShareLegs(points, tangents);

        var result = new List<Vector2>(points.Count * 4) { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            if (tangents[i] <= 0f)
            {
                result.Add(points[i]);
                continue;
            }

            Vector2 corner = points[i];
            Vector2 d1 = (corner - points[i - 1]).normalized;
            Vector2 d2 = (points[i + 1] - corner).normalized;
            float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
            float tangent = tangents[i];
            float arcRadius = tangent / Mathf.Tan(turn * 0.5f);
            float side = d1.x * d2.y - d1.y * d2.x > 0f ? 1f : -1f;

            Vector2 entry = corner - d1 * tangent;
            Vector2 normal = side > 0f ? new Vector2(-d1.y, d1.x) : new Vector2(d1.y, -d1.x);
            Vector2 center = entry + normal * arcRadius;
            Vector2 arm = entry - center;
            int segments = Mathf.Max(2, Mathf.CeilToInt(turn / ARC_STEP_RADIANS));

            result.Add(entry);

            for (int step = 1; step <= segments; step++)
            {
                float angle = side * turn * step / segments;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);

                result.Add(center + new Vector2(arm.x * cosine - arm.y * sine, arm.x * sine + arm.y * cosine));
            }
        }

        result.Add(points[^1]);

        return result.ToArray();
    }

    private static float[] DesiredTangents(List<Vector2> points, float radius)
    {
        var tangents = new float[points.Count];
        float minTurn = MIN_TURN_DEGREES * Mathf.Deg2Rad;

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 incoming = points[i] - points[i - 1];
            Vector2 outgoing = points[i + 1] - points[i];

            if (incoming.sqrMagnitude < 1e-6f || outgoing.sqrMagnitude < 1e-6f)
                continue;

            float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(incoming.normalized, outgoing.normalized), -1f, 1f));

            if (turn < minTurn || turn > Mathf.PI - minTurn)
                continue;

            tangents[i] = radius * Mathf.Tan(turn * 0.5f);
        }

        return tangents;
    }

    private static void ShareLegs(List<Vector2> points, float[] tangents)
    {
        var limits = (float[])tangents.Clone();

        for (int i = 0; i < points.Count - 1; i++)
        {
            float need = tangents[i] + tangents[i + 1];
            float length = Vector2.Distance(points[i], points[i + 1]);

            if (need <= length)
                continue;

            float scale = length / need;
            limits[i] = Mathf.Min(limits[i], tangents[i] * scale);
            limits[i + 1] = Mathf.Min(limits[i + 1], tangents[i + 1] * scale);
        }

        System.Array.Copy(limits, tangents, tangents.Length);
    }
}
