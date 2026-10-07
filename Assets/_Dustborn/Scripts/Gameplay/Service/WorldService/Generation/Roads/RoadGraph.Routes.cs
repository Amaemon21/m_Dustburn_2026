using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadGraph
{
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
}
