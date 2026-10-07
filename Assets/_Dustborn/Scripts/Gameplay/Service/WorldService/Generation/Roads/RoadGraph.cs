using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadGraph
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
}
