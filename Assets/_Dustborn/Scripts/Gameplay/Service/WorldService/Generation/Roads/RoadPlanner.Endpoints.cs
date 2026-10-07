using UnityEngine;

public partial class RoadPlanner
{
    private Endpoint CorridorAt(int cell)
    {
        return new Endpoint { Direct = false, Edge = _corridorEdge[cell], Along = _corridorAlong[cell], Cost = 0f };
    }

    private float GatewayDistance(RoadEdge edge, float along)
    {
        if (_gatewayDistance == null || Mathf.Max(edge.From, edge.To) >= _gatewayDistance.Length)
            return float.MaxValue;

        return Mathf.Min(_gatewayDistance[edge.From] + along, _gatewayDistance[edge.To] + edge.Length - along);
    }

    private bool NearApproach(int cell)
    {
        Vector2 center = CellCenter(cell);
        float keep = _config.GatewayApproachLength + _config.HighwayMinCurveRadius;

        foreach ((Vector2 point, Vector2 _, Vector2 _) in _zones)
        {
            if ((center - point).sqrMagnitude < keep * keep)
                return true;
        }

        return false;
    }

    private bool NearApproachPoint(int cell)
    {
        Vector2 center = CellCenter(cell);
        float reach = _config.HighwayMinCurveRadius * APPROACH_TURN_RADII;

        foreach (Vector2 point in _approachPoints)
        {
            if ((center - point).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
    }

    private bool FitsApproach(int cell, int step)
    {
        return (_approachMask[cell] & (1 << step)) != 0;
    }

    private void BuildApproachMask()
    {
        foreach (int cell in _maskedCells)
            _approachMask[cell] = ALL_HEADINGS;

        _maskedCells.Clear();

        if (_zones.Count == 0 || !_strictApproach)
            return;

        float radius = _config.HighwayMinCurveRadius * APPROACH_ZONE;
        int span = Mathf.CeilToInt(radius / _cellSize) + 1;

        foreach ((Vector2 point, Vector2 outward, Vector2 travel) in _zones)
        {
            int column = Mathf.Clamp((int)(point.x / _cellSize), 0, _resolution - 1);
            int row = Mathf.Clamp((int)(point.y / _cellSize), 0, _resolution - 1);
            ushort refused = 0;

            for (int step = 0; step < HEADINGS; step++)
            {
                if (Vector2.Dot(_headings[step], travel) < APPROACH_ALIGNMENT)
                    refused |= (ushort)(1 << step);
            }

            for (int y = Mathf.Max(0, row - span); y <= Mathf.Min(_resolution - 1, row + span); y++)
            {
                for (int x = Mathf.Max(0, column - span); x <= Mathf.Min(_resolution - 1, column + span); x++)
                {
                    int cell = y * _resolution + x;
                    Vector2 center = CellCenter(cell);
                    Vector2 offset = center - point;

                    if (offset.sqrMagnitude > radius * radius || Vector2.Dot(offset, outward) < -_cellSize * 0.5f)
                        continue;

                    if (_approachMask[cell] == ALL_HEADINGS)
                        _maskedCells.Add(cell);

                    _approachMask[cell] &= (ushort)~refused;
                }
            }
        }
    }

    private void BuildEndpoints(SettlementGateway start, SettlementGateway end, float[] fromStart, float[] fromEnd, bool loop, float reach, bool relaxed)
    {
        _sources.Clear();
        _goals.Clear();
        _zones.Clear();
        _approachPoints.Clear();
        _strictApproach = !relaxed;
        _prepared = false;

        if (_graph.Degree(start.Node, RoadKind.Highway) == 0)
        {
            _sources.Add(new Endpoint { Direct = true, Gateway = start, Point = start.Approach, Tangent = start.Tangent });
            _zones.Add((start.Approach, start.Tangent, start.Tangent));
            _approachPoints.Add(start.Approach);
        }

        if (_graph.Degree(end.Node, RoadKind.Highway) == 0)
        {
            _goals.Add(new Endpoint { Direct = true, Gateway = end, Point = end.Approach, Tangent = -end.Tangent });
            _zones.Add((end.Approach, end.Tangent, -end.Tangent));
            _approachPoints.Add(end.Approach);
        }

        float stub = relaxed ? _config.GatewayApproachLength * 0.5f : _config.GatewayApproachLength;
        float keep = _config.GatewayApproachLength + _config.HighwayMinCurveRadius;
        float spacing = _cellSize * 0.5f;

        foreach (RoadEdge edge in _graph.Edges)
        {
            if (!edge.Alive || edge.Kind != RoadKind.Highway)
                continue;

            for (float along = spacing * 0.5f; along < edge.Length; along += spacing)
            {
                float toStart = Mathf.Min(fromStart[edge.From] + along, fromStart[edge.To] + edge.Length - along);
                float toEnd = Mathf.Min(fromEnd[edge.From] + along, fromEnd[edge.To] + edge.Length - along);
                float toGateway = GatewayDistance(edge, along);
                Vector2 point = edge.PointAt(along);

                if (toGateway < stub)
                    continue;

                if (!relaxed && IsBlocked(point))
                    continue;

                if (NearBridge(edge, along))
                    continue;

                if (toStart <= reach && (!loop || toStart < toEnd) && (point - end.Port).sqrMagnitude >= keep * keep)
                    _sources.Add(Corridor(edge, along, point, toStart));

                if (toEnd <= reach && (!loop || toEnd < toStart) && (point - start.Port).sqrMagnitude >= keep * keep)
                    _goals.Add(Corridor(edge, along, point, toEnd));
            }
        }
    }

    private bool NearBridge(RoadEdge edge, float along)
    {
        WaterMap water = _water;

        if (water == null || water.Rivers.Count == 0)
            return false;

        Vector2 before = edge.PointAt(Mathf.Max(0f, along - BRIDGE_JOIN_GUARD));
        Vector2 after = edge.PointAt(Mathf.Min(edge.Length, along + BRIDGE_JOIN_GUARD));

        return water.CrossesRiver(before, after, out float width, out _) && width >= _config.Water.BridgeMinWidth;
    }

    private Endpoint Corridor(RoadEdge edge, float along, Vector2 point, float network)
    {
        return new Endpoint
        {
            Direct = false,
            Edge = edge.Id,
            Along = along,
            Point = point,
            Tangent = edge.TangentAt(along),
            Cost = network + _config.JunctionPenalty
        };
    }

    private Vector2 TangentOf(int edge, float along)
    {
        int resolved = _graph.Resolve(edge, ref along);

        return _graph.Edges[resolved].TangentAt(along);
    }
}
