using System.Collections.Generic;
using UnityEngine;

public static partial class WaterMeshes
{
    private static void Rivers(WaterMap water, Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts)
    {
        var drawn = new RibbonCover();

        for (int source = 0; source < water.Rivers.Count; source++)
        {
            drawn.Begin(source);
            List<RiverPoint> points = Extend(water, water.Rivers[source].Points);
            int columns = Columns(points);
            float travelled = 0f;
            int previous = -1;
            WaterMeshPart previousPart = null;

            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    travelled += Vector2.Distance(points[i - 1].Position, points[i].Position);

                if (!OpenNear(water, points, i))
                {
                    previous = -1;
                    continue;
                }

                WaterMeshPart part = Part(parts, WaterKind.River, points[i].Position, water.FrozenAt(points[i].Position.x, points[i].Position.y));

                if (part != previousPart && previous >= 0)
                    previous = Section(part, points, i - 1, columns, travelled - Vector2.Distance(points[i - 1].Position, points[i].Position));

                int current = Section(part, points, i, columns, travelled);

                if (previous >= 0)
                {
                    for (int k = 0; k < columns; k++)
                    {
                        Triangle(part, water, drawn, previous + k, current + k, previous + k + 1);
                        Triangle(part, water, drawn, previous + k + 1, current + k, current + k + 1);
                    }
                }

                previous = current;
                previousPart = part;
            }
        }
    }

    private static int Columns(List<RiverPoint> points)
    {
        float widest = 0f;

        foreach (RiverPoint point in points)
            widest = Mathf.Max(widest, point.Width + 2f * RIBBON_PAD);

        return Mathf.Max(1, Mathf.CeilToInt(widest / COLUMN));
    }

    private static List<RiverPoint> Extend(WaterMap water, List<RiverPoint> points)
    {
        if (points.Count < 2)
            return points;

        bool start = AtBorder(water, points[0]);
        bool end = AtBorder(water, points[^1]);

        if (!start && !end)
            return points;

        var extended = new List<RiverPoint>(points);

        if (start)
        {
            RiverPoint first = points[0];
            first.Position = Inside(water, first.Position - Tangent(points, 0) * first.Width);
            extended.Insert(0, first);
        }

        if (end)
        {
            RiverPoint last = points[^1];
            last.Position = Inside(water, last.Position + Tangent(points, points.Count - 1) * last.Width);
            extended.Add(last);
        }

        return extended;
    }

    private static bool AtBorder(WaterMap water, RiverPoint point)
    {
        float reach = point.Width;
        Vector2 p = point.Position;

        return p.x <= reach || p.y <= reach || p.x >= water.WorldSize - reach || p.y >= water.WorldSize - reach;
    }

    private static Vector2 Inside(WaterMap water, Vector2 point)
    {
        return new Vector2(Mathf.Clamp(point.x, 0f, water.WorldSize), Mathf.Clamp(point.y, 0f, water.WorldSize));
    }

    private static bool OpenNear(WaterMap water, List<RiverPoint> points, int i)
    {
        for (int k = Mathf.Max(0, i - MOUTH_REACH); k <= Mathf.Min(points.Count - 1, i + MOUTH_REACH); k++)
        {
            if (Open(water, points[k]))
                return true;
        }

        return false;
    }

    private static bool Open(WaterMap water, RiverPoint point)
    {
        return water.RiverOpen(point);
    }

    private static int Section(WaterMeshPart part, List<RiverPoint> points, int i, int columns, float travelled)
    {
        RiverPoint point = points[i];
        Vector2 tangent = Tangent(points, i);
        var normal = new Vector2(-tangent.y, tangent.x);
        float half = point.Width * 0.5f + RIBBON_PAD;
        float v = travelled / Mathf.Max(1f, point.Width);
        int index = part.Vertices.Count;

        for (int k = 0; k <= columns; k++)
        {
            float u = k / (float)columns;
            Vector2 at = point.Position + normal * Mathf.Lerp(half, -half, u);

            part.Vertices.Add(new Vector3(at.x, point.Surface, at.y));
            part.Uv.Add(new Vector2(u, v));
        }

        return index;
    }

    private static void Triangle(WaterMeshPart part, WaterMap water, RibbonCover drawn, int a, int b, int c, int depth = 0)
    {
        Vector3 first = part.Vertices[a];
        Vector3 second = part.Vertices[b];
        Vector3 third = part.Vertices[c];

        float facing = (second.z - first.z) * (third.x - first.x) - (second.x - first.x) * (third.z - first.z);

        if (facing <= 0f)
            return;

        bool coveredA = Covered(water, drawn, first, out _);
        bool coveredB = Covered(water, drawn, second, out _);
        bool coveredC = Covered(water, drawn, third, out _);

        if (depth < SPLIT_DEPTH && (coveredA != coveredB || coveredB != coveredC || Contradicts(water, drawn, first, second, third, coveredA)))
        {
            int ab = Midpoint(part, a, b), bc = Midpoint(part, b, c), ca = Midpoint(part, c, a);

            Triangle(part, water, drawn, a, ab, ca, depth + 1);
            Triangle(part, water, drawn, ab, b, bc, depth + 1);
            Triangle(part, water, drawn, ca, bc, c, depth + 1);
            Triangle(part, water, drawn, ab, bc, ca, depth + 1);
            return;
        }

        if (!coveredA && !coveredB && !coveredC)
        {
            Emit(part, drawn, a, b, c);
            return;
        }

        if (coveredA && coveredB && coveredC)
            return;

        var polygon = new List<int>(4);
        (int Index, bool Covered)[] corners = { (a, coveredA), (b, coveredB), (c, coveredC) };

        for (int k = 0; k < 3; k++)
        {
            (int from, bool fromCovered) = corners[k];
            (int to, bool toCovered) = corners[(k + 1) % 3];

            if (!fromCovered)
                polygon.Add(from);

            if (fromCovered != toCovered)
                polygon.Add(fromCovered ? Crossing(part, water, drawn, to, from) : Crossing(part, water, drawn, from, to));
        }

        for (int k = 1; k + 1 < polygon.Count; k++)
            Emit(part, drawn, polygon[0], polygon[k], polygon[k + 1]);
    }

    private static bool Contradicts(WaterMap water, RibbonCover drawn, Vector3 a, Vector3 b, Vector3 c, bool covered)
    {
        if (!covered)
        {
            return Covered(water, drawn, 0.5f * (a + b), out _) || Covered(water, drawn, 0.5f * (b + c), out _)
                || Covered(water, drawn, 0.5f * (c + a), out _) || Covered(water, drawn, (a + b + c) / 3f, out _);
        }

        for (int i = 0; i <= COVER_LATTICE; i++)
        {
            for (int j = 0; i + j <= COVER_LATTICE; j++)
            {
                Vector3 probe = (a * i + b * j + c * (COVER_LATTICE - i - j)) / COVER_LATTICE;

                if (!Covered(water, drawn, probe, out _))
                    return true;
            }
        }

        return false;
    }

    private static int Midpoint(WaterMeshPart part, int a, int b)
    {
        int index = part.Vertices.Count;
        part.Vertices.Add(0.5f * (part.Vertices[a] + part.Vertices[b]));
        part.Uv.Add(0.5f * (part.Uv[a] + part.Uv[b]));

        return index;
    }

    private static int Crossing(WaterMeshPart part, WaterMap water, RibbonCover drawn, int open, int covered)
    {
        Vector3 dry = part.Vertices[open];
        Vector3 wet = part.Vertices[covered];
        Vector2 uvDry = part.Uv[open];
        Vector2 uvWet = part.Uv[covered];
        float low = 0f, high = 1f;
        float level = float.NaN;

        for (int step = 0; step < CROSSING_STEPS; step++)
        {
            float middle = 0.5f * (low + high);

            if (Covered(water, drawn, Vector3.Lerp(dry, wet, middle), out float surface))
            {
                high = middle;
                level = surface;
            }
            else
            {
                low = middle;
            }
        }

        if (float.IsNaN(level))
            Covered(water, drawn, wet, out level);

        Vector3 point = Vector3.Lerp(dry, wet, low);
        point.y = float.IsNaN(level) || float.IsInfinity(level) ? point.y : level;

        int index = part.Vertices.Count;
        part.Vertices.Add(point);
        part.Uv.Add(Vector2.Lerp(uvDry, uvWet, low));

        return index;
    }

    private static void Emit(WaterMeshPart part, RibbonCover drawn, int a, int b, int c)
    {
        part.Triangles.Add(a);
        part.Triangles.Add(b);
        part.Triangles.Add(c);
        part.Sources.Add(drawn.Source);
        drawn.Add(part.Vertices[a], part.Vertices[b], part.Vertices[c]);
    }

    private static bool Covered(WaterMap water, RibbonCover drawn, Vector3 vertex, out float surface)
    {
        if (water.StandingAt(vertex.x, vertex.z, out surface) && vertex.y <= surface + MOUTH_BLEND)
            return true;

        return drawn.Inside(vertex.x, vertex.z, out surface);
    }

    private static Vector2 Tangent(List<RiverPoint> points, int i)
    {
        Vector2 before = points[Mathf.Max(0, i - 1)].Position;
        Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)].Position;
        Vector2 tangent = after - before;

        return tangent.sqrMagnitude < 1e-8f ? new Vector2(1f, 0f) : tangent.normalized;
    }
}
