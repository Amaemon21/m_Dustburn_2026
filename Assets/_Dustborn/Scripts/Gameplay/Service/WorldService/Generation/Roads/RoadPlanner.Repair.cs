using System;
using UnityEngine;

public partial class RoadPlanner
{
    private Route RepairTilt(Route route, Endpoint source, Endpoint goal, float startStub, float endStub, float startHold, float endHold)
    {
        float limit = _config.HighwayMaxCrossSlope * CROSS_MARGIN;

        return Repair(route, source, goal, startStub, endStub, startHold, endHold, (point, direction) => CrossSlopeAt(point, direction) > limit,
            (point, direction) => Acceptable(point, direction, limit), repaired => repaired.Tilted);
    }

    private Route RepairFlood(Route route, Endpoint source, Endpoint goal, float startStub, float endStub, float startHold, float endHold)
    {
        float limit = _config.HighwayMaxCrossSlope * CROSS_MARGIN;

        float guard = REPAIR_END_GUARD * _config.HighwayMinCurveRadius;

        return Repair(route, source, goal, startStub, endStub, startHold + guard, endHold + guard, RiverFlooded,
            (point, direction) => Acceptable(point, direction, limit) && !RiverFlooded(point, direction) && !Standing(point, _map.SampleWorldSmooth(point.x, point.y)),
            repaired => repaired.Flooded);
    }

    private bool RiverFlooded(Vector2 point, Vector2 direction)
    {
        return AlongRiver(point, direction.normalized) || LooseRiverContact(point, direction.normalized);
    }

    private Route Repair(Route route, Endpoint source, Endpoint goal, float startStub, float endStub, float startHold, float endHold,
        Func<Vector2, Vector2, bool> bad, Func<Vector2, Vector2, bool> good, Func<Route, int> measure)
    {
        Vector2[] points = route.Points;
        int count = points.Length;

        if (count < 5)
            return route;

        float[] distance = Cumulative(points);
        float total = distance[^1];
        float radius = _config.HighwayMinCurveRadius;
        var field = new float[count];
        bool shifted = false;

        for (int i = 1; i < count - 1; i++)
        {
            Vector2 direction = points[i + 1] - points[i - 1];

            if (distance[i] < startHold || total - distance[i] < endHold || !bad(points[i], direction))
                continue;

            float shift = ShiftFor(points[i], direction, good);

            if (shift == 0f)
                continue;

            float reach = Mathf.Sqrt(radius * Mathf.Abs(shift) * Mathf.PI * Mathf.PI * 0.5f) * REPAIR_TAPER;

            if (distance[i] - reach < startHold || distance[i] + reach > total - endHold)
                continue;

            shifted = true;

            for (int j = 0; j < count; j++)
            {
                float gap = Mathf.Abs(distance[j] - distance[i]);

                if (gap >= reach)
                    continue;

                float value = shift * 0.5f * (1f + Mathf.Cos(Mathf.PI * gap / reach));

                if (Mathf.Abs(value) > Mathf.Abs(field[j]))
                    field[j] = value;
            }
        }

        if (!shifted)
            return route;

        var moved = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            Vector2 direction = points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];

            moved[i] = points[i] + new Vector2(-direction.y, direction.x).normalized * field[i];
        }

        moved = RoadSmoother.EnforceRadius(moved, radius, startHold, endHold, RADIUS_PASSES);
        moved = ResamplePieces(moved, _config.RoadPointSpacing, IndexAtDistance(moved, startStub), IndexAtDistance(moved, Length(moved) - endStub));
        moved[0] = points[0];
        moved[^1] = points[^1];

        Route repaired = Validate(ClampToWorld(moved), source, goal);

        return measure(repaired) < measure(route) && repaired.Hard <= route.Hard ? repaired : route;
    }

    private float ShiftFor(Vector2 point, Vector2 direction, Func<Vector2, Vector2, bool> good)
    {
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized;

        for (float shift = REPAIR_STEP; shift <= _config.RoadCorridor * REPAIR_REACH; shift += REPAIR_STEP)
        {
            if (good(point + normal * shift, direction))
                return shift;

            if (good(point - normal * shift, direction))
                return -shift;
        }

        return 0f;
    }

    private bool Acceptable(Vector2 point, Vector2 direction, float limit)
    {
        return !IsOutsideWorld(point) && !IsBlocked(point) && CrossSlopeAt(point, direction) <= limit;
    }
}
