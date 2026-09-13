using System;
using System.Collections.Generic;
using UnityEngine;

public enum RoadNodeKind
{
    Gateway,
    Junction,
    Terminal
}

public sealed class RoadNode
{
    public int Id { get; }
    public Vector2 Position { get; }
    public RoadNodeKind Kind { get; }
    public int Settlement { get; }
    public int Gateway { get; }
    public List<int> Edges { get; } = new();

    public RoadNode(int id, Vector2 position, RoadNodeKind kind, int settlement, int gateway)
    {
        Id = id;
        Position = position;
        Kind = kind;
        Settlement = settlement;
        Gateway = gateway;
    }
}

public sealed class RoadEdge
{
    public int Id { get; }
    public int From { get; }
    public int To { get; }
    public Vector2[] Points { get; }
    public float[] Distance { get; }
    public RoadKind Kind { get; }
    public int Route { get; }
    public bool Alive { get; private set; } = true;

    public RoadEdge(int id, int from, int to, Vector2[] points, RoadKind kind, int route)
    {
        Id = id;
        From = from;
        To = to;
        Points = points;
        Kind = kind;
        Route = route;
        Distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            Distance[i] = Distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);
    }

    public float Length => Distance[^1];

    public void Retire()
    {
        Alive = false;
    }

    public int Other(int node)
    {
        return node == From ? To : From;
    }

    public int SegmentAt(float along)
    {
        int low = 0;
        int high = Points.Length - 2;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;

            if (Distance[middle] <= along)
                low = middle;
            else
                high = middle - 1;
        }

        return Mathf.Clamp(low, 0, Points.Length - 2);
    }

    public Vector2 PointAt(float along)
    {
        along = Mathf.Clamp(along, 0f, Length);

        int segment = SegmentAt(along);
        float span = Distance[segment + 1] - Distance[segment];

        if (span <= Mathf.Epsilon)
            return Points[segment];

        return Vector2.Lerp(Points[segment], Points[segment + 1], (along - Distance[segment]) / span);
    }

    public Vector2 TangentAt(float along)
    {
        int segment = SegmentAt(Mathf.Clamp(along, 0f, Length));

        for (int offset = 0; offset < Points.Length - 1; offset++)
        {
            int forward = Mathf.Min(Points.Length - 2, segment + offset);
            Vector2 delta = Points[forward + 1] - Points[forward];

            if (delta.sqrMagnitude > 1e-6f)
                return delta.normalized;

            int backward = Mathf.Max(0, segment - offset);
            delta = Points[backward + 1] - Points[backward];

            if (delta.sqrMagnitude > 1e-6f)
                return delta.normalized;
        }

        return new Vector2(1f, 0f);
    }

    public Vector2 DirectionFrom(int node, float reach)
    {
        float along = Mathf.Min(reach, Length);

        return node == From
            ? (PointAt(along) - Points[0]).normalized
            : (PointAt(Length - along) - Points[^1]).normalized;
    }

    public float Project(Vector2 point, out Vector2 nearest)
    {
        float bestSqr = float.MaxValue;
        float bestAlong = 0f;

        nearest = Points[0];

        for (int i = 0; i < Points.Length - 1; i++)
        {
            Vector2 line = Points[i + 1] - Points[i];
            float lengthSqr = line.sqrMagnitude;
            float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(point - Points[i], line) / lengthSqr) : 0f;
            Vector2 candidate = Points[i] + line * t;
            float distanceSqr = (point - candidate).sqrMagnitude;

            if (distanceSqr >= bestSqr)
                continue;

            bestSqr = distanceSqr;
            nearest = candidate;
            bestAlong = Distance[i] + (Distance[i + 1] - Distance[i]) * t;
        }

        return bestAlong;
    }
}

public sealed class RoadGraph
{
    private const float MIN_SPLIT = 1f;
    private const float WELD = 0.05f;

    private readonly List<RoadNode> _nodes = new();
    private readonly List<RoadEdge> _edges = new();
    private readonly Dictionary<int, (int Left, int Right, float Along)> _splits = new();

    private int _routes;

    public IReadOnlyList<RoadNode> Nodes => _nodes;
    public IReadOnlyList<RoadEdge> Edges => _edges;

    public int AddNode(Vector2 position, RoadNodeKind kind, int settlement = -1, int gateway = -1)
    {
        _nodes.Add(new RoadNode(_nodes.Count, position, kind, settlement, gateway));

        return _nodes.Count - 1;
    }

