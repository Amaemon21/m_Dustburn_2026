using UnityEngine;

public partial class RoadSmoother
{
    public const float ARC_STEP = 2f;
    public const float ARC_RADIUS_SCALE = 1.03f;

    private const float MIN_SEGMENT = 0.05f;
    private const float RADIUS_STEP = 0.5f;
    private const int DUBINS_TYPES = 4;
    private const float SWEEP_EPSILON = 1e-4f;
    private const float SPACING_EPSILON = 1e-3f;
    private const int FACING_TRIES = 12;
    private const float FACING_MAX_TURN_DEGREES = 135f;
    private const float FACING_DETOUR = 2f;

    private readonly HeightMap _map;

    public float SimplifyTolerance { get; set; } = 12f;
    public int ChaikinPasses { get; set; } = 3;
    public int RelaxPasses { get; set; } = 20;
    public float RelaxStrength { get; set; } = 0.4f;
    public float Corridor { get; set; } = 40f;
    public float MaxGrade { get; set; } = 0.1f;
    public float Spacing { get; set; } = 12f;
    public float MaxCrossSlope { get; set; } = float.MaxValue;
    public float CrossProbe { get; set; }

    public RoadSmoother(HeightMap map)
    {
        _map = map;
    }

    public Vector2[] Smooth(Vector2[] points)
    {
        if (points == null || points.Length < 3)
            return points;

        Vector2[] anchors = Simplify(points, SimplifyTolerance);
        Vector2[] curve = Resample(Chaikin(anchors, ChaikinPasses), Spacing);

        Relax(curve);

        return Resample(curve, Spacing);
    }

    private void Relax(Vector2[] points)
    {
        if (points.Length < 3 || RelaxPasses <= 0 || RelaxStrength <= 0f)
            return;

        var anchors = (Vector2[])points.Clone();

        for (int pass = 0; pass < RelaxPasses; pass++)
        {
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 target = (points[i - 1] + points[i + 1]) * 0.5f;
                Vector2 moved = Vector2.Lerp(points[i], target, RelaxStrength);

                moved = ClampToCorridor(anchors[i], moved);

                if (IsSteeper(points[i - 1], points[i], points[i + 1], moved))
                    continue;

                points[i] = moved;
            }
        }
    }

    private Vector2 ClampToCorridor(Vector2 anchor, Vector2 moved)
    {
        Vector2 delta = moved - anchor;

        if (delta.sqrMagnitude <= Corridor * Corridor)
            return moved;

        return anchor + delta.normalized * Corridor;
    }

    private bool IsSteeper(Vector2 previous, Vector2 current, Vector2 next, Vector2 moved)
    {
        float before = Mathf.Max(Grade(previous, current), Grade(current, next));
        float after = Mathf.Max(Grade(previous, moved), Grade(moved, next));

        if (after > MaxGrade && after > before)
            return true;

        if (CrossProbe <= 0f)
            return false;

        Vector2 direction = next - previous;
        float tilt = CrossSlope(moved, direction);

        return tilt > MaxCrossSlope && tilt > CrossSlope(current, direction);
    }

    private float CrossSlope(Vector2 point, Vector2 direction)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return 0f;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * CrossProbe;
        float left = _map.SampleWorldSmooth(point.x - normal.x, point.y - normal.y);
        float right = _map.SampleWorldSmooth(point.x + normal.x, point.y + normal.y);

        return Mathf.Abs(right - left) / (2f * CrossProbe);
    }

    private float Grade(Vector2 from, Vector2 to)
    {
        float distance = Vector2.Distance(from, to);

        if (distance <= MIN_SEGMENT)
            return 0f;

        float fromHeight = _map.SampleWorld(new Vector3(from.x, 0f, from.y));
        float toHeight = _map.SampleWorld(new Vector3(to.x, 0f, to.y));

        return Mathf.Abs(toHeight - fromHeight) / distance;
    }
}
