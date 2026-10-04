using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private Route Build(List<int> states, Endpoint source, Endpoint goal)
    {
        Vector2 startPoint = ExactPoint(source, states[0] / STATES);
        Vector2 endPoint = ExactPoint(goal, states[^1] / STATES);
        bool startsAtGateway = source.Direct && source.Gateway != null;
        bool endsAtGateway = goal.Direct && goal.Gateway != null;

        Vector2 startOrigin = startsAtGateway ? source.Gateway.Port : startPoint;
        Vector2 endOrigin = endsAtGateway ? goal.Gateway.Port : endPoint;
        Vector2 startDirection = startsAtGateway ? source.Gateway.Tangent : StepDirection(states, 1, startPoint, endPoint);
        Vector2 endDirection = endsAtGateway ? -goal.Gateway.Tangent : StepDirection(states, states.Count - 1, startPoint, endPoint);
        float startStub = startsAtGateway ? _config.GatewayApproachLength : BRANCH_LENGTH;
        float endStub = endsAtGateway ? _config.GatewayApproachLength : BRANCH_LENGTH;
        float startHold = startsAtGateway ? startStub + STUB_HOLD : BRANCH_HOLD;
        float endHold = endsAtGateway ? endStub + STUB_HOLD : BRANCH_HOLD;

        Vector2[] points;
        Vector2[] interior = null;
        Vector2 startTip = startOrigin + startDirection * startStub;
        Vector2 endTip = endOrigin - endDirection * endStub;
        bool degenerate = (endTip - startTip).sqrMagnitude < _cellSize * _cellSize;
        float reach = 0f;
        List<Vector2> curve = _forceFacing && startsAtGateway && endsAtGateway ? FacingCurve(source.Gateway, goal.Gateway, out reach) : null;
        bool facing = curve != null;

        if (facing)
        {
            points = Facing(startOrigin, startDirection, endOrigin, goal.Gateway.Tangent, reach, curve);
        }
        else if (degenerate)
        {
            points = Straight(startOrigin, startsAtGateway ? startDirection * startStub : Vector2.zero,
                endOrigin, endsAtGateway ? endDirection * endStub : Vector2.zero);
        }
        else
        {
            var cells = new List<Vector2>(states.Count + 2) { startOrigin + startDirection * startStub };

            for (int i = 1; i < states.Count - 1; i++)
                cells.Add(CellCenter(states[i] / STATES));

            cells.Add(endOrigin - endDirection * endStub);
            interior = cells.ToArray();
            points = Shape((Vector2[])interior.Clone(), startOrigin, startDirection, startHold, endOrigin, endDirection, endHold);
        }

        Route route = Validate(points, source, goal);

        if (interior != null && route.Tight > 0)
        {
            _connectCorridor = _config.RoadCorridor * CONNECT_WIDENING;

            Route wider = Validate(Shape((Vector2[])interior.Clone(), startOrigin, startDirection, startHold, endOrigin, endDirection, endHold), source, goal);

            _connectCorridor = _config.RoadCorridor;

            if (wider.Hard < route.Hard)
                route = wider;
        }

        for (int round = 0; round < REPAIR_ROUNDS && !facing && !degenerate && route.Tilted > 0; round++)
        {
            Route repaired = RepairTilt(route, source, goal, startStub, endStub, startHold, endHold);

            if (repaired == route)
                break;

            route = repaired;
        }

        for (int round = 0; round < REPAIR_ROUNDS && !facing && !degenerate && route.Flooded > 0; round++)
        {
            Route repaired = RepairFlood(route, source, goal, startStub, endStub, startHold, endHold);

            if (repaired == route)
                break;

            route = repaired;
        }

        route.Shape = facing ? TightFacing(source.Gateway, goal.Gateway) ? PieceShape.Facing : PieceShape.Direct : degenerate ? PieceShape.Straight : PieceShape.Routed;

        if (degenerate && !facing && (startsAtGateway || endsAtGateway))
        {
            route.Hard += DEGENERATE_PENALTY;
            route.Problems.Add((startOrigin + endOrigin) * 0.5f);
        }

        route.Cost = source.Cost + Length(route.Points) + (goal.Direct ? 0f : goal.Cost);

        return route;
    }

    private Vector2 ExactPoint(Endpoint endpoint, int cell)
    {
        if (endpoint.Direct)
            return endpoint.Point;

        float along = endpoint.Along;
        int edge = _graph.Resolve(endpoint.Edge, ref along);
        float projected = _graph.Edges[edge].Project(CellCenter(cell), out Vector2 nearest);

        endpoint.Edge = edge;
        endpoint.Along = projected;
        endpoint.Point = nearest;
        endpoint.Tangent = _graph.Edges[edge].TangentAt(projected);

        return nearest;
    }

    private bool TightFacing(SettlementGateway start, SettlementGateway end)
    {
        if (Vector2.Dot(start.Tangent, end.Tangent) > -FACING_DOT)
            return false;

        RoadSmoother.FacingSpan(start.Port, start.Tangent, end.Port, end.Tangent, _config.HighwayMinCurveRadius, out float along, out float gap);

        return along > 0f && along < 2f * _config.GatewayApproachLength + gap + FACING_SLACK_RADII * _config.HighwayMinCurveRadius;
    }

    private List<Vector2> FacingCurve(SettlementGateway start, SettlementGateway end, out float reach)
    {
        reach = _config.GatewayApproachLength;

        if (TightFacing(start, end))
        {
            reach = RoadSmoother.FacingReach(start.Port, start.Tangent, end.Port, end.Tangent, _config.GatewayApproachLength,
                _config.HighwayMinCurveRadius, out List<Vector2> facing);

            return facing;
        }

        float distance = Vector2.Distance(start.Approach, end.Approach);

        if (distance > _config.HighwayMinCurveRadius * DIRECT_RADII)
            return null;

        List<Vector2> curve = RoadSmoother.Dubins(start.Approach, start.Tangent, end.Approach, -end.Tangent,
            _config.HighwayMinCurveRadius * RoadSmoother.ARC_RADIUS_SCALE, RoadSmoother.ARC_STEP, out float length, out float turning);

        return curve == null || turning > DIRECT_MAX_TURN_DEGREES * Mathf.Deg2Rad || length > distance * DIRECT_DETOUR ? null : curve;
    }

    private Vector2[] Facing(Vector2 start, Vector2 startTangent, Vector2 end, Vector2 endTangent, float reach, List<Vector2> curve)
    {
        float spacing = _config.RoadPointSpacing;
        Vector2 from = start + startTangent * reach;
        Vector2 to = end + endTangent * reach;
        Vector2[] head = Piece(start, from, spacing);
        Vector2[] middle = RoadSmoother.ResampleEven(curve.ToArray(), spacing);
        Vector2[] tail = Piece(to, end, spacing);
        var combined = new List<Vector2>(head.Length + middle.Length + tail.Length);

        combined.AddRange(head);

        for (int i = 1; i < middle.Length; i++)
            combined.Add(middle[i]);

        int tailStart = combined.Count - 1;

        for (int i = 1; i < tail.Length; i++)
            combined.Add(tail[i]);

        float hold = reach + STUB_HOLD;
        Vector2[] points = RoadSmoother.EnforceRadius(combined.ToArray(), _config.HighwayMinCurveRadius, hold, hold, RADIUS_PASSES);

        points = ResamplePieces(points, spacing, head.Length - 1, tailStart);
        points[0] = start;
        points[^1] = end;

        return ClampToWorld(points);
    }

    private Vector2[] Straight(Vector2 start, Vector2 startReach, Vector2 end, Vector2 endReach)
    {
        var anchors = new List<Vector2> { start };
        Vector2 afterStart = start + startReach;
        Vector2 beforeEnd = end - endReach;

        if (startReach.sqrMagnitude > 0f && Vector2.Dot(end - afterStart, startReach) > 0f)
            anchors.Add(afterStart);

        if (endReach.sqrMagnitude > 0f && Vector2.Dot(beforeEnd - anchors[^1], endReach) > 0f)
            anchors.Add(beforeEnd);

        anchors.Add(end);

        var breaks = new int[Mathf.Max(0, anchors.Count - 2)];

        for (int i = 0; i < breaks.Length; i++)
            breaks[i] = i + 1;

        return ClampToWorld(ResamplePieces(anchors.ToArray(), _config.RoadPointSpacing, breaks));
    }

    private Vector2 StepDirection(List<int> states, int index, Vector2 start, Vector2 end)
    {
        if (index <= 0 || index >= states.Count || states[index] % STATES == FREE_HEADING)
            return (end - start).sqrMagnitude > 1e-4f ? (end - start).normalized : new Vector2(1f, 0f);

        return _headings[states[index] % STATES];
    }

    private Vector2[] Shape(Vector2[] interior, Vector2 startOrigin, Vector2 startDirection, float startHold, Vector2 endOrigin, Vector2 endDirection, float endHold)
    {
        Vector2[] middle = interior.Length < 3 ? interior : _smoother.Smooth(interior);
        float total = Length(middle);
        float blend = Mathf.Min(BLEND_RADII * _config.HighwayMinCurveRadius, total * BLEND_SHARE);
        float connect = Mathf.Min(CONNECT_RADII * _config.HighwayMinCurveRadius, total * CONNECT_SHARE);

        middle = Attach(middle, startDirection, connect) ?? Blend(middle, startDirection, blend);
        Array.Reverse(middle);
        middle = Attach(middle, -endDirection, connect) ?? Blend(middle, -endDirection, blend);
        Array.Reverse(middle);

        float spacing = _config.RoadPointSpacing;
        Vector2[] head = Piece(startOrigin, middle[0], spacing);
        Vector2[] tail = Piece(middle[^1], endOrigin, spacing);
        var combined = new List<Vector2>(middle.Length + head.Length + tail.Length);

        combined.AddRange(head);

        for (int i = 1; i < middle.Length - 1; i++)
            combined.Add(middle[i]);

        int tailStart = combined.Count;

        combined.AddRange(tail);

        Vector2[] points = RoadSmoother.EnforceRadius(combined.ToArray(), _config.HighwayMinCurveRadius, startHold, endHold, RADIUS_PASSES);

        points = ResamplePieces(points, spacing, head.Length - 1, tailStart);
        points[0] = startOrigin;
        points[^1] = endOrigin;

        return ClampToWorld(points);
    }

    private static Vector2[] Blend(Vector2[] points, Vector2 tangent, float length)
    {
        if (points.Length < 3 || length <= 0f || tangent.sqrMagnitude < 1e-6f)
            return points;

        var result = (Vector2[])points.Clone();
        Vector2 anchor = points[0];
        Vector2 direction = tangent.normalized;
        float travelled = 0f;

        for (int i = 1; i < points.Length; i++)
        {
            travelled += Vector2.Distance(points[i - 1], points[i]);

            if (travelled >= length)
                break;

            float u = travelled / length;
            float weight = 1f - u * u * (3f - 2f * u);

            result[i] = Vector2.Lerp(points[i], anchor + direction * travelled, weight);
        }

        return result;
    }

    private Vector2[] Attach(Vector2[] points, Vector2 heading, float limit)
    {
        if (points.Length < 3 || heading.sqrMagnitude < 1e-6f || limit <= 0f)
            return null;

        float radius = _config.HighwayMinCurveRadius * RoadSmoother.ARC_RADIUS_SCALE;
        float maxTurn = CONNECT_MAX_TURN_DEGREES * Mathf.Deg2Rad;
        float corridorSqr = _connectCorridor * _connectCorridor;
        Vector2 direction = heading.normalized;
        float travelled = 0f;

        for (int k = 1; k < points.Length - 1; k++)
        {
            travelled += Vector2.Distance(points[k - 1], points[k]);

            if (travelled > limit)
                break;

            Vector2 tangent = points[k + 1] - points[k];

            if (tangent.sqrMagnitude < 1e-6f)
                continue;

            List<Vector2> path = RoadSmoother.Dubins(points[0], direction, points[k], tangent, radius, RoadSmoother.ARC_STEP, out float length, out float turning);

            if (path == null || turning > maxTurn || length > travelled * CONNECT_DETOUR + _cellSize || DeviationSqr(path, points, k) > corridorSqr)
                continue;

            Vector2[] connector = RoadSmoother.ResampleEven(path.ToArray(), _config.RoadPointSpacing);
            var result = new List<Vector2>(connector.Length + points.Length - k);

            result.AddRange(connector);

            for (int i = k + 1; i < points.Length; i++)
                result.Add(points[i]);

            return result.ToArray();
        }

        return null;
    }

    private static float DeviationSqr(List<Vector2> path, Vector2[] points, int last)
    {
        float worst = 0f;

        foreach (Vector2 point in path)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < last; i++)
                nearest = Mathf.Min(nearest, PointSegmentSqr(point, points[i], points[i + 1]));

            worst = Mathf.Max(worst, nearest);
        }

        return worst;
    }

    private static Vector2[] Piece(Vector2 from, Vector2 to, float spacing)
    {
        return (to - from).sqrMagnitude < MIN_PIECE * MIN_PIECE ? new[] { from } : RoadSmoother.ResampleEven(new[] { from, to }, spacing);
    }

    private static Vector2[] ResamplePieces(Vector2[] points, float spacing, params int[] breaks)
    {
        var result = new List<Vector2>(points.Length);
        int from = 0;

        for (int k = 0; k <= breaks.Length; k++)
        {
            int to = k < breaks.Length ? breaks[k] : points.Length - 1;

            if (to <= from)
                continue;

            var part = new Vector2[to - from + 1];
            Array.Copy(points, from, part, 0, part.Length);

            Vector2[] piece = RoadSmoother.ResampleEven(part, spacing);

            for (int i = result.Count == 0 ? 0 : 1; i < piece.Length; i++)
                result.Add(piece[i]);

            from = to;
        }

        if (result.Count == 0)
            result.AddRange(points);

        return result.ToArray();
    }

    private Vector2[] ClampToWorld(Vector2[] points)
    {
        float limit = _config.WorldSize - 1f;

        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector2(Mathf.Clamp(points[i].x, 1f, limit), Mathf.Clamp(points[i].y, 1f, limit));

        return points;
    }
}