    public int NewRoute()
    {
        return _routes++;
    }

    public int AddEdge(int from, int to, IReadOnlyList<Vector2> points, RoadKind kind, int route)
    {
        var clean = new List<Vector2>(points.Count) { _nodes[from].Position };

        for (int i = 1; i < points.Count - 1; i++)
        {
            if ((points[i] - clean[^1]).sqrMagnitude > WELD * WELD)
                clean.Add(points[i]);
        }

        Vector2 end = _nodes[to].Position;

        if (clean.Count > 1 && (clean[^1] - end).sqrMagnitude <= WELD * WELD)
            clean.RemoveAt(clean.Count - 1);

        clean.Add(end);

        var edge = new RoadEdge(_edges.Count, from, to, clean.ToArray(), kind, route);

        _edges.Add(edge);
        _nodes[from].Edges.Add(edge.Id);

        if (to != from)
            _nodes[to].Edges.Add(edge.Id);

        return edge.Id;
    }

    public int Degree(int node)
    {
        return _nodes[node].Edges.Count;
    }

    public int Degree(int node, RoadKind kind)
    {
        int degree = 0;

        foreach (int edge in _nodes[node].Edges)
        {
            if (_edges[edge].Kind == kind)
                degree++;
        }

        return degree;
    }

    public int Resolve(int edge, ref float along)
    {
        while (_splits.TryGetValue(edge, out (int Left, int Right, float Along) split))
        {
            if (along <= split.Along)
            {
                edge = split.Left;
                continue;
            }

            along -= split.Along;
            edge = split.Right;
        }

        return edge;
    }

    public int Split(int edge, float along)
    {
        edge = Resolve(edge, ref along);

        RoadEdge source = _edges[edge];

        if (along <= MIN_SPLIT)
            return source.From;

        if (along >= source.Length - MIN_SPLIT)
            return source.To;

        Vector2 point = source.PointAt(along);
        int segment = source.SegmentAt(along);

        var left = new List<Vector2>(segment + 2);
        var right = new List<Vector2>(source.Points.Length - segment + 1);

        for (int i = 0; i <= segment; i++)
            left.Add(source.Points[i]);

        left.Add(point);
        right.Add(point);

        for (int i = segment + 1; i < source.Points.Length; i++)
            right.Add(source.Points[i]);

        int node = AddNode(point, RoadNodeKind.Junction);

        source.Retire();
        _nodes[source.From].Edges.Remove(source.Id);
        _nodes[source.To].Edges.Remove(source.Id);

        int leftEdge = AddEdge(source.From, node, left, source.Kind, source.Route);
        int rightEdge = AddEdge(node, source.To, right, source.Kind, source.Route);

        _splits[edge] = (leftEdge, rightEdge, along);

        return node;
    }

    public float[] Distances(IReadOnlyList<(int Node, float Cost)> sources, Func<RoadEdge, bool> filter)
    {
        var distances = new float[_nodes.Count];
        Array.Fill(distances, float.MaxValue);

        var heap = new MinHeap(Mathf.Max(16, _nodes.Count));

        foreach ((int node, float cost) in sources)
        {
            if (cost >= distances[node])
                continue;

            distances[node] = cost;
            heap.Push(node, cost);
        }

        var closed = new bool[_nodes.Count];

        while (heap.TryPop(out int current))
        {
            if (closed[current])
                continue;

            closed[current] = true;

            foreach (int id in _nodes[current].Edges)
            {
                RoadEdge edge = _edges[id];

                if (filter != null && !filter(edge))
                    continue;

                int next = edge.Other(current);
                float candidate = distances[current] + edge.Length;

                if (candidate >= distances[next])
                    continue;

                distances[next] = candidate;
                heap.Push(next, candidate);
            }
        }

        return distances;
    }

