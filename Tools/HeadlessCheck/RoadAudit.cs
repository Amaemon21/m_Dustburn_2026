using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class RoadAudit
{
    private const float WELD = 2f;
    private const float CELL = 32f;
    private const float PARALLEL_STEP = 10f;
    private const float PARALLEL_GAP = 20f;
    private const float PARALLEL_DOT = 0.9f;
    private const int PARALLEL_RUN = 6;
    private const float END_CLEARANCE = 40f;

    private sealed class Grid
    {
        private readonly Dictionary<long, List<int>> _cells = new();
        private readonly List<int> _stamps = new();
        private int _stamp;

        public readonly List<(int Road, Vector2 A, Vector2 B)> Segments = new();

        public Grid(List<Road> roads)
        {
            for (int road = 0; road < roads.Count; road++)
            {
                Vector2[] points = roads[road].Points;

                for (int i = 0; i < points.Length - 1; i++)
                {
                    int id = Segments.Count;

                    Segments.Add((road, points[i], points[i + 1]));
                    _stamps.Add(0);

                    Visit(Vector2.Min(points[i], points[i + 1]), Vector2.Max(points[i], points[i + 1]), key =>
                    {
                        if (!_cells.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            _cells[key] = list;
                        }

                        list.Add(id);
                    });
                }
            }
        }

        public List<int> Query(Vector2 min, Vector2 max)
        {
            var result = new List<int>();
            _stamp++;

            Visit(min, max, key =>
            {
                if (!_cells.TryGetValue(key, out List<int> list))
                    return;

                foreach (int id in list)
                {
                    if (_stamps[id] == _stamp)
                        continue;

                    _stamps[id] = _stamp;
                    result.Add(id);
                }
            });

            return result;
        }

        private static void Visit(Vector2 min, Vector2 max, Action<long> visit)
        {
            for (int y = Mathf.FloorToInt(min.y / CELL); y <= Mathf.FloorToInt(max.y / CELL); y++)
            {
                for (int x = Mathf.FloorToInt(min.x / CELL); x <= Mathf.FloorToInt(max.x / CELL); x++)
                    visit(((long)y << 32) ^ (uint)x);
            }
        }
    }

    public static void Run(string path)
    {
        (List<Road> regional, List<Road> streets, int hubs) = Read(path);

        Console.WriteLine($"audit of {path}: {hubs} settlements");
        Print(Measure(regional, streets, hubs));
    }

    public static List<(string Name, double Value)> Measure(List<Road> regional, List<Road> streets, int settlements)
    {
        var metrics = new List<(string, double)>
        {
            ("settlements", settlements),
            ("regional.roads", regional.Count),
            ("regional.km", Length(regional) / 1000.0),
            ("streets.roads", streets.Count),
            ("streets.km", Length(streets) / 1000.0)
        };

        var all = new List<Road>(regional);
        all.AddRange(streets);

        var regionalGrid = new Grid(regional);
        var allGrid = new Grid(all);

        (int nodes, int junctions, double meanDegree, int maxDegree, int components, int loops) = Topology(regional, regionalGrid);

        metrics.Add(("regional.nodes", nodes));
        metrics.Add(("regional.junctionsDegree3plus", junctions));
        metrics.Add(("regional.meanDegree", meanDegree));
        metrics.Add(("regional.maxDegree", maxDegree));
        metrics.Add(("regional.components", components));
        metrics.Add(("regional.cyclomaticLoops", loops));
        metrics.Add(("regional.roadsPerSettlement", settlements == 0 ? 0.0 : regional.Count / (double)settlements));

        (int interior, int contacts) = Crossings(regional, regionalGrid, regional.Count);
        (int allInterior, int _) = Crossings(all, allGrid, all.Count);

        metrics.Add(("regional.crossingsAwayFromEnds", interior));
        metrics.Add(("regional.tContactsOnInterior", contacts));
        metrics.Add(("all.crossingsAwayFromEnds", allInterior));
        metrics.Add(("regional.parallelRuns", Parallel(regional, regionalGrid)));
        metrics.Add(("all.parallelRuns", Parallel(all, allGrid)));

        List<float> turns = Turns(regional);
        turns.Sort();

        if (turns.Count > 0)
        {
            metrics.Add(("regional.turnPer30m.p50", turns[(turns.Count - 1) / 2]));
            metrics.Add(("regional.turnPer30m.p90", turns[(int)((turns.Count - 1) * 0.9f)]));
            metrics.Add(("regional.turnPer30m.max", turns[^1]));
        }

        return metrics;
    }

    public static void Print(List<(string Name, double Value)> metrics)
    {
        foreach ((string name, double value) in metrics)
            Console.WriteLine($"  {name,-44} {value.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    private static (List<Road>, List<Road>, int) Read(string path)
    {
        var regional = new List<Road>();
        var streets = new List<Road>();
        var points = new List<Vector2>();
        List<Road> target = null;
        int hubs = 0;
        bool inHubs = false;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimEnd();

            if (line.StartsWith("  _", StringComparison.Ordinal))
            {
                inHubs = line == "  _hubs:";
                target = line == "  _roads:" ? regional : line == "  _streets:" ? streets : null;
                continue;
            }

            if (inHubs && line.StartsWith("  - <Position>", StringComparison.Ordinal))
                hubs++;

            if (target == null)
                continue;

            string body = line.TrimStart(' ', '-').Trim();

            if (body.StartsWith("<Points>k__BackingField", StringComparison.Ordinal))
            {
                points.Clear();
                continue;
            }

            if (body.StartsWith("{x:", StringComparison.Ordinal))
            {
                string[] parts = body.Trim('{', '}').Split(',');
                points.Add(new Vector2(
                    float.Parse(parts[0].Substring(parts[0].IndexOf(':') + 1), CultureInfo.InvariantCulture),
                    float.Parse(parts[1].Substring(parts[1].IndexOf(':') + 1), CultureInfo.InvariantCulture)));
                continue;
            }

            if (!body.StartsWith("<Width>k__BackingField:", StringComparison.Ordinal) || points.Count < 2)
                continue;

            float width = float.Parse(body.Substring(body.IndexOf(':') + 1), CultureInfo.InvariantCulture);
            target.Add(new Road(points.ToArray(), width, target == regional ? RoadKind.Highway : RoadKind.LocalStreet));
            points.Clear();
        }

        return (regional, streets, hubs);
    }

    private static (int, int, double, int, int, int) Topology(List<Road> roads, Grid grid)
    {
        var nodes = new List<Vector2>();
        var nodeGrid = new Dictionary<long, List<int>>();
        var parent = new List<int>();
        var degree = new List<int>();
        int edges = 0;

        for (int road = 0; road < roads.Count; road++)
        {
            Vector2[] points = roads[road].Points;
            var stops = new List<(float Along, Vector2 Point)> { (0f, points[0]), (float.MaxValue, points[^1]) };
            var distance = Cumulative(points);

            for (int i = 0; i < points.Length - 1; i++)
            {
                foreach (int id in grid.Query(Vector2.Min(points[i], points[i + 1]) - Vector2.one * WELD, Vector2.Max(points[i], points[i + 1]) + Vector2.one * WELD))
                {
                    (int other, Vector2 a, Vector2 b) = grid.Segments[id];

                    if (other == road)
                        continue;

                    if (Intersect(points[i], points[i + 1], a, b, out Vector2 point, out float t))
                        stops.Add((distance[i] + Vector2.Distance(points[i], points[i + 1]) * t, point));

                    foreach (Vector2 end in new[] { roads[other].Points[0], roads[other].Points[^1] })
                    {
                        if ((end - a).sqrMagnitude > 1e-6f && (end - b).sqrMagnitude > 1e-6f)
                            continue;

                        float along = ProjectSegment(points[i], points[i + 1], end, out float gap);

                        if (gap <= WELD)
                            stops.Add((distance[i] + along, end));
                    }
                }
            }

            stops.Sort((left, right) => left.Along.CompareTo(right.Along));
            int previous = -1;

            foreach ((float _, Vector2 point) in stops)
            {
                int node = NodeFor(nodes, nodeGrid, parent, degree, point);

                if (previous >= 0 && previous != node)
                {
                    edges++;
                    degree[previous]++;
                    degree[node]++;
                    Union(parent, previous, node);
                }

                previous = node;
            }
        }

        var roots = new HashSet<int>();
        int junctions = 0;
        int maxDegree = 0;
        long degreeSum = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            roots.Add(Find(parent, i));
            degreeSum += degree[i];
            maxDegree = Math.Max(maxDegree, degree[i]);

            if (degree[i] >= 3)
                junctions++;
        }

        return (nodes.Count, junctions, nodes.Count == 0 ? 0.0 : degreeSum / (double)nodes.Count, maxDegree, roots.Count, edges - nodes.Count + roots.Count);
    }

    private static int NodeFor(List<Vector2> nodes, Dictionary<long, List<int>> nodeGrid, List<int> parent, List<int> degree, Vector2 point)
    {
        int cellX = Mathf.FloorToInt(point.x / CELL);
        int cellY = Mathf.FloorToInt(point.y / CELL);

        for (int y = cellY - 1; y <= cellY + 1; y++)
        {
            for (int x = cellX - 1; x <= cellX + 1; x++)
            {
                if (!nodeGrid.TryGetValue(((long)y << 32) ^ (uint)x, out List<int> list))
                    continue;

                foreach (int node in list)
                {
                    if ((nodes[node] - point).sqrMagnitude <= WELD * WELD)
                        return node;
                }
            }
        }

        long key = ((long)cellY << 32) ^ (uint)cellX;

        if (!nodeGrid.TryGetValue(key, out List<int> cell))
        {
            cell = new List<int>();
            nodeGrid[key] = cell;
        }

        cell.Add(nodes.Count);
        nodes.Add(point);
        parent.Add(parent.Count);
        degree.Add(0);

        return nodes.Count - 1;
    }

    private static (int, int) Crossings(List<Road> roads, Grid grid, int count)
    {
        int interior = 0;
        int contacts = 0;

        for (int id = 0; id < grid.Segments.Count; id++)
        {
            (int road, Vector2 a, Vector2 b) = grid.Segments[id];

            foreach (int other in grid.Query(Vector2.Min(a, b), Vector2.Max(a, b)))
            {
                (int otherRoad, Vector2 c, Vector2 d) = grid.Segments[other];

                if (other <= id || otherRoad == road)
                    continue;

                if (!Intersect(a, b, c, d, out Vector2 point, out float _))
                    continue;

                bool atEndA = Near(point, roads[road].Points[0]) || Near(point, roads[road].Points[^1]);
                bool atEndB = Near(point, roads[otherRoad].Points[0]) || Near(point, roads[otherRoad].Points[^1]);

                if (!atEndA && !atEndB)
                    interior++;
                else if (atEndA != atEndB)
                    contacts++;
            }
        }

        return (interior, contacts);
    }

    private static int Parallel(List<Road> roads, Grid grid)
    {
        int runs = 0;

        for (int road = 0; road < roads.Count; road++)
        {
            Vector2[] points = RoadSmoother.Resample(roads[road].Points, PARALLEL_STEP);
            float total = Length(points);
            float travelled = 0f;
            int run = 0;

            for (int i = 1; i < points.Length - 1; i++)
            {
                travelled += Vector2.Distance(points[i - 1], points[i]);

                if (travelled < END_CLEARANCE || total - travelled < END_CLEARANCE)
                {
                    run = 0;
                    continue;
                }

                Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
                float reach = roads[road].HalfWidth + PARALLEL_GAP + 8f;
                bool flagged = false;

                foreach (int id in grid.Query(points[i] - Vector2.one * reach, points[i] + Vector2.one * reach))
                {
                    (int other, Vector2 a, Vector2 b) = grid.Segments[id];

                    if (other == road || Near(points[i], roads[other].Points[0], END_CLEARANCE) || Near(points[i], roads[other].Points[^1], END_CLEARANCE))
                        continue;

                    float half = roads[road].HalfWidth + roads[other].HalfWidth;
                    float distanceSqr = DistanceSqr(points[i], a, b);
                    float low = half + 1.5f;
                    float high = half + PARALLEL_GAP;

                    if (distanceSqr < low * low || distanceSqr > high * high || (b - a).sqrMagnitude < 1e-4f)
                        continue;

                    if (Mathf.Abs(Vector2.Dot((b - a).normalized, tangent)) < PARALLEL_DOT)
                        continue;

                    flagged = true;
                    break;
                }

                run = flagged ? run + 1 : 0;

                if (run == PARALLEL_RUN)
                    runs++;
            }
        }

        return runs;
    }

    private static List<float> Turns(List<Road> roads)
    {
        var turns = new List<float>();

        foreach (Road road in roads)
        {
            Vector2[] points = RoadSmoother.Resample(road.Points, 30f);

            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 back = points[i] - points[i - 1];
                Vector2 forward = points[i + 1] - points[i];

                if (back.sqrMagnitude < 1f || forward.sqrMagnitude < 1f)
                    continue;

                turns.Add(Mathf.Acos(Mathf.Clamp(Vector2.Dot(back.normalized, forward.normalized), -1f, 1f)) * Mathf.Rad2Deg);
            }
        }

        return turns;
    }

    private static float ProjectSegment(Vector2 from, Vector2 to, Vector2 point, out float gap)
    {
        Vector2 line = to - from;
        float length = line.magnitude;
        float t = length > 1e-4f ? Mathf.Clamp01(Vector2.Dot(point - from, line) / (length * length)) : 0f;

        gap = Vector2.Distance(point, from + line * t);

        return length * t;
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
    }

    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point, out float t)
    {
        point = a;
        t = 0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-6f)
            return false;

        Vector2 delta = c - a;
        t = (delta.x * s.y - delta.y * s.x) / denominator;
        float u = (delta.x * r.y - delta.y * r.x) / denominator;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        point = a + r * t;
        return true;
    }

    private static bool Near(Vector2 a, Vector2 b, float distance = WELD)
    {
        return (a - b).sqrMagnitude <= distance * distance;
    }

    private static float DistanceSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;
        float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr) : 0f;

        return (point - (from + line * t)).sqrMagnitude;
    }

    private static double Length(List<Road> roads)
    {
        double total = 0.0;

        foreach (Road road in roads)
            total += Length(road.Points);

        return total;
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 1; i < points.Length; i++)
            total += Vector2.Distance(points[i - 1], points[i]);

        return total;
    }

    private static int Find(List<int> parent, int node)
    {
        while (parent[node] != node)
        {
            parent[node] = parent[parent[node]];
            node = parent[node];
        }

        return node;
    }

    private static void Union(List<int> parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
            parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
    }
}
