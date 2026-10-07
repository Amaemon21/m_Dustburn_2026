using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class RoadNetworkDiagnostics
{
    private static void MeasureRoads(List<Road> roads, RoadNetworkReport report)
    {
        var counts = new int[4];
        var lengths = new double[4];

        foreach (Road road in roads)
        {
            counts[(int)road.Kind]++;
            lengths[(int)road.Kind] += Length(road.Points);
        }

        foreach (RoadKind kind in new[] { RoadKind.Highway, RoadKind.Arterial, RoadKind.LocalStreet, RoadKind.DirtAccess })
        {
            report.Metric($"roads.{kind}.count", counts[(int)kind]);
            report.Metric($"roads.{kind}.km", lengths[(int)kind] / 1000.0);
        }
    }

    private static void CheckGeometry(WorldGenerationConfig config, List<Road> roads, SegmentGrid grid, RoadNetworkReport report)
    {
        var seen = new HashSet<(long, long)>();

        foreach (Road road in roads)
        {
            Vector2[] points = road.Points;

            if (points == null || points.Length < 2)
            {
                report.Violation(ZERO_SEGMENTS, Vector2.zero);
                continue;
            }

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 point = points[i];

                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                {
                    report.Violation(INVALID_COORDINATES, Vector2.zero);
                    continue;
                }

                if (point.x < 0f || point.y < 0f || point.x > config.WorldSize || point.y > config.WorldSize)
                    report.Violation(OUT_OF_BOUNDS, point);

                if (i == 0)
                    continue;

                if ((point - points[i - 1]).sqrMagnitude < 0.05f * 0.05f)
                {
                    report.Violation(ZERO_SEGMENTS, point);
                    continue;
                }

                long a = Quantise(points[i - 1]);
                long b = Quantise(point);

                if (!seen.Add(a < b ? (a, b) : (b, a)))
                    report.Violation(DUPLICATE_SEGMENTS, point);
            }
        }
    }

    private static void CheckCrossings(List<Road> roads, SegmentGrid grid, List<Vector2> nodes, int[] connected, RoadNetworkReport report)
    {
        var nodeGrid = new SegmentGrid();

        foreach (Vector2 node in nodes)
            nodeGrid.Add(0, 0, node, node);

        var candidates = new List<int>();
        var near = new List<int>();
        int explained = 0;

        for (int id = 0; id < grid.Segments.Count; id++)
        {
            (int road, int index, Vector2 a, Vector2 b) = grid.Segments[id];

            grid.Query(Vector2.Min(a, b) - Vector2.one * CONTACT, Vector2.Max(a, b) + Vector2.one * CONTACT, candidates);

            foreach (int other in candidates)
            {
                if (other <= id)
                    continue;

                (int otherRoad, int otherIndex, Vector2 c, Vector2 d) = grid.Segments[other];

                if (otherRoad == road)
                {
                    if (CrossesItself(roads[road].Points, index, otherIndex))
                        report.Violation(UNEXPLAINED_CROSSINGS, (a + b) * 0.5f);

                    continue;
                }

                if (SharedEnd(a, b, c, d, out Vector2 shared) && IsDeclared(nodeGrid, near, shared))
                {
                    explained++;
                    RoadGraph.Union(connected, road, otherRoad);
                    continue;
                }

                if (!Touch(a, b, c, d, out Vector2 point))
                    continue;

                if (IsDeclared(nodeGrid, near, point))
                {
                    explained++;
                    RoadGraph.Union(connected, road, otherRoad);
                    continue;
                }

                report.Violation(UNEXPLAINED_CROSSINGS, point);
            }
        }

        report.Metric("crossings.atDeclaredNodes", explained);
    }

    private static bool SharedEnd(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 shared)
    {
        foreach (Vector2 first in new[] { a, b })
        {
            foreach (Vector2 second in new[] { c, d })
            {
                if ((first - second).sqrMagnitude > CONTACT * CONTACT)
                    continue;

                shared = first;
                return true;
            }
        }

        shared = default;
        return false;
    }

    private static bool CrossesItself(Vector2[] points, int first, int second)
    {
        int gap = Math.Abs(first - second);

        if (gap <= SELF_INDEX_GAP)
            return false;

        if (gap == points.Length - 2 && (points[0] - points[^1]).sqrMagnitude < CONTACT * CONTACT)
            return false;

        Vector2 a = points[first];
        Vector2 r = points[first + 1] - a;
        Vector2 c = points[second];
        Vector2 s = points[second + 1] - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-8f)
            return false;

        Vector2 delta = c - a;
        float t = (delta.x * s.y - delta.y * s.x) / denominator;
        float u = (delta.x * r.y - delta.y * r.x) / denominator;

        return t > SELF_EPSILON && t < 1f - SELF_EPSILON && u > SELF_EPSILON && u < 1f - SELF_EPSILON;
    }

    private static bool IsDeclared(SegmentGrid nodeGrid, List<int> near, Vector2 point)
    {
        nodeGrid.Query(point - Vector2.one * NODE_WELD, point + Vector2.one * NODE_WELD, near);

        foreach (int id in near)
        {
            if ((nodeGrid.Segments[id].A - point).sqrMagnitude <= NODE_WELD * NODE_WELD)
                return true;
        }

        return false;
    }

    private static void CheckParallel(List<Road> roads, SegmentGrid grid, List<Vector2> nodes, RoadNetworkReport report)
    {
        var nodeGrid = new SegmentGrid();

        foreach (Vector2 node in nodes)
            nodeGrid.Add(0, 0, node, node);

        var candidates = new List<int>();
        var near = new List<int>();
        int corridors = 0;
        int flaggedSamples = 0;

        for (int road = 0; road < roads.Count; road++)
        {
            Road current = roads[road];
            Vector2[] points = RoadSmoother.Resample(current.Points, PARALLEL_STEP);
            float total = Length(points);
            float travelled = 0f;
            int run = 0;

            for (int i = 1; i < points.Length - 1; i++)
            {
                travelled += Vector2.Distance(points[i - 1], points[i]);

                if (travelled < NODE_CLEARANCE || total - travelled < NODE_CLEARANCE || NearNode(nodeGrid, near, points[i]))
                {
                    run = 0;
                    continue;
                }

                Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
                float reach = current.HalfWidth + PARALLEL_GAP + 8f;

                grid.Query(points[i] - Vector2.one * reach, points[i] + Vector2.one * reach, candidates);

                bool flagged = false;

                foreach (int id in candidates)
                {
                    (int otherRoad, int _, Vector2 a, Vector2 b) = grid.Segments[id];

                    if (otherRoad == road)
                        continue;

                    float half = current.HalfWidth + roads[otherRoad].HalfWidth;
                    float distanceSqr = DistanceSqr(points[i], a, b);
                    float low = half + 1.5f;
                    float high = half + PARALLEL_GAP;

                    if (distanceSqr < low * low || distanceSqr > high * high)
                        continue;

                    Vector2 line = b - a;

                    if (line.sqrMagnitude < 1e-4f || Mathf.Abs(Vector2.Dot(line.normalized, tangent)) < PARALLEL_DOT)
                        continue;

                    flagged = true;
                    break;
                }

                if (!flagged)
                {
                    run = 0;
                    continue;
                }

                flaggedSamples++;
                run++;

                if (run != PARALLEL_RUN)
                    continue;

                corridors++;
                report.Violation(PARALLEL_CORRIDORS, points[i]);
            }
        }

        report.Metric("parallel.corridorRuns", corridors);
        report.Metric("parallel.flaggedSamples", flaggedSamples);
    }

    private static bool NearNode(SegmentGrid nodeGrid, List<int> near, Vector2 point)
    {
        nodeGrid.Query(point - Vector2.one * NODE_CLEARANCE, point + Vector2.one * NODE_CLEARANCE, near);

        foreach (int id in near)
        {
            if ((nodeGrid.Segments[id].A - point).sqrMagnitude <= NODE_CLEARANCE * NODE_CLEARANCE)
                return true;
        }

        return false;
    }

    private static void CheckSettlementIntrusion(RoadNetwork network, IReadOnlyList<SettlementLayout> layouts, RoadNetworkReport report)
    {
        int samples = 0;

        foreach (Road road in network.Roads)
        {
            foreach (Vector2 point in Samples(road.Points, SETTLEMENT_STEP))
            {
                samples++;

                foreach (SettlementLayout layout in layouts)
                {
                    if (layout == null || !layout.Contains(point) || NearGateway(layout, point))
                        continue;

                    report.Violation(HIGHWAY_IN_SETTLEMENT, point);
                    break;
                }
            }
        }

        report.Metric("settlementIntrusion.samples", samples);
    }

    private static bool NearGateway(SettlementLayout layout, Vector2 point)
    {
        foreach (SettlementGateway gateway in layout.Gateways)
        {
            if ((gateway.Port - point).sqrMagnitude <= SETTLEMENT_STEP * SETTLEMENT_STEP)
                return true;
        }

        return false;
    }

    private static bool Touch(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
    {
        point = a;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) > 1e-6f)
        {
            Vector2 delta = c - a;
            float t = (delta.x * s.y - delta.y * s.x) / denominator;
            float u = (delta.x * r.y - delta.y * r.x) / denominator;
            float slackT = CONTACT / Mathf.Max(CONTACT, r.magnitude);
            float slackU = CONTACT / Mathf.Max(CONTACT, s.magnitude);

            if (t < -slackT || t > 1f + slackT || u < -slackU || u > 1f + slackU)
                return false;

            point = a + r * Mathf.Clamp01(t);
            return true;
        }

        foreach ((Vector2 end, Vector2 from, Vector2 to) in new[] { (a, c, d), (b, c, d), (c, a, b), (d, a, b) })
        {
            if (DistanceSqr(end, from, to) > CONTACT * CONTACT)
                continue;

            point = end;
            return true;
        }

        return false;
    }

    private static float DistanceSqr(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;
        float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr) : 0f;

        return (point - (from + line * t)).sqrMagnitude;
    }

    private static long Quantise(Vector2 point)
    {
        long x = Mathf.RoundToInt(point.x * 100f);
        long y = Mathf.RoundToInt(point.y * 100f);

        return (x << 32) ^ (y & 0xffffffffL);
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 1; i < points.Length; i++)
            total += Vector2.Distance(points[i - 1], points[i]);

        return total;
    }

    private static IEnumerable<Vector2> Samples(Vector2[] points, float step)
    {
        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / step));

            for (int k = 0; k < steps; k++)
                yield return Vector2.Lerp(points[i], points[i + 1], k / (float)steps);
        }

        yield return points[^1];
    }
}
