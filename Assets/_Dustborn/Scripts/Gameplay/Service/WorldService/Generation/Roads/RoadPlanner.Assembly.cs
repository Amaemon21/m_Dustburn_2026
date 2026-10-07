using System.Collections.Generic;
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
}
