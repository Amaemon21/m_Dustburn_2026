using UnityEngine;

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
