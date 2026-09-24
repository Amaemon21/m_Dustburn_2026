using System.Collections.Generic;
using UnityEngine;

public sealed class WaterMeshPart
{
    public string Name;
    public WaterKind Kind;
    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector2> Uv = new();
    public readonly List<int> Triangles = new();
    public readonly List<int> Sources = new();
}

public static class WaterMeshes
{
    public const float TILE = 1024f;
    public const float RIBBON_PAD = 0.3f;
    public const float UNDERLAP = WaterMap.RIVER_UNDERLAP;
    public const float MOUTH_BLEND = 0.5f;
    public const float TUCK = 0.12f;

    public static List<WaterMeshPart> Build(WaterMap water)
    {
        var parts = new Dictionary<(WaterKind, int, int), WaterMeshPart>();

        Rivers(water, parts);
        StandingWaterMesh.Build(water, parts);

        var list = new List<WaterMeshPart>();

        foreach (WaterMeshPart part in parts.Values)
        {
            if (part.Triangles.Count > 0)
                list.Add(part);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public static WaterMeshPart Part(Dictionary<(WaterKind, int, int), WaterMeshPart> parts, WaterKind kind, Vector2 at)
    {
        var key = (kind, Mathf.FloorToInt(at.x / TILE), Mathf.FloorToInt(at.y / TILE));

        if (!parts.TryGetValue(key, out WaterMeshPart part))
            parts[key] = part = new WaterMeshPart { Kind = kind, Name = $"{kind} {key.Item2} {key.Item3}" };

        return part;
    }

    private static void Rivers(WaterMap water, Dictionary<(WaterKind, int, int), WaterMeshPart> parts)
    {
        for (int source = 0; source < water.Rivers.Count; source++)
        {
            List<RiverPoint> points = water.Rivers[source].Points;
            float travelled = 0f;
            int previous = -1;
            bool previousTucked = false;
            WaterMeshPart previousPart = null;

            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    travelled += Vector2.Distance(points[i - 1].Position, points[i].Position);

                bool drawn = Open(water, points[i]) || i > 0 && Open(water, points[i - 1]) || i + 1 < points.Count && Open(water, points[i + 1]);

                if (!drawn)
                {
                    previous = -1;
                    continue;
                }

                WaterMeshPart part = Part(parts, WaterKind.River, points[i].Position);

                if (part != previousPart && previous >= 0)
                    previous = Pair(part, water, points, i - 1, travelled - Vector2.Distance(points[i - 1].Position, points[i].Position), out previousTucked);

                int current = Pair(part, water, points, i, travelled, out bool tucked);

                if (previous >= 0 && !(tucked && previousTucked && Submerged(water, points[i - 1].Position, points[i].Position)))
                {
                    Triangle(part, source, previous, current, previous + 1);
                    Triangle(part, source, previous + 1, current, current + 1);
                }

                previous = current;
                previousTucked = tucked;
                previousPart = part;
            }
        }
    }

    private static bool Submerged(WaterMap water, Vector2 a, Vector2 b)
    {
        Vector2 middle = 0.5f * (a + b);

        return water.StandingAt(middle.x, middle.y, out _);
    }

    private static bool Open(WaterMap water, RiverPoint point)
    {
        return water.RiverOpen(point);
    }

    private static int Pair(WaterMeshPart part, WaterMap water, List<RiverPoint> points, int i, float travelled, out bool tucked)
    {
        RiverPoint point = points[i];
        Vector2 tangent = Tangent(points, i);
        var normal = new Vector2(-tangent.y, tangent.x);

        float half = point.Width * 0.5f + RIBBON_PAD;
        float surface = Open(water, point) ? point.Surface : point.Surface - UNDERLAP;
        float v = travelled / Mathf.Max(1f, point.Width);

        Vector2 left = point.Position + normal * half;
        Vector2 right = point.Position - normal * half;

        int index = part.Vertices.Count;
        float height = Tuck(water, surface, left, point.Position, right);
        tucked = height < surface && water.StandingAt(point.Position.x, point.Position.y, out _);

        part.Vertices.Add(new Vector3(left.x, height, left.y));
        part.Vertices.Add(new Vector3(right.x, height, right.y));
        part.Uv.Add(new Vector2(0f, v));
        part.Uv.Add(new Vector2(1f, v));

        return index;
    }

    private static float Tuck(WaterMap water, float surface, Vector2 left, Vector2 center, Vector2 right)
    {
        float tucked = Mathf.Min(Tucked(water, surface, left), Mathf.Min(Tucked(water, surface, center), Tucked(water, surface, right)));

        return Mathf.Min(surface, tucked);
    }

    private static float Tucked(WaterMap water, float surface, Vector2 at)
    {
        if (!water.StandingAt(at.x, at.y, out float standing))
            return float.PositiveInfinity;

        return surface >= standing - UNDERLAP && surface - standing < MOUTH_BLEND ? standing - TUCK : float.PositiveInfinity;
    }

    private static void Triangle(WaterMeshPart part, int source, int a, int b, int c)
    {
        Vector3 first = part.Vertices[a];
        Vector3 second = part.Vertices[b];
        Vector3 third = part.Vertices[c];

        float facing = (second.z - first.z) * (third.x - first.x) - (second.x - first.x) * (third.z - first.z);

        if (facing <= 0f)
            return;

        part.Triangles.Add(a);
        part.Triangles.Add(b);
        part.Triangles.Add(c);
        part.Sources.Add(source);
    }

    private static Vector2 Tangent(List<RiverPoint> points, int i)
    {
        Vector2 before = points[Mathf.Max(0, i - 1)].Position;
        Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)].Position;
        Vector2 tangent = after - before;

        return tangent.sqrMagnitude < 1e-8f ? new Vector2(1f, 0f) : tangent.normalized;
    }
}