    public int[] Components(Func<RoadEdge, bool> filter, out int count)
    {
        var parent = new int[_nodes.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        var touched = new bool[_nodes.Count];

        foreach (RoadEdge edge in _edges)
        {
            if (!edge.Alive || (filter != null && !filter(edge)))
                continue;

            touched[edge.From] = true;
            touched[edge.To] = true;
            Union(parent, edge.From, edge.To);
        }

        var labels = new int[_nodes.Count];
        var roots = new Dictionary<int, int>();

        for (int i = 0; i < labels.Length; i++)
        {
            if (!touched[i])
            {
                labels[i] = -1;
                continue;
            }

            int root = Find(parent, i);

            if (!roots.TryGetValue(root, out int label))
            {
                label = roots.Count;
                roots[root] = label;
            }

            labels[i] = label;
        }

        count = roots.Count;

        return labels;
    }

    public List<Road> BuildRoads(WorldGenerationConfig config)
    {
        var byRoute = new SortedDictionary<int, List<RoadEdge>>();

        foreach (RoadEdge edge in _edges)
        {
            if (!edge.Alive)
                continue;

            if (!byRoute.TryGetValue(edge.Route, out List<RoadEdge> list))
            {
                list = new List<RoadEdge>();
                byRoute[edge.Route] = list;
            }

            list.Add(edge);
        }

        var roads = new List<Road>();

        foreach (List<RoadEdge> route in byRoute.Values)
            roads.AddRange(Chain(config, route));

        return roads;
    }

    private IEnumerable<Road> Chain(WorldGenerationConfig config, List<RoadEdge> route)
    {
        var incident = new Dictionary<int, List<RoadEdge>>();

        foreach (RoadEdge edge in route)
        {
            Add(incident, edge.From, edge);
            Add(incident, edge.To, edge);
        }

        var used = new HashSet<int>();

        while (used.Count < route.Count)
        {
            RoadEdge current = null;
            int at = -1;

            foreach (RoadEdge edge in route)
            {
                if (used.Contains(edge.Id))
                    continue;

                if (incident[edge.From].Count == 1)
                {
                    current = edge;
                    at = edge.From;
                    break;
                }

                if (incident[edge.To].Count == 1)
                {
                    current = edge;
                    at = edge.To;
                    break;
                }

                if (current != null)
                    continue;

                current = edge;
                at = edge.From;
            }

            var points = new List<Vector2>();
            RoadKind kind = current.Kind;

            while (current != null && used.Add(current.Id))
            {
                bool forward = current.From == at;
                int count = current.Points.Length;

                for (int i = points.Count == 0 ? 0 : 1; i < count; i++)
                    points.Add(current.Points[forward ? i : count - 1 - i]);

                at = current.Other(at);
                current = incident[at].Count == 2 ? Unused(incident[at], used) : null;
            }

            if (points.Count >= 2)
                yield return RoadKindProfile.Create(config, points.ToArray(), kind);
        }
    }

    private static RoadEdge Unused(List<RoadEdge> edges, HashSet<int> used)
    {
        foreach (RoadEdge edge in edges)
        {
            if (!used.Contains(edge.Id))
                return edge;
        }

        return null;
    }

    private static void Add(Dictionary<int, List<RoadEdge>> incident, int node, RoadEdge edge)
    {
        if (!incident.TryGetValue(node, out List<RoadEdge> list))
        {
            list = new List<RoadEdge>(2);
            incident[node] = list;
        }

        list.Add(edge);
    }

    public static List<int> SpanningTree(int nodeCount, IReadOnlyList<(int From, int To, float Cost)> sortedEdges, List<int> leftover)
    {
        var parent = new int[nodeCount];

        for (int node = 0; node < nodeCount; node++)
            parent[node] = node;

        var tree = new List<int>(Mathf.Max(0, nodeCount - 1));

        for (int edge = 0; edge < sortedEdges.Count; edge++)
        {
            int from = Find(parent, sortedEdges[edge].From);
            int to = Find(parent, sortedEdges[edge].To);

            if (from == to)
            {
                leftover?.Add(edge);
                continue;
            }

            parent[to] = from;
            tree.Add(edge);
        }

        return tree;
    }

    public static List<int>[] Neighbours(int nodeCount, IReadOnlyList<(int From, int To)> links)
    {
        var neighbours = new List<int>[nodeCount];

        for (int node = 0; node < nodeCount; node++)
            neighbours[node] = new List<int>();

        foreach ((int from, int to) in links)
        {
            if (!neighbours[from].Contains(to))
                neighbours[from].Add(to);

            if (!neighbours[to].Contains(from))
                neighbours[to].Add(from);
        }

        return neighbours;
    }

    public static int Find(int[] parent, int node)
    {
        while (parent[node] != node)
        {
            parent[node] = parent[parent[node]];
            node = parent[node];
        }

        return node;
    }

    public static bool Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA == rootB)
            return false;

        if (rootA < rootB)
            parent[rootB] = rootA;
        else
            parent[rootA] = rootB;

        return true;
    }
}
