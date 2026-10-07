using System.Collections.Generic;
using UnityEngine;

public partial class RoadPlanner
{
    private void Commit(Journey journey)
    {
        foreach (Route piece in journey.Pieces)
            Commit(piece);

        Rides += journey.Rides;
    }

    private void RefreshCrossings(Route route)
    {
        Vector2[] points = route.Points;
        float total = Length(points);
        float sinJunction = Mathf.Sin(_config.JunctionAngle * Mathf.Deg2Rad);
        float travelled = 0f;

        route.Crossings.Clear();

        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);

            if (length <= Mathf.Epsilon)
                continue;

            FindCrossings(route, points[i], points[i + 1], travelled, (points[i + 1] - points[i]) / length, sinJunction, total);
            travelled += length;
        }
    }

    private void Commit(Route route)
    {
        RefreshCrossings(route);
        route.Crossings.Sort((left, right) => left.Along.CompareTo(right.Along));

        int startNode = NodeFor(route.Start);
        var nodes = new List<(float Along, int Node)> { (0f, startNode) };

        foreach ((float along, int edge, float edgeAlong, Vector2 _) in route.Crossings)
        {
            int node = _graph.Split(edge, edgeAlong);

            if (nodes[^1].Node == node)
                continue;

            nodes.Add((along, node));
            Crossings++;
        }

        int endNode = NodeFor(route.End);

        nodes.Add((Length(route.Points), endNode));

        int routeId = _graph.NewRoute();
        Vector2[] points = route.Points;

        route.Id = routeId;
        float[] distance = Cumulative(points);

        for (int k = 0; k < nodes.Count - 1; k++)
        {
            if (nodes[k].Node == nodes[k + 1].Node)
                continue;

            var piece = new List<Vector2> { _graph.Nodes[nodes[k].Node].Position };

            for (int i = 1; i < points.Length - 1; i++)
            {
                if (distance[i] > nodes[k].Along + JUNCTION_WELD && distance[i] < nodes[k + 1].Along - JUNCTION_WELD)
                    piece.Add(points[i]);
            }

            piece.Add(_graph.Nodes[nodes[k + 1].Node].Position);

            int edge = _graph.AddEdge(nodes[k].Node, nodes[k + 1].Node, piece, RoadKind.Highway, routeId);

            Rasterise(_graph.Edges[edge]);
            IndexSegments(_graph.Edges[edge]);
        }

        IndexSplits();

        if (route.Shallow > 0)
            ShallowJunctions += route.Shallow;

        CommittedIntrusions += route.Intrusions;
        CommittedParallel += route.Parallel > 1 ? 1 : 0;
        Problems.AddRange(route.Problems);
    }

    private int NodeFor(Endpoint endpoint)
    {
        if (endpoint.Direct)
            return endpoint.Gateway.Node;

        Joins++;

        return _graph.Split(endpoint.Edge, endpoint.Along);
    }

    private void IndexSplits()
    {
        for (int i = _indexedEdges; i < _graph.Edges.Count; i++)
        {
            RoadEdge edge = _graph.Edges[i];

            if (edge.Alive && !_indexed.Contains(edge.Id))
            {
                IndexSegments(edge);
                Rasterise(edge);
            }
        }

        _indexedEdges = _graph.Edges.Count;
    }

    private void IndexSegments(RoadEdge edge)
    {
        if (!_indexed.Add(edge.Id))
            return;

        for (int segment = 0; segment < edge.Points.Length - 1; segment++)
        {
            Vector2 a = edge.Points[segment];
            Vector2 b = edge.Points[segment + 1];

            int minX = SegmentCell(Mathf.Min(a.x, b.x));
            int maxX = SegmentCell(Mathf.Max(a.x, b.x));
            int minY = SegmentCell(Mathf.Min(a.y, b.y));
            int maxY = SegmentCell(Mathf.Max(a.y, b.y));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int key = y * 65536 + x;

                    if (!_segments.TryGetValue(key, out List<long> list))
                    {
                        list = new List<long>();
                        _segments[key] = list;
                    }

                    list.Add(((long)edge.Id << 20) | (long)segment);
                }
            }
        }
    }

    private IEnumerable<long> SegmentsNear(Vector2 from, Vector2 to)
    {
        int minX = SegmentCell(Mathf.Min(from.x, to.x));
        int maxX = SegmentCell(Mathf.Max(from.x, to.x));
        int minY = SegmentCell(Mathf.Min(from.y, to.y));
        int maxY = SegmentCell(Mathf.Max(from.y, to.y));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_segments.TryGetValue(y * 65536 + x, out List<long> list))
                    continue;

                foreach (long entry in list)
                    yield return entry;
            }
        }
    }

    private int SegmentCell(float coordinate)
    {
        return Mathf.Max(0, Mathf.FloorToInt(coordinate / SEGMENT_CELL));
    }
}
