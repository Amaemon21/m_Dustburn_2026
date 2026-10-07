using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampComposer
{
    private Piece Stamped(WaterStampDefinition definition, WaterStampPlacement placement, int placementIndex, Line line, float from, float to, Vector2[] path, int first, int last)
    {
        var piece = new Piece();
        float total = 0f;

        for (int k = first + 1; k <= last; k++)
            total += Vector2.Distance(path[k - 1], path[k]);

        float travelled = 0f;
        float nominal = Mathf.Min(WIDTH_SCALE_MAX, definition.NominalChannelWidth / _library.ReferenceWidth);
        float median = Mathf.Max(1e-3f, definition.MedianCoreHalfWidth);
        float depth = Mathf.Clamp(definition.RecommendedDepth / _referenceDepth, DEPTH_SCALE_MIN, DEPTH_SCALE_MAX);

        for (int k = first; k <= last; k++)
        {
            if (k > first)
                travelled += Vector2.Distance(path[k - 1], path[k]);

            float t = total < 1e-6f ? 0f : travelled / total;
            float profile = path == definition.Centerline ? Mathf.Clamp(definition.CoreHalfWidths[k] / median, WIDTH_PROFILE_MIN, WIDTH_PROFILE_MAX) : 1f;

            piece.Nodes.Add(new Node
            {
                Point = placement.World(path[k]),
                Area = line.AreaAt(Mathf.Lerp(from, to, t)),
                Placement = placementIndex,
                Stamp = path[k],
                WidthScale = nominal * profile,
                DepthScale = depth
            });
        }

        _composedArc += total * placement.Scale;
        return piece;
    }

    private static Piece Copy(Line line, int first, int last)
    {
        var piece = new Piece();

        for (int i = first; i <= last; i++)
            piece.Nodes.Add(new Node { Point = line.Points[i], Area = line.Areas[i], Placement = -1, WidthScale = 1f, DepthScale = 1f });

        return piece;
    }

    private static Piece Straight(Vector2 from, Vector2 to, float area)
    {
        var piece = new Piece();
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / Hydrology.RESAMPLE_STEP));

        for (int i = 0; i <= steps; i++)
            piece.Nodes.Add(new Node { Point = Vector2.Lerp(from, to, i / (float)steps), Area = area, Placement = -1, WidthScale = 1f, DepthScale = 1f });

        return piece;
    }

    private List<Node> Assemble(List<Piece> pieces)
    {
        var nodes = new List<Node>();
        var joins = new List<int>();

        foreach (Piece piece in pieces)
        {
            if (piece.Nodes.Count == 0)
                continue;

            int skip = nodes.Count > 0 && (nodes[^1].Point - piece.Nodes[0].Point).sqrMagnitude < 0.01f ? 1 : 0;

            if (nodes.Count > 0)
                joins.Add(nodes.Count - 1);

            for (int i = skip; i < piece.Nodes.Count; i++)
                nodes.Add(piece.Nodes[i]);
        }

        foreach (int join in joins)
            Blend(nodes, join);

        return nodes;
    }

    private void Blend(List<Node> nodes, int join)
    {
        if (join <= 0 || join >= nodes.Count - 1 || _settings.BlendDistance <= 0f)
            return;

        int before = join, after = join;
        float back = 0f, ahead = 0f;

        while (before > 0 && back < _settings.BlendDistance)
        {
            back += Vector2.Distance(nodes[before - 1].Point, nodes[before].Point);
            before--;
        }

        while (after < nodes.Count - 1 && ahead < _settings.BlendDistance)
        {
            ahead += Vector2.Distance(nodes[after + 1].Point, nodes[after].Point);
            after++;
        }

        if (after - before < 3)
            return;

        float total = back + ahead;
        Vector2 t0 = Tangent(nodes, before) * total;
        Vector2 t1 = Tangent(nodes, after) * total;
        var shares = new float[after - before + 1];

        for (int i = before + 1; i <= after; i++)
            shares[i - before] = shares[i - before - 1] + Vector2.Distance(nodes[i - 1].Point, nodes[i].Point);

        Vector2 p0 = nodes[before].Point;
        Vector2 p1 = nodes[after].Point;

        for (int i = before + 1; i < after; i++)
        {
            Node node = nodes[i];
            node.Point = Hermite(p0, t0, p1, t1, shares[i - before] / Mathf.Max(1e-3f, shares[^1]));
            nodes[i] = node;
        }
    }

    private static Vector2 Hermite(Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1, float s)
    {
        s = Mathf.Clamp01(s);
        float s2 = s * s, s3 = s2 * s;

        return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * t0 + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * t1;
    }

    private static Vector2 Tangent(List<Node> nodes, int i)
    {
        Vector2 direction = nodes[Mathf.Min(nodes.Count - 1, i + 1)].Point - nodes[Mathf.Max(0, i - 1)].Point;

        return direction.sqrMagnitude < 1e-10f ? new Vector2(1f, 0f) : direction.normalized;
    }

    private WaterStampCourse Build(List<Node> nodes)
    {
        float step = Hydrology.RESAMPLE_STEP;
        var result = new List<Node> { nodes[0] };
        float carry = 0f;

        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            Node a = nodes[i], b = nodes[i + 1];
            float length = Vector2.Distance(a.Point, b.Point);

            if (length < 1e-4f)
                continue;

            float along = step - carry;

            while (along < length)
            {
                result.Add(Mix(a, b, along / length));
                along += step;
            }

            carry = length - (along - step);
        }

        if (Vector2.Distance(result[^1].Point, nodes[^1].Point) > 1e-3f)
            result.Add(nodes[^1]);

        int count = result.Count;
        var course = new WaterStampCourse
        {
            Points = new List<Vector2>(count),
            Areas = new List<float>(count),
            WidthScale = new float[count],
            DepthScale = new float[count],
            Track = new WaterStampTrack(count)
        };

        float area = 0f;

        for (int i = 0; i < count; i++)
        {
            area = Mathf.Max(area, result[i].Area);
            course.Points.Add(result[i].Point);
            course.Areas.Add(area);
            course.Track.Placement[i] = result[i].Placement;
            course.Track.Stamp[i] = result[i].Stamp;
            course.WidthScale[i] = result[i].WidthScale;
            course.DepthScale[i] = result[i].DepthScale;
        }

        int window = Mathf.Max(1, Mathf.RoundToInt(_settings.BlendDistance / step));
        Smooth(course.WidthScale, window);
        Smooth(course.DepthScale, window);

        return course;
    }

    private static Node Mix(Node a, Node b, float t)
    {
        bool same = a.Placement == b.Placement;
        Node near = t < 0.5f ? a : b;

        return new Node
        {
            Point = Vector2.Lerp(a.Point, b.Point, t),
            Area = Mathf.Lerp(a.Area, b.Area, t),
            Placement = near.Placement,
            Stamp = same ? Vector2.Lerp(a.Stamp, b.Stamp, t) : near.Stamp,
            WidthScale = Mathf.Lerp(a.WidthScale, b.WidthScale, t),
            DepthScale = Mathf.Lerp(a.DepthScale, b.DepthScale, t)
        };
    }

    private static void Smooth(float[] values, int radius)
    {
        var copy = (float[])values.Clone();

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int taps = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;

                if (j < 0 || j >= copy.Length)
                    continue;

                sum += copy[j];
                taps++;
            }

            values[i] = sum / taps;
        }
    }
}
