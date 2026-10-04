using System.Collections.Generic;
using UnityEngine;

public class RoadSmoother
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

    public static Vector2[] Simplify(Vector2[] points, float tolerance)
    {
        if (points.Length < 3 || tolerance <= 0f)
            return points;

        var keep = new bool[points.Length];

        keep[0] = true;
        keep[^1] = true;

        Divide(points, keep, 0, points.Length - 1, tolerance);

        var result = new List<Vector2>(points.Length);

        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }

        return result.ToArray();
    }

    private static void Divide(Vector2[] points, bool[] keep, int from, int to, float tolerance)
    {
        if (to - from < 2)
            return;

        Vector2 axis = points[to] - points[from];
        float length = axis.magnitude;

        int worst = -1;
        float worstDistance = tolerance;

        for (int i = from + 1; i < to; i++)
        {
            float distance = length <= MIN_SEGMENT
                ? Vector2.Distance(points[i], points[from])
                : Mathf.Abs((points[i].x - points[from].x) * axis.y - (points[i].y - points[from].y) * axis.x) / length;

            if (distance <= worstDistance)
                continue;

            worstDistance = distance;
            worst = i;
        }

        if (worst < 0)
            return;

        keep[worst] = true;

        Divide(points, keep, from, worst, tolerance);
        Divide(points, keep, worst, to, tolerance);
    }

    public static Vector2[] Chaikin(Vector2[] points, int passes)
    {
        Vector2[] current = points;

        for (int pass = 0; pass < passes; pass++)
        {
            if (current.Length < 3)
                break;

            var next = new Vector2[(current.Length - 1) * 2];
            int index = 0;

            next[index++] = current[0];

            for (int i = 1; i < current.Length - 1; i++)
            {
                next[index++] = current[i - 1] * 0.25f + current[i] * 0.75f;
                next[index++] = current[i] * 0.75f + current[i + 1] * 0.25f;
            }

            next[index] = current[^1];
            current = next;
        }

        return current;
    }

    public static Vector2[] Resample(Vector2[] points, float spacing)
    {
        if (points.Length < 2 || spacing <= 0f)
            return points;

        var result = new List<Vector2> { points[0] };

        float travelled = 0f;
        float target = spacing;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 from = points[i];
            Vector2 to = points[i + 1];

            float length = Vector2.Distance(from, to);

            if (length <= MIN_SEGMENT)
                continue;

            while (target <= travelled + length)
            {
                result.Add(Vector2.Lerp(from, to, (target - travelled) / length));
                target += spacing;
            }

            travelled += length;
        }

        Vector2 last = points[^1];

        if (result.Count > 1 && Vector2.Distance(result[^1], last) < spacing * 0.5f)
            result[^1] = last;
        else
            result.Add(last);

        return result.ToArray();
    }

    public static Vector2[] ResampleEven(Vector2[] points, float spacing)
    {
        if (points.Length < 2 || spacing <= 0f)
            return points;

        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        float total = distance[^1];

        if (total <= MIN_SEGMENT)
            return new[] { points[0], points[^1] };

        int count = Mathf.Max(1, Mathf.CeilToInt(total / spacing - SPACING_EPSILON));
        var result = new Vector2[count + 1];
        int segment = 0;

        result[0] = points[0];

        for (int k = 1; k < count; k++)
        {
            float target = total * k / count;

            while (segment < points.Length - 2 && distance[segment + 1] < target)
                segment++;

            float span = distance[segment + 1] - distance[segment];
            float t = span > 1e-6f ? (target - distance[segment]) / span : 0f;

            result[k] = Vector2.Lerp(points[segment], points[segment + 1], t);
        }

        result[count] = points[^1];

        return result;
    }

    public static Vector2[] EnforceRadius(Vector2[] points, float minRadius, float pinStart, float pinEnd, int passes)
    {
        if (points.Length < 3 || minRadius <= 0f)
            return points;

        var result = (Vector2[])points.Clone();
        var distance = new float[result.Length];

        for (int pass = 0; pass < passes; pass++)
        {
            distance[0] = 0f;

            for (int i = 1; i < result.Length; i++)
                distance[i] = distance[i - 1] + Vector2.Distance(result[i - 1], result[i]);

            float total = distance[^1];
            bool changed = false;

            for (int i = 1; i < result.Length - 1; i++)
            {
                if (Circumradius(result[i - 1], result[i], result[i + 1]) >= minRadius)
                    continue;

                bool pinnedStart = distance[i] < pinStart;
                bool pinnedEnd = total - distance[i] < pinEnd;

                if (pinnedStart && !pinnedEnd && i + 1 < result.Length - 1 && distance[i + 1] >= pinStart)
                    changed |= PullTowardTangent(result, i, i - 1, i + 1);

                if (pinnedEnd && !pinnedStart && i - 1 > 0 && total - distance[i - 1] >= pinEnd)
                    changed |= PullTowardTangent(result, i, i + 1, i - 1);

                if (pinnedStart || pinnedEnd)
                    continue;

                result[i] = Vector2.Lerp(result[i], (result[i - 1] + result[i + 1]) * 0.5f, RADIUS_STEP);
                changed = true;
            }

            if (!changed)
                break;
        }

        return result;
    }

    private static bool PullTowardTangent(Vector2[] points, int pinned, int behind, int free)
    {
        Vector2 back = points[pinned] - points[behind];

        if (back.sqrMagnitude < 1e-4f)
            return false;

        Vector2 target = points[pinned] + back.normalized * Vector2.Distance(points[pinned], points[free]);
        points[free] = Vector2.Lerp(points[free], target, RADIUS_STEP);

        return true;
    }

    public static float MinRadius(Vector2[] points, float spacing, float skipStart, float skipEnd)
    {
        if (points == null || points.Length < 3)
            return float.MaxValue;

        Vector2[] even = Resample(points, spacing);
        float total = 0f;

        for (int i = 1; i < even.Length; i++)
            total += Vector2.Distance(even[i - 1], even[i]);

        float travelled = 0f;
        float smallest = float.MaxValue;

        for (int i = 1; i < even.Length - 1; i++)
        {
            travelled += Vector2.Distance(even[i - 1], even[i]);

            if (travelled < skipStart || total - travelled < skipEnd)
                continue;

            smallest = Mathf.Min(smallest, Circumradius(even[i - 1], even[i], even[i + 1]));
        }

        return smallest;
    }

    public static void FacingSpan(Vector2 startPort, Vector2 startTangent, Vector2 endPort, Vector2 endTangent, float minRadius, out float along, out float gap)
    {
        Vector2 axis = (startTangent - endTangent).normalized;
        Vector2 delta = endPort - startPort;
        float lateral = Mathf.Abs(axis.x * delta.y - axis.y * delta.x);

        along = Vector2.Dot(delta, axis);
        gap = lateral <= 2f * minRadius ? Mathf.Sqrt(Mathf.Max(0f, 4f * lateral * minRadius - lateral * lateral)) : 2f * minRadius;
    }

    public static float FacingReach(Vector2 startPort, Vector2 startTangent, Vector2 endPort, Vector2 endTangent, float approach, float minRadius,
        out List<Vector2> curve)
    {
        bool swap = endPort.x < startPort.x || (endPort.x == startPort.x && endPort.y < startPort.y);

        if (!swap)
            return FacingReachOrdered(startPort, startTangent, endPort, endTangent, approach, minRadius, out curve);

        float reach = FacingReachOrdered(endPort, endTangent, startPort, startTangent, approach, minRadius, out curve);

        curve?.Reverse();

        return reach;
    }

    private static float FacingReachOrdered(Vector2 startPort, Vector2 startTangent, Vector2 endPort, Vector2 endTangent, float approach, float minRadius,
        out List<Vector2> curve)
    {
        float radius = minRadius * ARC_RADIUS_SCALE;
        float maxTurn = FACING_MAX_TURN_DEGREES * Mathf.Deg2Rad;

        for (int attempt = 0; attempt <= FACING_TRIES; attempt++)
        {
            float reach = approach * (1f - attempt / (float)FACING_TRIES);
            Vector2 tip = startPort + startTangent * reach;
            Vector2 other = endPort + endTangent * reach;
            List<Vector2> path = Dubins(tip, startTangent, other, -endTangent, radius, ARC_STEP, out float length, out float turning);

            if (path == null || turning > maxTurn || length > Vector2.Distance(tip, other) * FACING_DETOUR)
                continue;

            curve = path;

            return reach;
        }

        curve = null;

        return 0f;
    }

    public static List<Vector2> Dubins(Vector2 from, Vector2 fromHeading, Vector2 to, Vector2 toHeading, float radius, float step,
        out float length, out float turning)
    {
        length = float.MaxValue;
        turning = float.MaxValue;

        if (radius <= 0f || step <= 0f || fromHeading.sqrMagnitude < 1e-8f || toHeading.sqrMagnitude < 1e-8f)
            return null;

        Vector2 startHeading = fromHeading.normalized;
        Vector2 endHeading = toHeading.normalized;
        int best = -1;
        Vector2 bestDirection = Vector2.zero;
        float bestStraight = 0f;
        float bestFirst = 0f;
        float bestSecond = 0f;

        for (int type = 0; type < DUBINS_TYPES; type++)
        {
            bool firstLeft = type == 0 || type == 2;
            bool secondLeft = type == 0 || type == 3;
            Vector2 firstCentre = from + Side(startHeading, firstLeft) * radius;
            Vector2 secondCentre = to + Side(endHeading, secondLeft) * radius;

            if (!CircleTangent(firstCentre, firstLeft, secondCentre, secondLeft, radius, out Vector2 direction, out float straight))
                continue;

            float first = Sweep(startHeading, direction, firstLeft);
            float second = Sweep(direction, endHeading, secondLeft);
            float total = radius * (first + second) + straight;

            if (total >= length)
                continue;

            length = total;
            turning = first + second;
            best = type;
            bestDirection = direction;
            bestStraight = straight;
            bestFirst = first;
            bestSecond = second;
        }

        if (best < 0)
            return null;

        var points = new List<Vector2> { from };

        AppendArc(points, from, startHeading, best == 0 || best == 2, bestFirst, radius, step);

        if (bestStraight > MIN_SEGMENT)
            points.Add(points[^1] + bestDirection * bestStraight);

        AppendArc(points, points[^1], bestDirection, best == 0 || best == 3, bestSecond, radius, step);

        if (points.Count > 1 && (points[^1] - to).sqrMagnitude < MIN_SEGMENT * MIN_SEGMENT)
            points[^1] = to;
        else
            points.Add(to);

        return points;
    }

    private static Vector2 Side(Vector2 heading, bool left)
    {
        return left ? new Vector2(-heading.y, heading.x) : new Vector2(heading.y, -heading.x);
    }

    private static bool CircleTangent(Vector2 firstCentre, bool firstLeft, Vector2 secondCentre, bool secondLeft, float radius,
        out Vector2 direction, out float straight)
    {
        direction = Vector2.zero;
        straight = 0f;

        Vector2 between = secondCentre - firstCentre;
        float distance = between.magnitude;

        if (distance < MIN_SEGMENT)
            return false;

        if (firstLeft == secondLeft)
        {
            direction = between / distance;
            straight = distance;

            return true;
        }

        if (distance < 2f * radius)
            return false;

        float angle = Mathf.Atan2(between.y, between.x) + (firstLeft ? 1f : -1f) * Mathf.Asin(2f * radius / distance);

        direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        straight = Mathf.Sqrt(Mathf.Max(0f, distance * distance - 4f * radius * radius));

        return true;
    }

    private static float Sweep(Vector2 from, Vector2 to, bool left)
    {
        float angle = Mathf.Atan2(from.x * to.y - from.y * to.x, Vector2.Dot(from, to));
        float sweep = left ? angle : -angle;

        if (sweep < 0f)
            sweep += Mathf.PI * 2f;

        return sweep > Mathf.PI * 2f - SWEEP_EPSILON ? 0f : sweep;
    }

    private static void AppendArc(List<Vector2> points, Vector2 start, Vector2 heading, bool left, float sweep, float radius, float step)
    {
        if (sweep <= SWEEP_EPSILON)
            return;

        Vector2 centre = start + Side(heading, left) * radius;
        Vector2 radial = start - centre;
        int count = Mathf.Max(1, Mathf.CeilToInt(radius * sweep / step));
        float sign = left ? 1f : -1f;

        for (int k = 1; k <= count; k++)
        {
            float phi = sign * sweep * k / count;
            float cos = Mathf.Cos(phi);
            float sin = Mathf.Sin(phi);

            points.Add(centre + new Vector2(radial.x * cos - radial.y * sin, radial.x * sin + radial.y * cos));
        }
    }

    public static float Circumradius(Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Vector2.Distance(a, b);
        float bc = Vector2.Distance(b, c);
        float ca = Vector2.Distance(c, a);
        float doubleArea = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));

        if (doubleArea <= 1e-6f)
            return float.MaxValue;

        return ab * bc * ca / (2f * doubleArea);
    }

    public static Vector2[] RoundCorners(Vector2[] points, float radius)
    {
        if (points.Length < 3 || radius <= 0f)
            return points;

        var result = new List<Vector2>(points.Length * 3) { points[0] };

        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector2 previous = points[i - 1];
            Vector2 corner = points[i];
            Vector2 next = points[i + 1];

            Vector2 back = previous - corner;
            Vector2 forward = next - corner;

            float backLength = back.magnitude;
            float forwardLength = forward.magnitude;

            if (backLength <= MIN_SEGMENT || forwardLength <= MIN_SEGMENT)
            {
                result.Add(corner);
                continue;
            }

            float reach = Mathf.Min(radius, backLength * 0.5f, forwardLength * 0.5f);

            Vector2 entry = corner + back / backLength * reach;
            Vector2 exit = corner + forward / forwardLength * reach;

            result.Add(entry);
            result.Add(Vector2.Lerp(Vector2.Lerp(entry, corner, 0.5f), Vector2.Lerp(corner, exit, 0.5f), 0.5f));
            result.Add(exit);
        }

        result.Add(points[^1]);

        return result.ToArray();
    }
}
