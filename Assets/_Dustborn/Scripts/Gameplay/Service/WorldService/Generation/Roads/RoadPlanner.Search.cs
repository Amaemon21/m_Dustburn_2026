using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public partial class RoadPlanner
{
    private struct SearchOutcome
    {
        public List<int> States;
        public Endpoint Source;
        public Endpoint Goal;
        public bool Aborted;
    }

    private sealed class SearchBatch
    {
        public JobHandle[] Handles;
        public bool[] Done;
    }

    private SearchBatch StartSearches(List<SearchPlan> plans)
    {
        PrepareSearch();
        _search.Penalty.CopyFrom(_penalty);
        _search.Blocked.CopyFrom(_blocked);
        System.Threading.Interlocked.Exchange(ref SearchStop.Flag.Data, 0);

        int count = Mathf.Min(plans.Count, MAX_PARALLEL_PLANS);
        var batch = new SearchBatch { Handles = new JobHandle[count], Done = new bool[count] };

        for (int i = 0; i < count; i++)
            batch.Handles[i] = GenerationJobs.Schedule(Job(plans[i], _search.Scratch(i)));

        GenerationJobs.Flush();
        return batch;
    }

    private SearchOutcome FinishSearch(SearchBatch batch, List<SearchPlan> plans, int index)
    {
        if (index >= batch.Handles.Length)
            return SearchOne(plans[index]);

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadJob))
            GenerationJobs.Complete(batch.Handles[index]);

        batch.Done[index] = true;
        SearchOutcome outcome = Read(_search.Scratch(index));

        return outcome.Aborted ? SearchOne(plans[index]) : outcome;
    }

    private void StopSearches(SearchBatch batch)
    {
        System.Threading.Interlocked.Exchange(ref SearchStop.Flag.Data, 1);

        for (int i = 0; i < batch.Handles.Length; i++)
        {
            if (!batch.Done[i])
                GenerationJobs.Complete(batch.Handles[i]);
        }
    }

    private SearchOutcome SearchOne(SearchPlan plan)
    {
        System.Threading.Interlocked.Exchange(ref SearchStop.Flag.Data, 0);
        SearchScratch scratch = _search.Scratch(MAX_PARALLEL_PLANS);

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadJob))
            GenerationJobs.Run(Job(plan, scratch));

        return Read(scratch);
    }

    private SearchJob Job(SearchPlan plan, SearchScratch scratch)
    {
        return new SearchJob
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
            Cost = scratch.Cost,
            Previous = scratch.Previous,
            Closed = scratch.Closed,
            HeapItems = scratch.HeapItems,
            HeapPriorities = scratch.HeapPriorities,
            SourceIndex = scratch.SourceIndex,
            StepMask = scratch.StepMask,
            ReachMark = scratch.ReachMark,
            ReachQueue = scratch.ReachQueue,
            Result = scratch.Result,
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
    }

    private SearchOutcome Read(SearchScratch scratch)
    {
        if (scratch.Result[2] != 0)
            return new SearchOutcome { Aborted = true };

        int bestState = scratch.Result[0];
        int bestGoal = scratch.Result[1];

        if (bestState < 0)
            return default;

        var path = new List<int>();

        for (int state = bestState; state >= 0; state = scratch.Previous[state])
            path.Add(state);

        path.Reverse();

        int first = path[0] / STATES;

        return new SearchOutcome
        {
            States = path,
            Source = _sources[Mathf.Max(0, scratch.SourceIndex[first])],
            Goal = _goals[bestGoal]
        };
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
