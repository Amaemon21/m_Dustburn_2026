using System.Collections.Generic;
using UnityEngine;

public sealed class WaterMeshPart
{
    public string Name;
    public WaterKind Kind;
    public bool Frozen;
    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector2> Uv = new();
    public readonly List<int> Triangles = new();
    public readonly List<int> Sources = new();
}

public static class WaterMeshes
{
    public const float TILE = 1024f;
    public const float RIBBON_PAD = 0.5f;
    public const float UNDERLAP = WaterMap.RIVER_UNDERLAP;
    public const float MOUTH_BLEND = 0.05f;
    public const int MOUTH_REACH = 4;
    public const float COLUMN = 4f;
    private const int CROSSING_STEPS = 12;
    private const int SPLIT_DEPTH = 3;
    private const int COVER_LATTICE = 4;

    public static List<WaterMeshPart> Build(WaterMap water)
    {
        var parts = new Dictionary<(WaterKind, int, int, bool), WaterMeshPart>();

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

    public static WaterMeshPart Part(Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts, WaterKind kind, Vector2 at, bool frozen = false)
    {
        var key = (kind, Mathf.FloorToInt(at.x / TILE), Mathf.FloorToInt(at.y / TILE), frozen);

        if (!parts.TryGetValue(key, out WaterMeshPart part))
            parts[key] = part = new WaterMeshPart { Kind = kind, Frozen = frozen, Name = frozen ? $"{kind} {key.Item2} {key.Item3} ice" : $"{kind} {key.Item2} {key.Item3}" };

        return part;
    }

    public static void Tessellate(List<Vector3> vertices, List<int> triangles, float maxEdge)
    {
        float limit = maxEdge * maxEdge;
        var pending = new Stack<(int, int, int)>();
        var source = new List<int>(triangles);
        triangles.Clear();

        for (int i = 0; i < source.Count; i += 3)
            pending.Push((source[i], source[i + 1], source[i + 2]));

        while (pending.Count > 0)
        {
            (int a, int b, int c) = pending.Pop();
            float ab = Square(vertices[a], vertices[b]), bc = Square(vertices[b], vertices[c]), ca = Square(vertices[c], vertices[a]);

            if (ab <= limit && bc <= limit && ca <= limit)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                continue;
            }

            if (bc >= ab && bc >= ca)
                (a, b, c) = (b, c, a);
            else if (ca >= ab && ca >= bc)
                (a, b, c) = (c, a, b);

            int middle = vertices.Count;
            vertices.Add(new Vector3(0.5f * (vertices[a].x + vertices[b].x), 0.5f * (vertices[a].y + vertices[b].y), 0.5f * (vertices[a].z + vertices[b].z)));
            pending.Push((a, middle, c));
            pending.Push((middle, b, c));
        }
    }

    private static float Square(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
        return x * x + y * y + z * z;
    }

