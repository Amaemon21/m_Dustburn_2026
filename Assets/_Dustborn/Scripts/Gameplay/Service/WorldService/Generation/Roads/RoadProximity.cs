using System.Collections.Generic;
using UnityEngine;

public class RoadProximity
{
    private struct Segment
    {
        public Vector2 From;
        public Vector2 To;
        public float HalfWidth;
        public bool Alive;
    }

    private readonly List<int>[] _cells;
    private readonly Dictionary<Road, int> _starts = new();
    private readonly Dictionary<Road, int> _counts = new();

    private Segment[] _segments = new Segment[256];
    private int[] _visited = new int[256];
    private int _count;
    private int _query;

    private readonly float _cellSize;
    private readonly int _resolution;

    public int SegmentCount => _count;

    public RoadProximity(int worldSize, float cellSize)
    {
        _cellSize = cellSize;
        _resolution = Mathf.Max(1, Mathf.CeilToInt(worldSize / cellSize));
        _cells = new List<int>[_resolution * _resolution];
    }

    public RoadProximity(IReadOnlyList<Road> roads, int worldSize, float cellSize) : this(worldSize, cellSize)
    {
        AddRange(roads);
    }

    public void AddRange(IReadOnlyList<Road> roads)
    {
        if (roads == null)
            return;

        int needed = _count;

        foreach (Road road in roads)
        {
            if (road?.Points != null && road.Points.Length >= 2)
                needed += road.Points.Length - 1;
        }

        Reserve(needed);

        foreach (Road road in roads)
            Add(road);
    }

    public void Add(Road road)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        float halfWidth = road.Width * 0.5f;
        int start = _count;

        Reserve(_count + road.Points.Length - 1);

        for (int i = 0; i < road.Points.Length - 1; i++)
            Add(road.Points[i], road.Points[i + 1], halfWidth);

