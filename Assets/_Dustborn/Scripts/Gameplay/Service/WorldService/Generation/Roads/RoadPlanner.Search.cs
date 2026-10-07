using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public partial class RoadPlanner
{
    private List<int> Search(SearchPlan plan, out Endpoint source, out Endpoint goal)
    {
        source = null;
        goal = null;

        PrepareSearch();
        _search.Penalty.CopyFrom(_penalty);
        _search.Blocked.CopyFrom(_blocked);

        var job = new SearchJob
        {
            StepCost = _search.StepCost,
            Grade = _search.Grade,
            Cross = _search.Cross,
            TurnCost = _search.TurnCost,
            TurnRadius = _search.TurnRadius,
            StepLength = _search.StepLength,
            Headings = _search.Headings,
            StepOffset = _search.StepOffset,
            StepX = _search.StepX,
            StepY = _search.StepY,
            Penalty = _search.Penalty,
            Blocked = _search.Blocked,
            Heuristic = _search.Heuristic,
            GoalIndex = _search.GoalIndex,
            ApproachMask = _search.ApproachMask,
            RideMask = _search.RideMask,
            NearPoint = _search.NearPoint,
            CorridorKind = _search.CorridorKind,
            CorridorTangent = _search.CorridorTangent,
            Sources = _search.Sources,
            Goals = _search.Goals,
            Cost = _search.Cost,
            Previous = _search.Previous,
            Closed = _search.Closed,
            HeapItems = _search.HeapItems,
            HeapPriorities = _search.HeapPriorities,
            SourceIndex = _search.SourceIndex,
            StepMask = _search.StepMask,
            ReachMark = _search.ReachMark,
            ReachQueue = _search.ReachQueue,
            Result = _search.Result,
            Resolution = _resolution,
            SourceCount = _sources.Count,
            HardGrade = plan.HardGrade,
            HardCross = plan.HardCross,
            MinTurnRadius = _minTurnRadius,
            StrictTurnRadius = _strictTurnRadius,
            SinJunction = Mathf.Sin(_config.JunctionAngle * Mathf.Deg2Rad),
            ReuseDiscount = _config.RoadReuseDiscount,
            ParallelPenalty = _config.ParallelPenalty,
            JunctionPenalty = _config.JunctionPenalty,
            Exact = plan.Exact
        };

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadJob))
            job.Schedule().Complete();

        int bestState = _search.Result[0];
        int bestGoal = _search.Result[1];

        if (bestState < 0)
            return null;

        var path = new List<int>();

        for (int state = bestState; state >= 0; state = _search.Previous[state])
            path.Add(state);

        path.Reverse();

        int first = path[0] / STATES;

        source = _sources[Mathf.Max(0, _search.SourceIndex[first])];
        goal = _goals[bestGoal];

        return path;
    }

    private void PrepareSearch()
    {
        if (_prepared)
            return;

        _prepared = true;
        Array.Fill(_goalCost, float.MaxValue);
        Array.Fill(_goalIndex, -1);

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
        BuildApproachMask();
        MarkApproachPoints();
        UploadPrepared();
    }

    private void UploadPrepared()
    {
        _search.Heuristic.CopyFrom(_heuristic);
        _search.GoalIndex.CopyFrom(_goalIndex);
        _search.ApproachMask.CopyFrom(_approachMask);
        _search.NearPoint.CopyFrom(_nearApproachPoint);

        int cellCount = _resolution * _resolution;
        var ride = new ushort[cellCount];
        var kind = new byte[cellCount];
        var tangent = new float2[cellCount];

        for (int cell = 0; cell < cellCount; cell++)
        {
            if (_corridorEdge[cell] >= 0)
            {
                kind[cell] = ON_CORRIDOR;
                Vector2 along = TangentOf(_corridorEdge[cell], _corridorAlong[cell]);
                tangent[cell] = new float2(along.x, along.y);
                ride[cell] = RideSteps(cell);
                continue;
            }

            if (_bandEdge[cell] < 0)
                continue;

            kind[cell] = NEAR_CORRIDOR;
            Vector2 near = TangentOf(_bandEdge[cell], _bandAlong[cell]);
            tangent[cell] = new float2(near.x, near.y);
        }

        _search.RideMask.CopyFrom(ride);
        _search.CorridorKind.CopyFrom(kind);
        _search.CorridorTangent.CopyFrom(tangent);

        var sources = new SourceData[_sources.Count];

        for (int i = 0; i < sources.Length; i++)
        {
            Endpoint endpoint = _sources[i];
            sources[i] = new SourceData
            {
                Cell = CellOf(endpoint.Point),
                Heading = endpoint.Direct && endpoint.Tangent.sqrMagnitude > 0f ? HeadingOf(endpoint.Tangent) : FREE_HEADING,
                Cost = endpoint.Cost,
                Direct = endpoint.Direct,
                Gateway = endpoint.Gateway != null,
                Tangent = new float2(endpoint.Tangent.x, endpoint.Tangent.y)
            };
        }

        var goals = new GoalData[_goals.Count];

        for (int i = 0; i < goals.Length; i++)
        {
            Endpoint endpoint = _goals[i];
            bool tangential = endpoint.Tangent.sqrMagnitude > 0f;
            goals[i] = new GoalData
            {
                Direct = endpoint.Direct,
                Gateway = endpoint.Gateway != null,
                Tangential = tangential,
                Arrival = tangential ? HeadingOf(endpoint.Tangent) : 0,
                Cost = endpoint.Cost,
                Tangent = new float2(endpoint.Tangent.x, endpoint.Tangent.y)
            };
        }

        _search.Endpoints(sources, goals);
    }

    private ushort RideSteps(int cell)
    {
        int x = cell % _resolution;
        int y = cell / _resolution;
        int mask = 0;

        for (int step = 0; step < HEADINGS; step++)
        {
            int nextX = x + STEP_X[step];
            int nextY = y + STEP_Y[step];

            if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                continue;

            if (IsRide(cell, nextY * _resolution + nextX, step))
                mask |= 1 << step;
        }

        return (ushort)mask;
    }

    private float2[] HeadingVectors()
    {
        var headings = new float2[HEADINGS];

        for (int i = 0; i < HEADINGS; i++)
            headings[i] = new float2(_headings[i].x, _headings[i].y);

        return headings;
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
