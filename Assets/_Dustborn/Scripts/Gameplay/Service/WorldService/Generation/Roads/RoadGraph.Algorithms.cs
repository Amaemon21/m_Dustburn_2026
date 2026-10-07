using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadGraph
{
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
