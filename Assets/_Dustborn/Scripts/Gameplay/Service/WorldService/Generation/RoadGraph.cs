using System.Collections.Generic;
using UnityEngine;

public static class RoadGraph
{
    public static List<(int From, int To)> Link(IReadOnlyList<Hub> hubs, int extraEdges)
    {
        var edges = new List<(int From, int To, float Cost)>();

        for (int i = 0; i < hubs.Count; i++)
        {
            for (int j = i + 1; j < hubs.Count; j++)
                edges.Add((i, j, (hubs[i].Position - hubs[j].Position).sqrMagnitude));
        }

        edges.Sort((left, right) => left.Cost.CompareTo(right.Cost));

        var leftover = new List<int>();
        List<int> tree = SpanningTree(hubs.Count, edges, leftover);
        int extra = Mathf.Clamp(extraEdges, 0, leftover.Count);

        var links = new List<(int From, int To)>(tree.Count + extra);

        foreach (int edge in tree)
            links.Add((edges[edge].From, edges[edge].To));

        for (int i = 0; i < extra; i++)
            links.Add((edges[leftover[i]].From, edges[leftover[i]].To));

        return links;
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

    private static int Find(int[] parent, int node)
    {
        while (parent[node] != node)
        {
            parent[node] = parent[parent[node]];
            node = parent[node];
        }

        return node;
    }
}
