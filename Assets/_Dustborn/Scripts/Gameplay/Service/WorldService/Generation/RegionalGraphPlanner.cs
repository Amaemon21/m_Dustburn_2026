using System.Collections.Generic;
using UnityEngine;

public sealed class RegionalGraphPlanner
{
    private const float SAMPLE_STEP = 48f;
    private const int NEAREST = 3;
    private const int EARTHWORK_WINDOW = 4;
    private const float INTRUSION_PENALTY = 4f;
    private const float IMPORTANCE_EXPONENT = 0.25f;

    private sealed class Candidate
    {
        public int From;
        public int To;
        public float Length;
        public float Estimate;
        public float Water;
        public bool Used;

        public int Other(int node)
        {
            return node == From ? To : From;
        }
    }

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;

    public int Candidates { get; private set; }
    public int Backbone { get; private set; }
    public int Loops { get; private set; }
    public int RefusedAngle { get; private set; }
    public int RefusedCrossing { get; private set; }
    public int RefusedSpacing { get; private set; }
    public int RefusedDegree { get; private set; }
    public int RefusedDetour { get; private set; }

    public RegionalGraphPlanner(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;
    }

    public List<RegionalLink> Plan(IReadOnlyList<Hub> hubs)
    {
        var links = new List<RegionalLink>();
        int count = hubs.Count;

        if (count < 2)
            return links;

        List<Candidate> candidates = Collect(hubs);
        Candidates = candidates.Count;

        var degree = new int[count];
        var chosen = new List<Candidate>();

        BuildBackbone(hubs, candidates, degree, chosen);
        Backbone = chosen.Count;

        float[] weights = TreeWeights(hubs, chosen);
        var ordered = new List<(Candidate Link, float Priority, bool Backbone)>();

        for (int i = 0; i < chosen.Count; i++)
            ordered.Add((chosen[i], weights[i], true));

        ordered.Sort((left, right) =>
        {
            int compare = right.Priority.CompareTo(left.Priority);
            return compare != 0 ? compare : left.Link.Estimate.CompareTo(right.Link.Estimate);
        });

        List<Candidate> loops = AddLoops(hubs, candidates, degree, chosen);
        Loops = loops.Count;

        for (int i = 0; i < loops.Count; i++)
            ordered.Add((loops[i], -1f - i, false));

        foreach ((Candidate link, float priority, bool backbone) in ordered)
            links.Add(new RegionalLink(link.From, link.To, backbone, link.Estimate, priority));

        Debug.Log($"Regional graph: {Candidates} candidate links, {Backbone} backbone and {Loops} loops over {count} settlements ({links.Count / (float)count:F2} links per settlement). Loops refused: {RefusedDetour} saving too little, {RefusedAngle} too shallow, {RefusedCrossing} crossing, {RefusedSpacing} too close to another loop, {RefusedDegree} over the link cap");

        return links;
    }

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

            if (_config.SeaLevel > 0f && heights[i] < _config.SeaLevel + _config.ShoreMargin)
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

    private List<Candidate> AddLoops(IReadOnlyList<Hub> hubs, List<Candidate> candidates, int[] degree, List<Candidate> chosen)
    {
        var loops = new List<Candidate>();
        int count = hubs.Count;
        int target = Mathf.FloorToInt(count * _config.TargetLinkRatio);
        var midpoints = new List<Vector2>();

        while (chosen.Count < target)
        {
            float[,] network = NetworkDistances(count, chosen);
            Candidate best = null;
            float bestBenefit = 0f;

            RefusedAngle = RefusedCrossing = RefusedSpacing = RefusedDegree = RefusedDetour = 0;

            foreach (Candidate candidate in candidates)
            {
                if (candidate.Used || candidate.Length > _config.MaxLinkLength || candidate.Water > _config.MaxWaterExposure)
                    continue;

                float ratio = network[candidate.From, candidate.To] / Mathf.Max(1f, candidate.Estimate);

                if (ratio < _config.MinLoopDetour)
                {
                    RefusedDetour++;
                    continue;
                }

                if (degree[candidate.From] >= MaxLinks(hubs[candidate.From]) || degree[candidate.To] >= MaxLinks(hubs[candidate.To]))
                {
                    RefusedDegree++;
                    continue;
                }

                if (IsShallow(hubs, candidate, chosen))
                {
                    RefusedAngle++;
                    continue;
                }

                if (Crosses(hubs, candidate, chosen))
                {
                    RefusedCrossing++;
                    continue;
                }

                Vector2 middle = (hubs[candidate.From].Position + hubs[candidate.To].Position) * 0.5f;

                if (IsCrowded(midpoints, middle))
                {
                    RefusedSpacing++;
                    continue;
                }

                float benefit = (ratio - 1f) * Mathf.Sqrt(Importance(hubs[candidate.From]) * Importance(hubs[candidate.To]));

                if (benefit <= bestBenefit)
                    continue;

                bestBenefit = benefit;
                best = candidate;
            }

            if (best == null)
                break;

            best.Used = true;
            degree[best.From]++;
            degree[best.To]++;
            chosen.Add(best);
            loops.Add(best);
            midpoints.Add((hubs[best.From].Position + hubs[best.To].Position) * 0.5f);
        }

        return loops;
    }

