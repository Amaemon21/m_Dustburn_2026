using System.Collections.Generic;
using UnityEngine;

public partial class RoadSmoother
{
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
}