        _starts[road] = start;
        _counts[road] = _count - start;
    }

    public void Remove(Road road)
    {
        if (road == null || !_starts.TryGetValue(road, out int start))
            return;

        int count = _counts[road];

        for (int i = 0; i < count; i++)
            _segments[start + i].Alive = false;

        _starts.Remove(road);
        _counts.Remove(road);
    }

    public bool IsWithin(Vector2 point, float clearance)
    {
        int minX = Cell(point.x - clearance);
        int maxX = Cell(point.x + clearance);
        int minY = Cell(point.y - clearance);
        int maxY = Cell(point.y + clearance);

        int query = ++_query;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (int segment in bucket)
                {
                    if (!_segments[segment].Alive || _visited[segment] == query)
                        continue;

                    _visited[segment] = query;

                    float limit = _segments[segment].HalfWidth + clearance;

                    if (DistanceSqr(point, _segments[segment].From, _segments[segment].To) <= limit * limit)
                        return true;
                }
            }
        }

        return false;
    }

    public bool IsParallelWithin(Vector2 point, Vector2 direction, float clearance, float cosLimit, Vector2 ignoreOrigin, float ignoreRadius)
    {
        int minX = Cell(point.x - clearance);
        int maxX = Cell(point.x + clearance);
        int minY = Cell(point.y - clearance);
        int maxY = Cell(point.y + clearance);

        int query = ++_query;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (int segment in bucket)
                {
                    if (!_segments[segment].Alive || _visited[segment] == query)
                        continue;

                    _visited[segment] = query;

                    float limit = _segments[segment].HalfWidth + clearance;

                    if (DistanceSqr(point, _segments[segment].From, _segments[segment].To) > limit * limit)
                        continue;

                    if (Touches(segment, ignoreOrigin, ignoreRadius))
                        continue;

                    Vector2 line = _segments[segment].To - _segments[segment].From;
                    float lineSqr = line.sqrMagnitude;

                    if (lineSqr <= Mathf.Epsilon)
                        continue;

                    float projection = line.x * direction.x + line.y * direction.y;

                    if (projection * projection >= cosLimit * cosLimit * lineSqr)
                        return true;
                }
            }
        }

        return false;
    }

    public bool TryNearestDirection(Vector2 point, float radius, out Vector2 direction)
    {
        direction = Vector2.zero;

        int minX = Cell(point.x - radius);
        int maxX = Cell(point.x + radius);
        int minY = Cell(point.y - radius);
        int maxY = Cell(point.y + radius);

        float nearest = radius * radius;
        Vector2 line = Vector2.zero;

        int query = ++_query;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (int segment in bucket)
                {
                    if (!_segments[segment].Alive || _visited[segment] == query)
                        continue;

                    _visited[segment] = query;

                    float distance = DistanceSqr(point, _segments[segment].From, _segments[segment].To);

                    if (distance >= nearest)
                        continue;

                    Vector2 candidate = _segments[segment].To - _segments[segment].From;

                    if (candidate.sqrMagnitude <= Mathf.Epsilon)
                        continue;

                    nearest = distance;
                    line = candidate;
                }
            }
        }

        if (line.sqrMagnitude <= 0f)
            return false;

        direction = line.normalized;

        return true;
    }

    public bool TryNearestPoint(Vector2 point, float radius, out Vector2 nearest)
    {
        nearest = point;

        int minX = Cell(point.x - radius);
        int maxX = Cell(point.x + radius);
        int minY = Cell(point.y - radius);
        int maxY = Cell(point.y + radius);

        float best = radius * radius;
        bool found = false;
        int query = ++_query;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (int segment in bucket)
                {
                    if (!_segments[segment].Alive || _visited[segment] == query)
                        continue;

                    _visited[segment] = query;

                    Vector2 candidate = Closest(point, _segments[segment].From, _segments[segment].To);
                    float distance = (point - candidate).sqrMagnitude;

                    if (distance >= best)
                        continue;

                    best = distance;
                    nearest = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    public bool Crosses(Vector2 from, Vector2 to, Vector2 ignoreOrigin, float ignoreRadius, out Vector2 point, out float transverse)
    {
        point = to;
        transverse = 0f;

        Vector2 step = to - from;
        float length = step.magnitude;

        if (length <= Mathf.Epsilon)
            return false;

        Vector2 direction = step / length;
        float nearest = float.MaxValue;
        Vector2 hitLine = Vector2.zero;

        int minX = Cell(Mathf.Min(from.x, to.x));
        int maxX = Cell(Mathf.Max(from.x, to.x));
        int minY = Cell(Mathf.Min(from.y, to.y));
        int maxY = Cell(Mathf.Max(from.y, to.y));

        int query = ++_query;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (int segment in bucket)
                {
                    if (!_segments[segment].Alive || _visited[segment] == query)
                        continue;

                    _visited[segment] = query;

                    if (!Intersect(from, step, _segments[segment].From, _segments[segment].To, out float travel))
                        continue;

                    if (travel * length >= nearest)
                        continue;

                    if (Touches(segment, ignoreOrigin, ignoreRadius))
                        continue;

                    Vector2 line = _segments[segment].To - _segments[segment].From;

                    if (line.sqrMagnitude <= Mathf.Epsilon)
                        continue;

                    nearest = travel * length;
                    point = from + step * travel;
                    hitLine = line;
                }
            }
        }

        if (nearest >= float.MaxValue)
            return false;

        Vector2 other = hitLine.normalized;

        transverse = Mathf.Abs(direction.x * other.y - direction.y * other.x);

        return true;
    }

    private void Reserve(int capacity)
    {
        if (capacity <= _segments.Length)
            return;

        int size = _segments.Length;

        while (size < capacity)
            size *= 2;

        System.Array.Resize(ref _segments, size);
        System.Array.Resize(ref _visited, size);
    }

    private void Add(Vector2 from, Vector2 to, float halfWidth)
    {
        Reserve(_count + 1);

        int index = _count++;

        _segments[index] = new Segment { From = from, To = to, HalfWidth = halfWidth, Alive = true };

        int minX = Cell(Mathf.Min(from.x, to.x) - halfWidth);
        int maxX = Cell(Mathf.Max(from.x, to.x) + halfWidth);
        int minY = Cell(Mathf.Min(from.y, to.y) - halfWidth);
        int maxY = Cell(Mathf.Max(from.y, to.y) + halfWidth);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cell = y * _resolution + x;

                _cells[cell] ??= new List<int>();
                _cells[cell].Add(index);
            }
        }
    }

    private bool Touches(int segment, Vector2 origin, float radius)
    {
        if (radius <= 0f)
            return false;

        return DistanceSqr(origin, _segments[segment].From, _segments[segment].To) <= radius * radius;
    }

    private int Cell(float coordinate)
    {
        return Mathf.Clamp(Mathf.FloorToInt(coordinate / _cellSize), 0, _resolution - 1);
    }

    private static bool Intersect(Vector2 origin, Vector2 step, Vector2 from, Vector2 to, out float travel)
    {
        travel = 0f;

        Vector2 line = to - from;
        float denominator = step.x * line.y - step.y * line.x;

        if (Mathf.Abs(denominator) < Mathf.Epsilon)
            return false;

        Vector2 delta = from - origin;

        float t = (delta.x * line.y - delta.y * line.x) / denominator;
        float u = (delta.x * step.y - delta.y * step.x) / denominator;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        travel = t;

        return true;
    }

    private static float DistanceSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        return (point - Closest(point, from, to)).sqrMagnitude;
    }

    private static Vector2 Closest(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;

        if (lengthSqr <= Mathf.Epsilon)
            return from;

        float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr);

        return from + line * t;
    }
}
