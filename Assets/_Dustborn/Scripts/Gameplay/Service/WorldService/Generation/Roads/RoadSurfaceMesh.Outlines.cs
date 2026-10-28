using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const int CAP_STEPS = 8;
    private const float MAX_MITRE_TURN = 40f;
    private const float CONVEX_TOLERANCE = 0.0001f;

    private static void Shape(Chain chain)
    {
        List<Vector2> points = chain.Points;
        int count = points.Count - 1;
        var mitred = new bool[points.Count];
        float along = 0f;

        for (int k = 0; k < count; k++)
        {
            Vector2 delta = points[k + 1] - points[k];
            float length = delta.magnitude;
            Vector2 direction = delta / length;
            chain.Segments.Add(new Segment
            {
                From = points[k], To = points[k + 1], Direction = direction, Side = Perp(direction), Length = length, Along = along,
                StartWidth = chain.Widths[k], EndWidth = chain.Widths[k + 1], Style = chain.Styles[k]
            });
            chain.Rank = Mathf.Max(chain.Rank, chain.Styles[k]);
            along += length;
        }

        for (int i = 1; i < count; i++)
            mitred[i] = Turn(chain.Segments[i - 1].Direction, chain.Segments[i].Direction) <= MAX_MITRE_TURN;

        if (chain.Closed)
            mitred[0] = mitred[count] = Turn(chain.Segments[count - 1].Direction, chain.Segments[0].Direction) <= MAX_MITRE_TURN;

        do
        {
            Outline(chain, mitred);
        }
        while (Unbend(chain, mitred));

        foreach (Segment segment in chain.Segments)
        {
            segment.Min = segment.Boundary[0];
            segment.Max = segment.Boundary[0];

            foreach (Vector2 corner in segment.Boundary)
            {
                segment.Min = Vector2.Min(segment.Min, corner);
                segment.Max = Vector2.Max(segment.Max, corner);
            }
        }
    }

    private static void Outline(Chain chain, bool[] mitred)
    {
        List<Segment> segments = chain.Segments;
        int count = segments.Count;

        for (int k = 0; k < count; k++)
        {
            Segment segment = segments[k];
            segment.StartNormal = mitred[k] ? (segments[(k + count - 1) % count].Direction + segment.Direction).normalized : segment.Direction;
            segment.EndNormal = mitred[k + 1] ? -(segment.Direction + segments[(k + 1) % count].Direction).normalized : -segment.Direction;
            segment.Previous = mitred[k] ? (k + count - 1) % count : -1;
            segment.Next = mitred[k + 1] ? (k + 1) % count : -1;
            segment.Boundary = Outline(segment, mitred[k], mitred[k + 1]);
        }
    }

    private static bool Unbend(Chain chain, bool[] mitred)
    {
        List<Segment> segments = chain.Segments;
        int count = segments.Count;
        bool changed = false;

        for (int k = 0; k < count; k++)
        {
            if ((!mitred[k] && !mitred[k + 1]) || Convex(segments[k].Boundary))
                continue;

            Unmitre(chain, mitred, k);
            Unmitre(chain, mitred, k + 1);
            changed = true;
        }

        return changed;
    }

    private static void Unmitre(Chain chain, bool[] mitred, int joint)
    {
        mitred[joint] = false;

        if (chain.Closed && (joint == 0 || joint == mitred.Length - 1))
            mitred[0] = mitred[^1] = false;
    }

    private static Vector2[] Outline(Segment segment, bool mitredStart, bool mitredEnd)
    {
        var corners = new List<Vector2>(4 + 2 * CAP_STEPS);
        Vector2 start = Perp(segment.StartNormal) * (segment.StartWidth / Vector2.Dot(segment.StartNormal, segment.Direction));
        Vector2 end = Perp(-segment.EndNormal) * (segment.EndWidth / Vector2.Dot(-segment.EndNormal, segment.Direction));
        corners.Add(segment.From - start);
        corners.Add(segment.To - end);

        if (!mitredEnd)
            Cap(corners, segment.To, segment.Direction, segment.Side, segment.EndWidth);

        corners.Add(segment.To + end);
        corners.Add(segment.From + start);

        if (!mitredStart)
            Cap(corners, segment.From, -segment.Direction, -segment.Side, segment.StartWidth);

        return corners.ToArray();
    }

    private static void Cap(List<Vector2> corners, Vector2 centre, Vector2 forward, Vector2 side, float width)
    {
        for (int step = 1; step < CAP_STEPS; step++)
        {
            float angle = Mathf.PI * ((float)step / CAP_STEPS - 0.5f);
            corners.Add(centre + forward * (Mathf.Cos(angle) * width) + side * (Mathf.Sin(angle) * width));
        }
    }

    private static bool Convex(Vector2[] polygon)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            Vector2 c = polygon[(i + 2) % polygon.Length];

            if (Cross(b - a, c - b) < -CONVEX_TOLERANCE)
                return false;
        }

        return true;
    }

    private static Vector2 Perp(Vector2 direction) => new(-direction.y, direction.x);

    private static float Turn(Vector2 from, Vector2 to) => Mathf.Acos(Mathf.Clamp(Vector2.Dot(from, to), -1f, 1f)) * Mathf.Rad2Deg;
}