    private static void Rivers(WaterMap water, Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts)
    {
        for (int source = 0; source < water.Rivers.Count; source++)
        {
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
                        Triangle(part, water, source, previous + k, current + k, previous + k + 1);
                        Triangle(part, water, source, previous + k + 1, current + k, current + k + 1);
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

    private static void Triangle(WaterMeshPart part, WaterMap water, int source, int a, int b, int c, int depth = 0)
    {
        Vector3 first = part.Vertices[a];
        Vector3 second = part.Vertices[b];
        Vector3 third = part.Vertices[c];

        float facing = (second.z - first.z) * (third.x - first.x) - (second.x - first.x) * (third.z - first.z);

        if (facing <= 0f)
            return;

        bool coveredA = Covered(water, source, first, out _);
        bool coveredB = Covered(water, source, second, out _);
        bool coveredC = Covered(water, source, third, out _);

        if (depth < SPLIT_DEPTH && (coveredA != coveredB || coveredB != coveredC || Contradicts(water, source, first, second, third, coveredA)))
        {
            int ab = Midpoint(part, a, b), bc = Midpoint(part, b, c), ca = Midpoint(part, c, a);

            Triangle(part, water, source, a, ab, ca, depth + 1);
            Triangle(part, water, source, ab, b, bc, depth + 1);
            Triangle(part, water, source, ca, bc, c, depth + 1);
            Triangle(part, water, source, ab, bc, ca, depth + 1);
            return;
        }

        if (!coveredA && !coveredB && !coveredC)
        {
            Emit(part, source, a, b, c);
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
                polygon.Add(fromCovered ? Crossing(part, water, source, to, from) : Crossing(part, water, source, from, to));
        }

        for (int k = 1; k + 1 < polygon.Count; k++)
            Emit(part, source, polygon[0], polygon[k], polygon[k + 1]);
    }

    private static bool Contradicts(WaterMap water, int source, Vector3 a, Vector3 b, Vector3 c, bool covered)
    {
        if (!covered)
        {
            return Covered(water, source, 0.5f * (a + b), out _) || Covered(water, source, 0.5f * (b + c), out _)
                || Covered(water, source, 0.5f * (c + a), out _) || Covered(water, source, (a + b + c) / 3f, out _);
        }

        for (int i = 0; i <= COVER_LATTICE; i++)
        {
            for (int j = 0; i + j <= COVER_LATTICE; j++)
            {
                Vector3 probe = (a * i + b * j + c * (COVER_LATTICE - i - j)) / COVER_LATTICE;

                if (!Covered(water, source, probe, out _))
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

    private static int Crossing(WaterMeshPart part, WaterMap water, int source, int open, int covered)
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

            if (Covered(water, source, Vector3.Lerp(dry, wet, middle), out float surface))
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
            Covered(water, source, wet, out level);

        Vector3 point = Vector3.Lerp(dry, wet, low);
        point.y = float.IsNaN(level) || float.IsInfinity(level) ? point.y : level;

        int index = part.Vertices.Count;
        part.Vertices.Add(point);
        part.Uv.Add(Vector2.Lerp(uvDry, uvWet, low));

        return index;
    }

    private static void Emit(WaterMeshPart part, int source, int a, int b, int c)
    {
        part.Triangles.Add(a);
        part.Triangles.Add(b);
        part.Triangles.Add(c);
        part.Sources.Add(source);
    }

    private static bool Covered(WaterMap water, int source, Vector3 vertex, out float surface)
    {
        if (water.StandingAt(vertex.x, vertex.z, out surface) && vertex.y <= surface + MOUTH_BLEND)
            return true;

        return water.InsideDrawnEarlierRiver(source, new Vector2(vertex.x, vertex.z), RIBBON_PAD, out surface);
    }

    private static Vector2 Tangent(List<RiverPoint> points, int i)
    {
        Vector2 before = points[Mathf.Max(0, i - 1)].Position;
        Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)].Position;
        Vector2 tangent = after - before;

        return tangent.sqrMagnitude < 1e-8f ? new Vector2(1f, 0f) : tangent.normalized;
    }

    public const float OFFSHORE_STEP = 512f;
    public const float FLOOR_EDGE_STEP = 32f;
    private const float BORDER_EPSILON = 1e-3f;
    private const float OFFSHORE_UV = 1f / 16f;

    public static WaterMeshPart Offshore(WaterMap water, IReadOnlyList<WaterMeshPart> parts, float reach)
    {
        var inner = new List<Vector2>();

        foreach (WaterMeshPart part in parts)
        {
            if (part.Kind != WaterKind.Sea || part.Frozen)
                continue;

            foreach (Vector3 vertex in part.Vertices)
            {
                if (Mathf.Abs(vertex.y - water.SeaLevel) < BORDER_EPSILON && OnBorder(vertex.x, vertex.z, water.WorldSize))
                    inner.Add(new Vector2(vertex.x, vertex.z));
            }
        }

        var ring = new WaterMeshPart { Name = "Sea offshore", Kind = WaterKind.Sea };
        Ring(ring, water.WorldSize, water.SeaLevel, reach, inner, OFFSHORE_UV, false);
        return ring;
    }

    public static WaterMeshPart OffshoreFloor(float worldSize, float floor, float reach)
    {
        var inner = new List<Vector2>();

        for (float along = 0f; along <= worldSize; along += FLOOR_EDGE_STEP)
        {
            inner.Add(new Vector2(along, 0f));
            inner.Add(new Vector2(along, worldSize));
            inner.Add(new Vector2(0f, along));
            inner.Add(new Vector2(worldSize, along));
        }

        var ring = new WaterMeshPart { Name = "Sea floor offshore", Kind = WaterKind.None };
        Ring(ring, worldSize, floor, reach, inner, 1f / worldSize, true);
        return ring;
    }

    private static bool OnBorder(float x, float z, float world)
    {
        return Mathf.Abs(x) < BORDER_EPSILON || Mathf.Abs(z) < BORDER_EPSILON || Mathf.Abs(x - world) < BORDER_EPSILON || Mathf.Abs(z - world) < BORDER_EPSILON;
    }

    private static void Ring(WaterMeshPart part, float world, float height, float reach, List<Vector2> inner, float uvScale, bool clampUv)
    {
        if (reach <= 0f)
            return;

        int outside = Mathf.Max(1, Mathf.CeilToInt(reach / OFFSHORE_STEP));
        float step = OFFSHORE_STEP;
        var welds = new Dictionary<(long, long), int>();

        int Vertex(Vector2 at)
        {
            var key = ((long)System.Math.Round(at.x * 1000f), (long)System.Math.Round(at.y * 1000f));

            if (welds.TryGetValue(key, out int index))
                return index;

            index = part.Vertices.Count;
            welds[key] = index;
            part.Vertices.Add(new Vector3(at.x, height, at.y));
            part.Uv.Add(clampUv ? new Vector2(Mathf.Clamp01(at.x * uvScale), Mathf.Clamp01(at.y * uvScale)) : at * uvScale);
            return index;
        }

        void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            float facing = (b.y - a.y) * (c.x - a.x) - (b.x - a.x) * (c.y - a.y);

            if (Mathf.Abs(facing) < 1e-6f)
                return;

            if (facing < 0f)
                (b, c) = (c, b);

            part.Triangles.Add(Vertex(a));
            part.Triangles.Add(Vertex(b));
            part.Triangles.Add(Vertex(c));
            part.Sources.Add(-1);
        }

        for (int side = 0; side < 4; side++)
        {
            var edge = new List<float> { 0f, world };

            foreach (Vector2 point in inner)
            {
                if (Side(point, world) == side)
                    edge.Add(Along(point, side));
            }

            edge.Sort();
            var near = new List<Vector2>();

            foreach (float along in edge)
            {
                if (near.Count == 0 || along - Along(near[^1], side) > BORDER_EPSILON)
                    near.Add(Place(side, along, 0f, world));
            }

            var outer = new List<Vector2>();

            for (float along = 0f; along < world + 0.5f * step; along += step)
                outer.Add(Place(side, Mathf.Min(along, world), step, world));

            Zip(near, outer, side, Triangle);

            for (int band = 1; band < outside; band++)
            {
                for (float along = 0f; along < world - 0.5f; along += step)
                {
                    float next = Mathf.Min(world, along + step);
                    Vector2 a = Place(side, along, band * step, world), b = Place(side, next, band * step, world);
                    Vector2 c = Place(side, next, (band + 1) * step, world), d = Place(side, along, (band + 1) * step, world);
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
            }
        }

        foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(world, 0f), new Vector2(world, world), new Vector2(0f, world) })
        {
            float sx = corner.x > 0f ? 1f : -1f, sz = corner.y > 0f ? 1f : -1f;

            for (int u = 0; u < outside; u++)
            {
                for (int v = 0; v < outside; v++)
                {
                    Vector2 a = corner + new Vector2(sx * u * step, sz * v * step);
                    Vector2 b = corner + new Vector2(sx * (u + 1) * step, sz * v * step);
                    Vector2 c = corner + new Vector2(sx * (u + 1) * step, sz * (v + 1) * step);
                    Vector2 d = corner + new Vector2(sx * u * step, sz * (v + 1) * step);
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
            }
        }
    }

    private static int Side(Vector2 point, float world)
    {
        if (Mathf.Abs(point.y) < BORDER_EPSILON)
            return 0;

        if (Mathf.Abs(point.x - world) < BORDER_EPSILON)
            return 1;

        if (Mathf.Abs(point.y - world) < BORDER_EPSILON)
            return 2;

        return Mathf.Abs(point.x) < BORDER_EPSILON ? 3 : -1;
    }

    private static float Along(Vector2 point, int side)
    {
        return side == 0 || side == 2 ? point.x : point.y;
    }

    private static Vector2 Place(int side, float along, float outward, float world)
    {
        return side switch
        {
            0 => new Vector2(along, -outward),
            1 => new Vector2(world + outward, along),
            2 => new Vector2(along, world + outward),
            _ => new Vector2(-outward, along)
        };
    }

    private static void Zip(List<Vector2> near, List<Vector2> outer, int side, System.Action<Vector2, Vector2, Vector2> triangle)
    {
        int i = 0, j = 0;

        while (i + 1 < near.Count || j + 1 < outer.Count)
        {
            bool advanceNear = j + 1 >= outer.Count || i + 1 < near.Count && Along(near[i + 1], side) <= Along(outer[j + 1], side);

            if (advanceNear)
            {
                triangle(near[i], near[i + 1], outer[j]);
                i++;
            }
            else
            {
                triangle(near[i], outer[j + 1], outer[j]);
                j++;
            }
        }
    }
}
