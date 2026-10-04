using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private Journey Assemble(List<int> states, Endpoint source, Endpoint goal)
    {
        var journey = new Journey();
        Endpoint start = source;
        int from = 0;
        float ridden = 0f;

        foreach ((int entry, int exit) in RideRuns(states))
        {
            AddPiece(journey, states, from, entry, start, CorridorAt(states[entry] / STATES));
            start = CorridorAt(states[exit] / STATES);
            ridden += (exit - entry) * _cellSize;
            from = exit;
            journey.Rides++;
        }

        AddPiece(journey, states, from, states.Count - 1, start, goal);

        float length = ridden;

        foreach (Route piece in journey.Pieces)
        {
            length += Length(piece.Points);
            journey.Hard += piece.Hard;
            journey.Self += piece.Self;
            journey.Water = Mathf.Max(journey.Water, piece.Water);
            journey.Problems.AddRange(piece.Problems);
        }

        journey.Cost = source.Cost + length + (goal.Direct ? 0f : goal.Cost);

        return journey;
    }

    private void AddPiece(Journey journey, List<int> states, int from, int to, Endpoint start, Endpoint end)
    {
        if (!start.Direct && !end.Direct && from >= to)
            return;

        journey.Pieces.Add(Build(states.GetRange(from, Mathf.Max(1, to - from + 1)), start, end));
    }

    private List<(int Entry, int Exit)> RideRuns(List<int> states)
    {
        var runs = new List<(int Entry, int Exit)>();
        int entry = -1;

        for (int i = 1; i <= states.Count; i++)
        {
            bool ride = i < states.Count && states[i] % STATES != FREE_HEADING
                && IsRide(states[i - 1] / STATES, states[i] / STATES, states[i] % STATES);

            if (ride)
            {
                if (entry < 0)
                    entry = i - 1;

                continue;
            }

            if (entry >= 0 && i - 1 - entry >= MIN_RIDE_STEPS)
                runs.Add((entry, i - 1));

            entry = -1;
        }

        return runs;
    }

    private bool IsRide(int fromCell, int toCell, int step)
    {
        int fromEdge = _corridorEdge[fromCell];
        int toEdge = _corridorEdge[toCell];

        if (fromEdge < 0 || toEdge < 0 || _graph == null || NearApproach(toCell))
            return false;

        float fromAlong = _corridorAlong[fromCell];
        float toAlong = _corridorAlong[toCell];
        RoadEdge previous = _graph.Edges[_graph.Resolve(fromEdge, ref fromAlong)];
        RoadEdge next = _graph.Edges[_graph.Resolve(toEdge, ref toAlong)];

        if (previous.Route != next.Route || GatewayDistance(next, toAlong) < _config.GatewayApproachLength + _cellSize)
            return false;

        return Mathf.Abs(Vector2.Dot(_headings[step], next.TangentAt(toAlong))) >= RIDE_ALIGNMENT;
    }

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
        if (_zones.Count == 0 || !_strictApproach)
            return true;

        Vector2 center = CellCenter(cell);
        float radius = _config.HighwayMinCurveRadius * APPROACH_ZONE;

        foreach ((Vector2 point, Vector2 outward, Vector2 travel) in _zones)
        {
            Vector2 offset = center - point;

            if (offset.sqrMagnitude > radius * radius || Vector2.Dot(offset, outward) < -_cellSize * 0.5f)
                continue;

            if (Vector2.Dot(_headings[step], travel) < APPROACH_ALIGNMENT)
                return false;
        }

        return true;
    }

    private void BuildEndpoints(SettlementGateway start, SettlementGateway end, float[] fromStart, float[] fromEnd, bool loop, float reach, bool relaxed)
    {
        _sources.Clear();
        _goals.Clear();
        _zones.Clear();
        _approachPoints.Clear();
        _strictApproach = !relaxed;

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

    private List<int> Search(out Endpoint source, out Endpoint goal)
    {
        source = null;
        goal = null;

        Array.Fill(_cost, float.MaxValue);
        Array.Fill(_previous, -1);
        Array.Clear(_closed, 0, _closed.Length);
        Array.Fill(_goalCost, float.MaxValue);
        Array.Fill(_goalIndex, -1);
        Array.Fill(_sourceIndex, -1);

        for (int i = 0; i < _goals.Count; i++)
        {
            int cell = CellOf(_goals[i].Point);

            if (_goals[i].Cost < _goalCost[cell])
            {
                _goalCost[cell] = _goals[i].Cost;
                _goalIndex[cell] = i;
            }
        }

        BuildHeuristic();
        _open.Clear();

        for (int i = 0; i < _sources.Count; i++)
        {
            Endpoint endpoint = _sources[i];
            int cell = CellOf(endpoint.Point);
            int heading = endpoint.Direct && endpoint.Tangent.sqrMagnitude > 0f ? HeadingOf(endpoint.Tangent) : FREE_HEADING;
            int state = cell * STATES + heading;

            if (endpoint.Cost >= _cost[state])
                continue;

            _cost[state] = endpoint.Cost;
            _sourceIndex[cell] = i;
            _open.Push(state, endpoint.Cost + _heuristic[cell]);
        }

        float bestTotal = float.MaxValue;
        int bestState = -1;
        int bestGoal = -1;
        float sinJunction = Mathf.Sin(_config.JunctionAngle * Mathf.Deg2Rad);

        while (_open.TryPop(out int current))
        {
            if (_closed[current])
                continue;

            _closed[current] = true;

            int cell = current / STATES;

            if (_cost[current] + _heuristic[cell] >= bestTotal)
                break;

            int heading = current % STATES;
            int goalIndex = _goalIndex[cell];

            if (goalIndex >= 0 && _previous[current] >= 0)
            {
                float terminal = Terminal(_goals[goalIndex], heading, sinJunction);
                float total = _cost[current] + terminal;

                if (total < bestTotal)
                {
                    bestTotal = total;
                    bestState = current;
                    bestGoal = goalIndex;
                }
            }

            int x = cell % _resolution;
            int y = cell / _resolution;
            int sourceIndex = _previous[current] < 0 ? _sourceIndex[cell] : -1;

            for (int step = 0; step < HEADINGS; step++)
            {
                int nextX = x + STEP_X[step];
                int nextY = y + STEP_Y[step];

                if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                    continue;

                int nextCell = nextY * _resolution + nextX;
                int next = nextCell * STATES + step;

                if (_closed[next])
                    continue;

                if (_blocked[nextCell] && _goalIndex[nextCell] < 0)
                    continue;

                if (!FitsApproach(nextCell, step))
                    continue;

                float turn = _turnRadius[heading * HEADINGS + step];

                if (turn < _minTurnRadius || (turn < _strictTurnRadius && NearApproachPoint(nextCell)))
                    continue;

                int index = cell * HEADINGS + step;
                bool ride = IsRide(cell, nextCell, step);

                if (!ride && (_grade[index] > _hardGrade || _cross[index] > _hardCross))
                    continue;

                if (sourceIndex >= 0 && !_sources[sourceIndex].Direct && Mathf.Abs(Cross(_headings[step], _sources[sourceIndex].Tangent)) < sinJunction)
                    continue;

                if (_exactApproach && sourceIndex >= 0 && _sources[sourceIndex].Gateway != null && heading != FREE_HEADING && step != heading)
                    continue;

                float stepCost = ride
                    ? _stepLength[step] * _config.RoadReuseDiscount * _penalty[nextCell]
                    : CorridorCost(nextCell, step, sinJunction, _stepCost[index] * _penalty[nextCell], sourceIndex >= 0);
                float candidate = _cost[current] + stepCost + _turnCost[heading * HEADINGS + step];

                if (candidate >= _cost[next])
                    continue;

                _cost[next] = candidate;
                _previous[next] = current;
                _open.Push(next, candidate + _heuristic[nextCell]);
            }
        }

        if (bestState < 0)
            return null;

        var path = new List<int>();

        for (int state = bestState; state >= 0; state = _previous[state])
            path.Add(state);

        path.Reverse();

        int first = path[0] / STATES;

        source = _sources[Mathf.Max(0, _sourceIndex[first])];
        goal = _goals[bestGoal];

        return path;
    }

    private float Terminal(Endpoint goal, int heading, float sinJunction)
    {
        if (heading == FREE_HEADING)
            return goal.Direct ? 0f : float.MaxValue;

        if (goal.Direct)
        {
            if (goal.Tangent.sqrMagnitude <= 0f)
                return goal.Cost;

            int arrival = HeadingOf(goal.Tangent);

            if (_exactApproach && goal.Gateway != null && heading != arrival)
                return float.MaxValue;

            float turn = _turnRadius[heading * HEADINGS + arrival];

            return turn < _minTurnRadius || (goal.Gateway != null && turn < _strictTurnRadius) ? float.MaxValue : goal.Cost + TurnCost(heading, arrival);
        }

        return Mathf.Abs(Cross(_headings[heading], goal.Tangent)) < sinJunction ? float.MaxValue : goal.Cost;
    }

    private float CorridorCost(int cell, int step, float sinJunction, float cost, bool leavingSource)
    {
        if (_goalIndex[cell] >= 0 || leavingSource)
            return cost;

        int edge = _corridorEdge[cell];

        if (edge >= 0)
        {
            Vector2 tangent = TangentOf(edge, _corridorAlong[cell]);

            return Mathf.Abs(Cross(_headings[step], tangent)) < sinJunction ? cost * _config.ParallelPenalty : cost + _config.JunctionPenalty;
        }

        edge = _bandEdge[cell];

        if (edge < 0)
            return cost;

        Vector2 near = TangentOf(edge, _bandAlong[cell]);

        return Mathf.Abs(Cross(_headings[step], near)) < sinJunction ? cost * _config.ParallelPenalty * 0.5f : cost;
    }

    private Vector2 TangentOf(int edge, float along)
    {
        int resolved = _graph.Resolve(edge, ref along);

        return _graph.Edges[resolved].TangentAt(along);
    }

    private void BuildHeuristic()
    {
        Array.Fill(_heuristic, float.MaxValue * 0.25f);

        for (int cell = 0; cell < _goalCost.Length; cell++)
        {
            if (_goalIndex[cell] >= 0)
                _heuristic[cell] = 0f;
        }

        float straight = 1f;
        float diagonal = 1.41421356f;

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                int i = y * _resolution + x;
                float value = _heuristic[i];

                if (x > 0)
                    value = Mathf.Min(value, _heuristic[i - 1] + straight);

                if (y > 0)
                {
                    value = Mathf.Min(value, _heuristic[i - _resolution] + straight);

                    if (x > 0)
                        value = Mathf.Min(value, _heuristic[i - _resolution - 1] + diagonal);

                    if (x < _resolution - 1)
                        value = Mathf.Min(value, _heuristic[i - _resolution + 1] + diagonal);
                }

                _heuristic[i] = value;
            }
        }

        for (int y = _resolution - 1; y >= 0; y--)
        {
            for (int x = _resolution - 1; x >= 0; x--)
            {
                int i = y * _resolution + x;
                float value = _heuristic[i];

                if (x < _resolution - 1)
                    value = Mathf.Min(value, _heuristic[i + 1] + straight);

                if (y < _resolution - 1)
                {
                    value = Mathf.Min(value, _heuristic[i + _resolution] + straight);

                    if (x < _resolution - 1)
                        value = Mathf.Min(value, _heuristic[i + _resolution + 1] + diagonal);

                    if (x > 0)
                        value = Mathf.Min(value, _heuristic[i + _resolution - 1] + diagonal);
                }

                _heuristic[i] = value;
            }
        }

        float scale = _cellSize * CHAMFER_SCALE;

        for (int i = 0; i < _heuristic.Length; i++)
            _heuristic[i] *= scale;
    }
}
