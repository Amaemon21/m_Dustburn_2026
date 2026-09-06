using System.Collections.Generic;
using UnityEngine;

public class StreetStitcher
{
    private const int TURN_CANDIDATES = 7;

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly RoadProximity _index;

    private readonly List<Vector2> _points = new();
    private readonly List<Road> _anchored = new();
    private readonly List<List<Road>> _islands = new();

    public int Linked { get; private set; }
    public int Dropped { get; private set; }
    public int Islands { get; private set; }

    public StreetStitcher(WorldGenerationConfig config, HeightMap map, RoadProximity index)
    {
        _config = config;
        _map = map;
        _index = index;
    }

    public void Connect(CityLayout layout, IReadOnlyList<Road> trunks)
    {
        Partition(layout, trunks);

        Islands += _islands.Count;

        foreach (List<Road> island in _islands)
        {
            if (TryLink(island, out Road link))
            {
                layout.Streets.Add(link);
                _index.Add(link);

                _anchored.AddRange(island);
                _anchored.Add(link);

                Linked++;

                continue;
            }

            foreach (Road road in island)
            {
                layout.Streets.Remove(road);
                _index.Remove(road);

                Dropped++;
            }
        }
    }

    private void Partition(CityLayout layout, IReadOnlyList<Road> trunks)
    {
        _anchored.Clear();
        _islands.Clear();

        var all = new List<Road>(trunks);

        all.AddRange(layout.Streets);

        int[] parent = new int[all.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        for (int i = 1; i < trunks.Count; i++)
            Union(parent, 0, i);

        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                if (Touches(all[i], all[j]))
                    Union(parent, i, j);
            }
        }

        int anchor = trunks.Count > 0 ? Find(parent, 0) : Largest(parent, all.Count);

        var groups = new Dictionary<int, List<Road>>();

        for (int i = 0; i < all.Count; i++)
        {
            int root = Find(parent, i);

            if (root == anchor)
            {
                _anchored.Add(all[i]);
                continue;
            }

            if (!groups.TryGetValue(root, out List<Road> group))
            {
                group = new List<Road>();
                groups[root] = group;
            }

            group.Add(all[i]);
        }

        foreach (KeyValuePair<int, List<Road>> pair in groups)
            _islands.Add(pair.Value);
    }

    private bool TryLink(List<Road> island, out Road link)
    {
        link = null;

        if (_anchored.Count == 0)
            return false;

        Vector2 from = Vector2.zero;
        Vector2 target = Vector2.zero;
        float best = float.MaxValue;

        foreach (Road road in island)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            for (int end = 0; end < 2; end++)
            {
                Vector2 point = end == 0 ? road.Points[0] : road.Points[road.Points.Length - 1];
                float distance = NearestSqr(point, out Vector2 hit);

                if (distance >= best)
                    continue;

                best = distance;
                from = point;
                target = hit;
            }
        }

        return best < float.MaxValue && Pave(from, target, out link);
    }

    private float NearestSqr(Vector2 point, out Vector2 hit)
    {
        float best = float.MaxValue;

        hit = point;

        foreach (Road road in _anchored)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            for (int i = 0; i < road.Points.Length - 1; i++)
            {
                float distance = ProjectSqr(point, road.Points[i], road.Points[i + 1], out Vector2 candidate);

                if (distance >= best)
                    continue;

                best = distance;
                hit = candidate;
            }
        }

        return best;
    }

    private bool Pave(Vector2 from, Vector2 target, out Road link)
    {
        link = null;

        float step = _config.StreetStepLength;
        float span = Vector2.Distance(from, target);

        if (span <= Mathf.Epsilon)
            return false;

        float budget = span * 2.5f + step * 2f;

        _points.Clear();
        _points.Add(from);

        Vector2 position = from;
        Vector2 direction = (target - from) / span;
        float travelled = 0f;

        float stepSqr = step * step;

        while (travelled < budget)
        {
            if (DistanceUtility.SqrDistance(position, target) <= stepSqr)
            {
                _points.Add(target);

                link = new Road(_points.ToArray(), _config.StreetHalfWidth * 2f);

                return true;
            }

            if (!TryStep(position, target, ref direction, step, out Vector2 next))
                return false;

            _points.Add(next);

            position = next;
            travelled += step;
        }

        return false;
    }

    private bool TryStep(Vector2 position, Vector2 target, ref Vector2 direction, float step, out Vector2 next)
    {
        float turn = _config.StreetMaxTurn * Mathf.Deg2Rad;
        float fan = Mathf.Max(1f - Mathf.Cos(turn), Mathf.Epsilon);
        float ground = _map.SampleWorld(new Vector3(position.x, 0f, position.y));

        Vector2 wanted = (target - position).normalized;
        Vector2 bestDirection = direction;
        float bestScore = float.MaxValue;

        next = Vector2.zero;

        for (int i = 0; i < TURN_CANDIDATES; i++)
        {
            float angle = Mathf.Lerp(-turn, turn, i / (TURN_CANDIDATES - 1f));

            Vector2 candidateDirection = Rotate(direction, angle);
            Vector2 candidate = position + candidateDirection * step;

            if (!IsInsideWorld(candidate))
                continue;

            float height = _map.SampleWorld(new Vector3(candidate.x, 0f, candidate.y));
            float slope = Mathf.Abs(height - ground) / step;

            if (slope > _config.MaxStreetSlope)
                continue;

            float deviation = (1f - Vector2.Dot(candidateDirection, wanted)) / fan;
            float score = deviation + slope / _config.MaxStreetSlope * 0.5f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestDirection = candidateDirection;
            next = candidate;
        }

        if (bestScore == float.MaxValue)
            return false;

        direction = bestDirection;

        return true;
    }

    private bool IsInsideWorld(Vector2 point)
    {
        return point.x >= 0f && point.y >= 0f && point.x <= _config.WorldSize && point.y <= _config.WorldSize;
    }

    private static bool Touches(Road a, Road b)
    {
        if (a.Points == null || b.Points == null || a.Points.Length < 2 || b.Points.Length < 2)
            return false;

        float limit = (a.Width + b.Width) * 0.5f;
        float limitSqr = limit * limit;

        for (int i = 0; i < a.Points.Length - 1; i++)
        {
            for (int j = 0; j < b.Points.Length - 1; j++)
            {
                if (SegmentSqrDistance(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1]) <= limitSqr)
                    return true;
            }
        }

        return false;
    }

    private static float SegmentSqrDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        float best = ProjectSqr(a0, b0, b1, out _);

        best = Mathf.Min(best, ProjectSqr(a1, b0, b1, out _));
        best = Mathf.Min(best, ProjectSqr(b0, a0, a1, out _));
        best = Mathf.Min(best, ProjectSqr(b1, a0, a1, out _));

        return best;
    }

    private static float ProjectSqr(Vector2 point, Vector2 from, Vector2 to, out Vector2 hit)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;

        if (lengthSqr <= Mathf.Epsilon)
        {
            hit = from;

            return DistanceUtility.SqrDistance(point, from);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr);

        hit = from + line * t;

        return DistanceUtility.SqrDistance(point, hit);
    }

    private static int Largest(int[] parent, int count)
    {
        var sizes = new Dictionary<int, int>();
        int best = 0;
        int bestSize = 0;

        for (int i = 0; i < count; i++)
        {
            int root = Find(parent, i);

            sizes.TryGetValue(root, out int size);
            sizes[root] = ++size;

            if (size <= bestSize)
                continue;

            bestSize = size;
            best = root;
        }

        return best;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
            parent[rootB] = rootA;
    }

    private static Vector2 Rotate(Vector2 direction, float angle)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
    }
}
