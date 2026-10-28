using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

static class RoadView
{
    private const string GENERATED = "../../Assets/_Dustborn/Generated";
    private const float WINDOW = 48f;
    private const int PIXELS = 960;
    private const float JOIN_DISTANCE = 0.5f;
    private const float INTERIOR_MARGIN = 12f;

    public static void Run(string[] args)
    {
        int at = Array.IndexOf(args, "--road-view");
        string directory = at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "road_view";
        Directory.CreateDirectory(directory);
        (List<Road> regional, List<Road> streets, _) = RoadAudit.Read($"{GENERATED}/RoadNetwork.asset");
        var roads = new List<Road>(regional);
        roads.AddRange(streets);
        roads.RemoveAll(road => road.Surface != RoadSurface.Asphalt || road.Points.Length < 2);
        WaterMap water = WaterMapFormat.Read(File.ReadAllBytes($"{GENERATED}/WaterMap.bytes"));
        var surface = new RoadSurfaceMesh(roads, water);
        Joints(roads);
        Console.WriteLine($"junction fillets {surface.FilletCount}");

        foreach ((string name, Vector2 centre) in Scenes(roads))
        {
            Vector2 origin = centre - new Vector2(WINDOW, WINDOW) * 0.5f;

            foreach (float cell in new[] { 1f, 8f })
            {
                using VoxelMesh plane = Plane(origin, WINDOW, cell);
                string path = Path.Combine(directory, $"{name}_{cell:0}m.png");
                RoadSurfacePreview.Write(surface.Build(plane), path, origin, WINDOW, PIXELS);
            }

            Console.WriteLine($"  {name}: ({centre.x:0.0}, {centre.y:0.0})");
        }
    }

    private static void Joints(List<Road> roads)
    {
        var ends = Ends(roads);
        int continuations = 0, kindChanges = 0, widthChanges = 0;

        for (int a = 0; a < ends.Count; a++)
        {
            int partner = Partner(ends, a);

            if (partner <= a)
                continue;

            continuations++;
            Road first = roads[ends[a].Road], second = roads[ends[partner].Road];

            if (first.Kind != second.Kind)
                kindChanges++;

            if (Math.Abs(first.HalfWidth - second.HalfWidth) > 0.01f)
                widthChanges++;
        }

        Console.WriteLine($"asphalt roads {roads.Count}: {continuations} end-to-end continuations, {kindChanges} change kind, {widthChanges} change width");
    }

    private static IEnumerable<(string, Vector2)> Scenes(List<Road> roads)
    {
        var ends = Ends(roads);

        if (Curve(roads, out Vector2 curve))
            yield return ("curve", curve);

        for (int a = 0; a < ends.Count; a++)
        {
            int partner = Partner(ends, a);

            if (partner <= a)
                continue;

            Road first = roads[ends[a].Road], second = roads[ends[partner].Road];

            if (Math.Abs(first.HalfWidth - second.HalfWidth) > 0.01f)
            {
                yield return ("taper", ends[a].Point);
                break;
            }
        }

        for (int a = 0; a < ends.Count; a++)
        {
            int partner = Partner(ends, a);

            if (partner > a && (roads[ends[a].Road].Kind == RoadKind.Highway) != (roads[ends[partner].Road].Kind == RoadKind.Highway))
            {
                yield return ("gateway", ends[a].Point);
                break;
            }
        }

        foreach (RoadKind kind in new[] { RoadKind.Highway, RoadKind.LocalStreet })
        {
            if (Tee(roads, ends, kind, out Vector2 tee))
                yield return ($"tee_{kind}", tee);
        }

        if (Crossing(roads, out Vector2 crossing))
            yield return ("cross", crossing);
    }

    private static bool Curve(List<Road> roads, out Vector2 point)
    {
        point = default;
        float sharpest = 0f;

        foreach (Road road in roads)
        {
            if (road.Kind != RoadKind.Highway)
                continue;

            Vector2[] p = road.Points;

            for (int i = 2; i < p.Length - 2; i++)
            {
                float turn = Turn(p[i] - p[i - 1], p[i + 1] - p[i]);

                if (turn > sharpest && Isolated(roads, road, p[i], 40f))
                {
                    sharpest = turn;
                    point = p[i];
                }
            }
        }

        return sharpest > 0f;
    }

