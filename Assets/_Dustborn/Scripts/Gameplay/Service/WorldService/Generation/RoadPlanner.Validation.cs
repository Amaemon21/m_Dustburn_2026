using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private Route Validate(Vector2[] points, Endpoint source, Endpoint goal)
    {
        var route = new Route { Points = points, Start = source, End = goal };
        float total = Length(points);
        float sinJunction = Mathf.Sin(_config.JunctionAngle * Mathf.Deg2Rad);
        float cosJunction = Mathf.Cos(_config.JunctionAngle * Mathf.Deg2Rad);
        float grace = Mathf.Max(END_GRACE, _config.HighwaySettlementClearance + 1f);
        float twin = _config.RoadHalfWidth * 2f + _config.RoadShoulder;
        int samples = 0;
        int wet = 0;
        float travelled = 0f;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 from = points[i];
            Vector2 to = points[i + 1];
            float length = Vector2.Distance(from, to);

            if (length <= Mathf.Epsilon)
                continue;

            Vector2 direction = (to - from) / length;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / SAMPLE_STEP));

            for (int step = 0; step < steps; step++)
            {
                float along = travelled + length * step / steps;
                Vector2 point = Vector2.Lerp(from, to, step / (float)steps);
                bool nearEnd = along < grace || total - along < grace;

                samples++;

                float ground = _map.SampleWorldSmooth(point.x, point.y);

                if (WaterMap.Wet(_config, _water, point.x, point.y, ground))
                    wet++;

                if (Standing(point, ground) || AlongRiver(point, direction) || LooseRiverContact(point, direction))
                {
                    route.Flooded++;
                    route.Problems.Add(point);
                }

                if (!nearEnd && IsInsideSettlement(point))
                {
                    route.Intrusions++;
                    route.Problems.Add(point);
                }

                if (IsTilted(point, direction))
                {
                    route.Tilted++;
                    route.Problems.Add(point);
                }

                if (nearEnd || !IsTwin(point, direction, twin, cosJunction))
                    continue;

                route.Parallel++;
                route.Problems.Add(point);
            }

            FindCrossings(route, from, to, travelled, direction, sinJunction, total);
            CheckRiverAngle(route, from, to, direction);
            travelled += length;
        }

        CountSteep(route, points, grace);
        CountTight(route, points);
        CountSelfContacts(route, points);

        route.Water = samples == 0 ? 0f : wet / (float)samples;
        route.Hard = route.Intrusions + route.Shallow + Mathf.Max(0, route.Parallel - 1) + (route.Steep > STEEP_ALLOWANCE ? route.Steep : 0)
            + route.Tilted + route.Tight + route.Self + route.Flooded + route.Oblique;

        if (total > LOOP_RATIO * Vector2.Distance(points[0], points[^1]) + LOOP_SLACK)
        {
            route.Hard += DEGENERATE_PENALTY;
            route.Problems.Add(points[points.Length / 2]);
        }

        return route;
    }

    private void CountSteep(Route route, Vector2[] points, float grace)
    {
        float cell = _map.WorldSize / (float)(_map.Resolution - 1);
        float spacing = cell * 0.5f;
        var dense = new List<Vector2>(points.Length * 8);
        var along = new List<float>(points.Length * 8);
        float travelled = 0f;

        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / spacing));

            for (int step = 0; step < steps; step++)
            {
                dense.Add(Vector2.Lerp(points[i], points[i + 1], step / (float)steps));
                along.Add(travelled + length * step / steps);
            }

            travelled += length;
        }

        dense.Add(points[^1]);
        along.Add(travelled);

        int count = dense.Count;
        int last = _map.Resolution - 1;
        var ground = new float[count];
        float[] distance = along.ToArray();

        for (int i = 0; i < count; i++)
            ground[i] = _map.Get(Mathf.Clamp(Mathf.RoundToInt(dense[i].x / cell), 0, last), Mathf.Clamp(Mathf.RoundToInt(dense[i].y / cell), 0, last));

        float maxFill = _config.MaxRoadFill / _map.MaxHeight;
        float maxCut = _config.MaxRoadCut / _map.MaxHeight;
        float[] profile = RoadProfile.Build(ground, maxFill, maxCut, _config.RoadProfileSmoothing);

        RoadProfile.Fit(profile, ground, distance, _config.RoadMaxGrade * CARVE_GRADE_MARGIN / _map.MaxHeight, maxFill, maxCut, null, RoadProfile.EARTHWORK_OVERRUN);

        float limit = _config.RoadMaxGrade / _map.MaxHeight;
        float tolerance = OVERRUN_TOLERANCE / _map.MaxHeight;
        float nextCheck = 0f;
        int back = 0;

        for (int k = 0; k < count; k++)
        {
            if (distance[k] < nextCheck)
                continue;

            nextCheck = distance[k] + SAMPLE_STEP;

            while (distance[k] - distance[back] > GRADE_PROBE)
                back++;

            float span = distance[k] - distance[back];

            if (distance[k] < grace || travelled - distance[k] < grace)
                continue;

            bool overrun = profile[k] - ground[k] > maxFill + tolerance || ground[k] - profile[k] > maxCut + tolerance;
            bool steep = span >= GRADE_PROBE * 0.5f && Mathf.Abs(profile[k] - profile[back]) / span > limit;

            if (!overrun && !steep)
                continue;

            route.Steep++;

            if (route.Steep % 3 == 1)
                route.Problems.Add(dense[k]);
        }
    }

    private bool IsTilted(Vector2 point, Vector2 direction)
    {
        return CrossSlopeAt(point, direction) > _config.HighwayMaxCrossSlope * CROSS_MARGIN;
    }

    private float CrossSlopeAt(Vector2 point, Vector2 direction)
    {
        if (direction.sqrMagnitude < 1e-8f)
            return 0f;

        float probe = _config.RoadHalfWidth + _config.RoadShoulder;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
        Vector2 left = point - normal * probe;
        Vector2 right = point + normal * probe;

        return Mathf.Abs(_map.SampleWorldSmooth(right.x, right.y) - _map.SampleWorldSmooth(left.x, left.y)) / (2f * probe);
    }

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

    private bool IsOutsideWorld(Vector2 point)
    {
        return point.x < 0f || point.y < 0f || point.x > _config.WorldSize || point.y > _config.WorldSize;
    }

    private static int IndexAtDistance(Vector2[] points, float target)
    {
        float travelled = 0f;
        float bestGap = Mathf.Abs(target);
        int best = 0;

        for (int i = 1; i < points.Length; i++)
        {
            travelled += Vector2.Distance(points[i - 1], points[i]);

            float gap = Mathf.Abs(travelled - target);

            if (gap >= bestGap)
                continue;

            bestGap = gap;
            best = i;
        }

        return best;
    }

    private void CountTight(Route route, Vector2[] points)
    {
        float spacing = RoadKindProfile.SampleSpacing(RoadKind.Highway);
        float limit = _config.HighwayMinCurveRadius * TIGHT_TOLERANCE;
        Vector2[] even = RoadSmoother.Resample(points, spacing);

        for (int i = 1; i < even.Length - 1; i++)
        {
            if (Vector2.Distance(even[i - 1], even[i]) < spacing * 0.5f || Vector2.Distance(even[i], even[i + 1]) < spacing * 0.5f)
                continue;

            if (RoadSmoother.Circumradius(even[i - 1], even[i], even[i + 1]) >= limit)
                continue;

            route.Tight++;
            route.Problems.Add(even[i]);
        }
    }

    private void CountSelfContacts(Route route, Vector2[] points)
    {
        if (points.Length < 4)
            return;

        float reach = _config.RoadHalfWidth * 2f + _config.RoadShoulder;
        float gap = Mathf.Max(SELF_GAP, reach * 4f);
        float[] distance = Cumulative(points);
        var cells = new Dictionary<long, List<int>>();

        for (int i = 0; i < points.Length - 1; i++)
        {
            foreach (long key in SelfCells(points[i], points[i + 1], reach))
            {
                if (!cells.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    cells[key] = list;
                }

                list.Add(i);
            }
        }

        for (int i = 0; i < points.Length - 1; i++)
        {
            if (!TouchesItself(points, distance, cells, i, reach, gap))
                continue;

            route.Self++;
            route.Problems.Add(points[i]);
        }
    }

    private static bool TouchesItself(Vector2[] points, float[] distance, Dictionary<long, List<int>> cells, int segment, float reach, float gap)
    {
        foreach (long key in SelfCells(points[segment], points[segment + 1], reach))
        {
            if (!cells.TryGetValue(key, out List<int> list))
                continue;

            foreach (int other in list)
            {
                if (other <= segment || distance[other] - distance[segment + 1] < gap)
                    continue;

                if (SegmentGapSqr(points[segment], points[segment + 1], points[other], points[other + 1]) <= reach * reach)
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<long> SelfCells(Vector2 from, Vector2 to, float reach)
    {
        int minX = Mathf.FloorToInt((Mathf.Min(from.x, to.x) - reach) / SEGMENT_CELL);
        int maxX = Mathf.FloorToInt((Mathf.Max(from.x, to.x) + reach) / SEGMENT_CELL);
        int minY = Mathf.FloorToInt((Mathf.Min(from.y, to.y) - reach) / SEGMENT_CELL);
        int maxY = Mathf.FloorToInt((Mathf.Max(from.y, to.y) + reach) / SEGMENT_CELL);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
                yield return ((long)y << 32) ^ (uint)x;
        }
    }

    private static float SegmentGapSqr(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (Intersect(a, b, c, d, out _, out _))
            return 0f;

        return Mathf.Min(Mathf.Min(PointSegmentSqr(a, c, d), PointSegmentSqr(b, c, d)), Mathf.Min(PointSegmentSqr(c, a, b), PointSegmentSqr(d, a, b)));
    }

    private static float PointSegmentSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;
        float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr) : 0f;

        return (point - (from + line * t)).sqrMagnitude;
    }

    private float[] BuildProfileField()
    {
        int size = _profileResolution;
        var ground = new float[size * size];

        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
                ground[y * size + x] = _map.SampleWorldSmooth(x * PROFILE_CELL, y * PROFILE_CELL);
        });

        float reach = Mathf.Clamp((_config.MaxRoadFill + _config.MaxRoadCut) / (2f * Mathf.Max(_config.RoadMaxGrade, 1e-3f)), MIN_CARVE_REACH, MAX_CARVE_REACH);
        int radius = Mathf.Max(1, Mathf.RoundToInt(reach * 0.5f / PROFILE_CELL));
        float[] field = ground;

        for (int pass = 0; pass < PROFILE_BLUR_PASSES; pass++)
            field = BoxBlur(field, size, radius);

        for (int i = 0; i < field.Length; i++)
            field[i] = Mathf.Clamp(field[i], ground[i] - _config.MaxRoadCut, ground[i] + _config.MaxRoadFill);

        return field;
    }

    private float Smoothed(float x, float y)
    {
        int last = _profileResolution - 1;
        float u = Mathf.Clamp(x / PROFILE_CELL, 0f, last);
        float v = Mathf.Clamp(y / PROFILE_CELL, 0f, last);
        int x0 = Mathf.Min((int)u, last - 1);
        int y0 = Mathf.Min((int)v, last - 1);
        int row = y0 * _profileResolution;
        int next = row + _profileResolution;
        float bottom = Mathf.Lerp(_profile[row + x0], _profile[row + x0 + 1], u - x0);
        float top = Mathf.Lerp(_profile[next + x0], _profile[next + x0 + 1], u - x0);

        return Mathf.Lerp(bottom, top, v - y0);
    }

    private float StepGrade(int fromX, int fromY, int toX, int toY, float distance)
    {
        int pieces = Mathf.Max(2, Mathf.CeilToInt(distance / GRADE_PROBE));
        float startX = (fromX + 0.5f) * _cellSize;
        float startY = (fromY + 0.5f) * _cellSize;
        float endX = (toX + 0.5f) * _cellSize;
        float endY = (toY + 0.5f) * _cellSize;
        float previous = Smoothed(startX, startY);
        float steepest = 0f;

        for (int k = 1; k <= pieces; k++)
        {
            float t = k / (float)pieces;
            float height = Smoothed(startX + (endX - startX) * t, startY + (endY - startY) * t);

            steepest = Mathf.Max(steepest, Mathf.Abs(height - previous) * pieces / distance);
            previous = height;
        }

        return steepest;
    }

    private bool IsInsideSettlement(Vector2 point)
    {
        return _layouts != null && _blocked[CellOf(point)] && InsideAny(point);
    }

    private bool InsideAny(Vector2 point)
    {
        float clearance = _config.HighwaySettlementClearance * 0.5f;

        foreach (SettlementLayout layout in _layouts)
        {
            if (layout != null && layout.IsWithin(point, clearance))
                return true;
        }

        return false;
    }

    private bool IsTwin(Vector2 point, Vector2 direction, float distance, float cosJunction)
    {
        int cell = CellOf(point);
        int edge = _corridorEdge[cell] >= 0 ? _corridorEdge[cell] : _bandEdge[cell];

        if (edge < 0)
            return false;

        float along = _corridorEdge[cell] >= 0 ? _corridorAlong[cell] : _bandAlong[cell];
        int resolved = _graph.Resolve(edge, ref along);
        RoadEdge candidate = _graph.Edges[resolved];
        float projected = candidate.Project(point, out Vector2 nearest);

        if ((nearest - point).sqrMagnitude > distance * distance)
            return false;

        return Mathf.Abs(Vector2.Dot(candidate.TangentAt(projected), direction)) > cosJunction;
    }

    private void FindCrossings(Route route, Vector2 from, Vector2 to, float travelled, Vector2 direction, float sinJunction, float total)
    {
        var seen = new HashSet<long>();

        foreach (long entry in SegmentsNear(from, to))
        {
            if (!seen.Add(entry))
                continue;

            int edgeId = (int)(entry >> 20);
            int segment = (int)(entry & 0xfffff);
            RoadEdge edge = _graph.Edges[edgeId];

            if (!edge.Alive || segment >= edge.Points.Length - 1)
                continue;

            Vector2 a = edge.Points[segment];
            Vector2 b = edge.Points[segment + 1];

            if (!Intersect(from, to, a, b, out float t, out float u))
                continue;

            Vector2 point = Vector2.Lerp(from, to, t);
            float along = travelled + Vector2.Distance(from, to) * t;

            if (IsOwnEnd(route.Start, point) || IsOwnEnd(route.End, point) || along < JUNCTION_WELD || total - along < JUNCTION_WELD)
                continue;

            Vector2 other = (b - a).normalized;
            float edgeAlong = edge.Distance[segment] + Vector2.Distance(a, b) * u;

            float approach = _config.GatewayApproachLength + JUNCTION_WELD;
            bool nearOwnGateway = (IsGatewayEnd(route.Start) && along < approach) || (IsGatewayEnd(route.End) && total - along < approach);

            if (nearOwnGateway || Mathf.Abs(Cross(direction, other)) < sinJunction || GatewayDistance(edge, edgeAlong) < approach)
            {
                route.Shallow++;
                route.Problems.Add(point);
            }

            route.Crossings.Add((along, edge.Id, edgeAlong, point));
        }
    }

    private static bool IsGatewayEnd(Endpoint endpoint)
    {
        return endpoint.Direct && endpoint.Gateway != null;
    }

    private static bool IsOwnEnd(Endpoint endpoint, Vector2 point)
    {
        return !endpoint.Direct && (endpoint.Point - point).sqrMagnitude < JUNCTION_WELD * JUNCTION_WELD;
    }
}