    private static float[,] NetworkDistances(int count, List<Candidate> links)
    {
        var distance = new float[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
                distance[i, j] = i == j ? 0f : float.MaxValue * 0.25f;
        }

        foreach (Candidate link in links)
        {
            distance[link.From, link.To] = Mathf.Min(distance[link.From, link.To], link.Estimate);
            distance[link.To, link.From] = distance[link.From, link.To];
        }

        for (int k = 0; k < count; k++)
        {
            for (int i = 0; i < count; i++)
            {
                float viaK = distance[i, k];

                for (int j = 0; j < count; j++)
                {
                    float candidate = viaK + distance[k, j];

                    if (candidate < distance[i, j])
                        distance[i, j] = candidate;
                }
            }
        }

        return distance;
    }

    private bool IsShallow(IReadOnlyList<Hub> hubs, Candidate candidate, List<Candidate> links)
    {
        float cosLimit = Mathf.Cos(_config.MinLinkAngle * Mathf.Deg2Rad);

        foreach (Candidate link in links)
        {
            if (Shallow(hubs, candidate.From, candidate.To, link, cosLimit) || Shallow(hubs, candidate.To, candidate.From, link, cosLimit))
                return true;
        }

        return false;
    }

    private static bool Shallow(IReadOnlyList<Hub> hubs, int node, int toward, Candidate link, float cosLimit)
    {
        if (link.From != node && link.To != node)
            return false;

        Vector2 origin = hubs[node].Position;
        Vector2 first = (hubs[toward].Position - origin).normalized;
        Vector2 second = (hubs[link.Other(node)].Position - origin).normalized;

        return Vector2.Dot(first, second) > cosLimit;
    }

    private static bool Crosses(IReadOnlyList<Hub> hubs, Candidate candidate, List<Candidate> links)
    {
        Vector2 a = hubs[candidate.From].Position;
        Vector2 b = hubs[candidate.To].Position;

        foreach (Candidate link in links)
        {
            if (link.From == candidate.From || link.From == candidate.To || link.To == candidate.From || link.To == candidate.To)
                continue;

            if (SegmentsCross(a, b, hubs[link.From].Position, hubs[link.To].Position))
                return true;
        }

        return false;
    }

    private bool IsCrowded(List<Vector2> midpoints, Vector2 middle)
    {
        float limitSqr = _config.LoopSpacing * _config.LoopSpacing;

        foreach (Vector2 other in midpoints)
        {
            if ((other - middle).sqrMagnitude < limitSqr)
                return true;
        }

        return false;
    }

    private int MaxLinks(Hub hub)
    {
        SettlementTypeProfile profile = _config.Profile(hub.Type);

        return profile == null ? 2 : Mathf.Max(1, profile.MaxLinks);
    }

    private float Importance(Hub hub)
    {
        SettlementTypeProfile profile = _config.Profile(hub.Type);

        return profile == null ? 1f : Mathf.Max(0.1f, profile.Importance);
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(c, d, a);
        float d2 = Cross(c, d, b);
        float d3 = Cross(a, b, c);
        float d4 = Cross(a, b, d);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Cross(Vector2 origin, Vector2 end, Vector2 point)
    {
        return (end.x - origin.x) * (point.y - origin.y) - (end.y - origin.y) * (point.x - origin.x);
    }

    private static float DistanceSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;
        float t = lengthSqr > Mathf.Epsilon ? Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr) : 0f;

        return (point - (from + line * t)).sqrMagnitude;
    }

    private static long Key(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);

        return ((long)low << 32) | (uint)high;
    }
}