    private static bool Tee(List<Road> roads, List<(int Road, Vector2 Point)> ends, RoadKind kind, out Vector2 point)
    {
        foreach ((int index, Vector2 end) in ends)
        {
            if (roads[index].Kind != kind)
                continue;

            for (int other = 0; other < roads.Count; other++)
            {
                if (other != index && Interior(roads[other], end, 0.5f))
                {
                    point = end;
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    private static bool Crossing(List<Road> roads, out Vector2 point)
    {
        for (int a = 0; a < roads.Count; a++)
        {
            for (int b = a + 1; b < roads.Count; b++)
            {
                Vector2[] p = roads[a].Points, q = roads[b].Points;

                for (int i = 1; i < p.Length; i++)
                {
                    for (int j = 1; j < q.Length; j++)
                    {
                        if (Intersect(p[i - 1], p[i], q[j - 1], q[j], out point) && Interior(roads[a], point, 0.5f) && Interior(roads[b], point, 0.5f))
                            return true;
                    }
                }
            }
        }

        point = default;
        return false;
    }

    private static bool Interior(Road road, Vector2 point, float tolerance)
    {
        Vector2[] p = road.Points;

        if ((point - p[0]).magnitude < INTERIOR_MARGIN || (point - p[^1]).magnitude < INTERIOR_MARGIN)
            return false;

        for (int i = 1; i < p.Length; i++)
        {
            if (Distance(point, p[i - 1], p[i]) < tolerance)
                return true;
        }

        return false;
    }

    private static bool Isolated(List<Road> roads, Road road, Vector2 point, float radius)
    {
        foreach (Road other in roads)
        {
            if (other == road)
                continue;

            for (int i = 1; i < other.Points.Length; i++)
            {
                if (Distance(point, other.Points[i - 1], other.Points[i]) < radius)
                    return false;
            }
        }

        return true;
    }

    private static List<(int Road, Vector2 Point)> Ends(List<Road> roads)
    {
        var ends = new List<(int, Vector2)>();

        for (int i = 0; i < roads.Count; i++)
        {
            ends.Add((i, roads[i].Points[0]));
            ends.Add((i, roads[i].Points[^1]));
        }

        return ends;
    }

    private static int Partner(List<(int Road, Vector2 Point)> ends, int end)
    {
        int partner = -1;

        for (int other = 0; other < ends.Count; other++)
        {
            if (other == end || (ends[other].Point - ends[end].Point).magnitude > JOIN_DISTANCE)
                continue;

            if (partner >= 0)
                return -1;

            partner = other;
        }

        return partner;
    }

    private static VoxelMesh Plane(Vector2 origin, float size, float cell)
    {
        var mesh = new VoxelMesh();
        int cells = Mathf.RoundToInt(size / cell);

        for (int z = 0; z <= cells; z++)
        {
            for (int x = 0; x <= cells; x++)
            {
                mesh.Vertices.Add(new Vector3(origin.x + x * cell, 0f, origin.y + z * cell));
                mesh.Normals.Add(Vector3.up);
                mesh.Uv.Add(Vector2.zero);
            }
        }

        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int a = z * (cells + 1) + x, b = a + 1, c = a + cells + 1, d = c + 1;
                mesh.Triangles.Add(a); mesh.Triangles.Add(c); mesh.Triangles.Add(b);
                mesh.Triangles.Add(b); mesh.Triangles.Add(c); mesh.Triangles.Add(d);
            }
        }

        return mesh;
    }

    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
    {
        point = default;
        Vector2 r = b - a, s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Math.Abs(denominator) < 1e-6f)
            return false;

        float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denominator;
        float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denominator;

        if (t <= 0f || t >= 1f || u <= 0f || u >= 1f)
            return false;

        point = a + r * t;
        return true;
    }

    private static float Distance(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, d) / Mathf.Max(d.sqrMagnitude, 1e-6f));
        return (point - (a + d * t)).magnitude;
    }

    private static float Turn(Vector2 a, Vector2 b)
    {
        float cos = Vector2.Dot(a.normalized, b.normalized);
        return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
