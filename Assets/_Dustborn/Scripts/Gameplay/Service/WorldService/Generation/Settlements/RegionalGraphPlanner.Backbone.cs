using System.Collections.Generic;
using UnityEngine;

public sealed partial class RegionalGraphPlanner
{
    private List<Candidate> Collect(IReadOnlyList<Hub> hubs)
    {
        var positions = new List<Vector2>(hubs.Count);

        foreach (Hub hub in hubs)
            positions.Add(hub.Position);

        var keys = new SortedSet<long>();

        foreach ((int from, int to) in Delaunay.Edges(positions))
            keys.Add(Key(from, to));

        foreach ((int from, int to) in Delaunay.Nearest(positions, NEAREST))
            keys.Add(Key(from, to));

        var candidates = new List<Candidate>(keys.Count);

        foreach (long key in keys)
        {
            int from = (int)(key >> 32);
            int to = (int)(key & 0xffffffffL);

            candidates.Add(Measure(hubs, from, to));
        }

        return candidates;
    }

    private Candidate Measure(IReadOnlyList<Hub> hubs, int from, int to)
    {
        Vector2 start = hubs[from].Position;
        Vector2 end = hubs[to].Position;
        float length = Vector2.Distance(start, end);
        int steps = Mathf.Max(2, Mathf.CeilToInt(length / SAMPLE_STEP));
        float step = length / steps;

        var heights = new float[steps + 1];
        int wet = 0;

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(start, end, i / (float)steps);

            heights[i] = _map.SampleWorldSmooth(point.x, point.y);

            if (WaterMap.Wet(_config, _water, point.x, point.y, heights[i]))
                wet++;
        }

        float cost = 0f;
        float earthwork = 0f;

        for (int i = 0; i < steps; i++)
        {
            float grade = Mathf.Abs(heights[i + 1] - heights[i]) / Mathf.Max(1f, step);

            cost += step * (1f + _config.RoadSlopePenalty * grade * grade);
        }

        for (int i = 0; i <= steps; i++)
        {
            float sum = 0f;
            int samples = 0;

            for (int k = -EARTHWORK_WINDOW; k <= EARTHWORK_WINDOW; k++)
            {
                sum += heights[Mathf.Clamp(i + k, 0, steps)];
                samples++;
            }

            earthwork += Mathf.Abs(heights[i] - sum / samples);
        }

        float water = wet / (float)(steps + 1);
        float estimate = cost * (1f + _config.LinkWaterPenalty * water) * (1f + _config.LinkEarthworkWeight * earthwork / (steps + 1));

        if (Intrudes(hubs, from, to))
            estimate *= INTRUSION_PENALTY;

        return new Candidate { From = from, To = to, Length = length, Estimate = estimate, Water = water };
    }

    private static bool Intrudes(IReadOnlyList<Hub> hubs, int from, int to)
    {
        Vector2 start = hubs[from].Position;
        Vector2 end = hubs[to].Position;

        for (int i = 0; i < hubs.Count; i++)
        {
            if (i == from || i == to)
                continue;

            float radius = hubs[i].Radius;

            if (DistanceSqr(hubs[i].Position, start, end) < radius * radius)
                return true;
        }

        return false;
    }

    private void BuildBackbone(IReadOnlyList<Hub> hubs, List<Candidate> candidates, int[] degree, List<Candidate> chosen)
    {
        var sorted = new List<Candidate>(candidates);

        sorted.Sort((left, right) =>
        {
            int compare = Score(hubs, left).CompareTo(Score(hubs, right));
            return compare != 0 ? compare : Key(left.From, left.To).CompareTo(Key(right.From, right.To));
        });

        var parent = new int[hubs.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (Candidate candidate in sorted)
            {
                if (candidate.Used)
                    continue;

                if (pass == 0 && (degree[candidate.From] >= MaxLinks(hubs[candidate.From]) || degree[candidate.To] >= MaxLinks(hubs[candidate.To])))
                    continue;

                if (!RoadGraph.Union(parent, candidate.From, candidate.To))
                    continue;

                candidate.Used = true;
                degree[candidate.From]++;
                degree[candidate.To]++;
                chosen.Add(candidate);
            }
        }
    }

    private float Score(IReadOnlyList<Hub> hubs, Candidate candidate)
    {
        float importance = Importance(hubs[candidate.From]) * Importance(hubs[candidate.To]);

        return candidate.Estimate / Mathf.Pow(Mathf.Max(0.01f, importance), IMPORTANCE_EXPONENT);
    }

    private float[] TreeWeights(IReadOnlyList<Hub> hubs, List<Candidate> tree)
    {
        int count = hubs.Count;
        var adjacency = new List<(int Node, int Edge)>[count];

        for (int i = 0; i < count; i++)
            adjacency[i] = new List<(int, int)>();

        for (int i = 0; i < tree.Count; i++)
        {
            adjacency[tree[i].From].Add((tree[i].To, i));
            adjacency[tree[i].To].Add((tree[i].From, i));
        }

        float total = 0f;

        foreach (Hub hub in hubs)
            total += Importance(hub);

        var below = new float[count];
        var parentEdge = new int[count];
        var visited = new bool[count];
        var order = new List<int>(count);
        var stack = new Stack<int>();

        for (int root = 0; root < count; root++)
        {
            if (visited[root])
                continue;

            visited[root] = true;
            parentEdge[root] = -1;
            stack.Push(root);

            while (stack.Count > 0)
            {
                int node = stack.Pop();
                order.Add(node);

                foreach ((int next, int edge) in adjacency[node])
                {
                    if (visited[next])
                        continue;

                    visited[next] = true;
                    parentEdge[next] = edge;
                    stack.Push(next);
                }
            }
        }

        var weights = new float[tree.Count];

        for (int i = order.Count - 1; i >= 0; i--)
        {
            int node = order[i];
            below[node] += Importance(hubs[node]);

            int edge = parentEdge[node];

            if (edge < 0)
                continue;

            weights[edge] = below[node] * (total - below[node]);
            below[tree[edge].Other(node)] += below[node];
        }

        return weights;
    }
}
