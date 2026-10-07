using System;
using System.Collections.Generic;
using UnityEngine;

public partial class RoadPlanner
{
    private bool RouteBest(RegionalLink link, SettlementLayout from, SettlementLayout to, SettlementGateway start, SettlementGateway end)
    {
        LinkPlan best = PlanLink(start, end);

        if (best.Journey == null || best.Journey.Hard > 0)
        {
            foreach ((SettlementGateway first, SettlementGateway second) in OtherGateways(from, to, start, end, best.Journey == null ? GATEWAY_TRIES : COMPARED_GATEWAYS))
            {
                bool firstNew = !first.Neighbours.Contains(link.To);
                bool secondNew = !second.Neighbours.Contains(link.From);

                if (firstNew)
                    first.Neighbours.Add(link.To);

                if (secondNew)
                    second.Neighbours.Add(link.From);

                LinkPlan candidate = PlanLink(first, second);

                if (candidate.Journey != null && (best.Journey == null || Better(candidate.Journey, best.Journey)))
                {
                    Release(link, best, start, end);
                    best = candidate;
                    Hold(link, best);

                    if (best.Journey.Hard == 0)
                        break;

                    continue;
                }

                if (firstNew)
                    first.Neighbours.Remove(link.To);

                if (secondNew)
                    second.Neighbours.Remove(link.From);
            }
        }

        if (best.Journey == null)
        {
            start.Neighbours.Remove(link.To);
            end.Neighbours.Remove(link.From);
            return false;
        }

        if (best.Start != start || best.End != end)
            GatewaySwaps++;

        return Accept(link, best);
    }

    private static void Release(RegionalLink link, LinkPlan plan, SettlementGateway start, SettlementGateway end)
    {
        if (plan.Start == null || (plan.Start == start && plan.End == end))
        {
            start.Neighbours.Remove(link.To);
            end.Neighbours.Remove(link.From);
            return;
        }

        plan.Start.Neighbours.Remove(link.To);
        plan.End.Neighbours.Remove(link.From);
    }

    private static void Hold(RegionalLink link, LinkPlan plan)
    {
        if (!plan.Start.Neighbours.Contains(link.To))
            plan.Start.Neighbours.Add(link.To);

        if (!plan.End.Neighbours.Contains(link.From))
            plan.End.Neighbours.Add(link.From);
    }

    private static List<(SettlementGateway Start, SettlementGateway End)> OtherGateways(SettlementLayout from, SettlementLayout to, SettlementGateway start, SettlementGateway end, int limit)
    {
        Vector2 direction = (end.Port - start.Port).normalized;
        var pairs = new List<(SettlementGateway Start, SettlementGateway End, float Score)>();

        foreach (SettlementGateway first in from.Gateways)
        {
            foreach (SettlementGateway second in to.Gateways)
            {
                if (first == start && second == end)
                    continue;

                float score = Vector2.Dot(first.Tangent, direction) - Vector2.Dot(second.Tangent, direction);
                pairs.Add((first, second, score));
            }
        }

        pairs.Sort((a, b) => b.Score.CompareTo(a.Score));

        var result = new List<(SettlementGateway Start, SettlementGateway End)>();

        for (int i = 0; i < pairs.Count && i < limit; i++)
            result.Add((pairs[i].Start, pairs[i].End));

        return result;
    }

    private LinkPlan PlanLink(SettlementGateway start, SettlementGateway end)
    {
        Func<RoadEdge, bool> highways = edge => edge.Kind == RoadKind.Highway;
        int[] components = _graph.Components(highways, out _);
        bool loop = components[start.Node] >= 0 && components[start.Node] == components[end.Node];

        float[] fromStart = _graph.Distances(new[] { (start.Node, 0f) }, highways);
        float[] fromEnd = _graph.Distances(new[] { (end.Node, 0f) }, highways);
        var gateways = new List<(int Node, float Cost)>();

        foreach (RoadNode node in _graph.Nodes)
        {
            if (node.Kind == RoadNodeKind.Gateway && _graph.Degree(node.Id, RoadKind.Highway) > 0)
                gateways.Add((node.Id, 0f));
        }

        _gatewayDistance = _graph.Distances(gateways, highways);
        float reach = Mathf.Min(Vector2.Distance(start.Approach, end.Approach) * JOIN_REACH_SHARE, MAX_JOIN_REACH);

        Array.Fill(_penalty, 1f);

        Journey best = null;
        int attempts = Mathf.Max(1, _config.RouteAttempts);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Journey journey = TryRoute(start, end, fromStart, fromEnd, loop, reach);

            if (journey == null)
                break;

            if (Better(journey, best))
                best = journey;

            if (journey.Hard == 0)
                break;

            Rerouted++;

            foreach (Vector2 problem in journey.Problems)
                Penalise(problem);
        }

        return new LinkPlan { Start = start, End = end, Journey = best, Loop = loop, Existing = fromStart[end.Node] };
    }

    private bool Accept(RegionalLink link, LinkPlan plan)
    {
        Journey best = plan.Journey;
        SettlementGateway start = plan.Start;
        SettlementGateway end = plan.End;
        bool loop = plan.Loop;
        float existing = plan.Existing;

        if (loop)
        {
            float gain = existing - best.Cost;

            if (existing >= float.MaxValue * 0.5f || best.Hard > 0 || gain < existing * _config.MinLoopGain || best.Water > _config.MaxWaterExposure)
            {
                start.Neighbours.Remove(link.To);
                end.Neighbours.Remove(link.From);
                DroppedLoops++;
                return true;
            }
        }

        foreach (Route piece in best.Pieces)
        {
            CommittedSteep += piece.Steep;
            CommittedTilted += piece.Tilted;
            CommittedTight += piece.Tight;
            CommittedSelf += piece.Self;
            CommittedFlooded += piece.Flooded;
            CommittedOblique += piece.Oblique;

            if (piece.Shape == PieceShape.Facing)
                FacingPieces++;

            if (piece.Shape == PieceShape.Direct)
                DirectPieces++;

            if (piece.Shape == PieceShape.Straight)
                StraightPieces++;
        }

        Commit(best);
        Routed++;

        foreach (Route piece in best.Pieces)
        {
            Records.Add(new RouteRecord(piece.Id, piece.Shape, piece.Start.Direct && piece.Start.Gateway != null,
                piece.End.Direct && piece.End.Gateway != null, best.Level, best.Relaxed, piece.Hard));
        }

        return true;
    }

    private Journey TryRoute(SettlementGateway start, SettlementGateway end, float[] fromStart, float[] fromEnd, bool loop, float reach)
    {
        float[] gradeLimits = { 1f, HARD_RELAX, float.MaxValue, float.MaxValue, float.MaxValue };
        float[] crossLimits = { 1f, 1f, 1f, CROSS_RELAX, float.MaxValue };

        Journey facing = null;

        if (_graph.Degree(start.Node, RoadKind.Highway) == 0 && _graph.Degree(end.Node, RoadKind.Highway) == 0 && FacingCurve(start, end, out _) != null)
        {
            var facingSource = new Endpoint { Direct = true, Gateway = start, Point = start.Approach, Tangent = start.Tangent };
            var facingGoal = new Endpoint { Direct = true, Gateway = end, Point = end.Approach, Tangent = -end.Tangent };

            _forceFacing = true;
            facing = Assemble(new List<int> { CellOf(start.Approach) * STATES + FREE_HEADING }, facingSource, facingGoal);
            _forceFacing = false;

            if (facing.Hard == 0)
                return facing;
        }

        Journey tangled = null;

        foreach (bool relaxed in new[] { false, true })
        {
            BuildEndpoints(start, end, fromStart, fromEnd, loop, relaxed ? MAX_JOIN_REACH : reach, relaxed);
            _minTurnRadius = _config.HighwayMinCurveRadius * (relaxed ? RELAXED_TURN_SHARE : TURN_RADIUS_SHARE);

            if (_sources.Count == 0 || _goals.Count == 0)
                continue;

            var plans = new List<SearchPlan>();

            for (int level = 0; level < gradeLimits.Length; level++)
            {
                float hardGrade = _config.RoadMaxGrade * gradeLimits[level];
                float hardCross = _config.HighwayMaxCrossSlope * SEARCH_CROSS_MARGIN * crossLimits[level];

                foreach (bool exact in relaxed ? LOOSE_APPROACH : EXACT_APPROACH_FIRST)
                    plans.Add(new SearchPlan(level, exact, hardGrade, hardCross));
            }

            using (WorldGenProbe.Measure(WorldGenStage.MapRoadPrepare))
                PrepareSearch();

            for (int i = 0; i < plans.Count; i++)
            {
                List<int> states;
                Endpoint source;
                Endpoint goal;

                using (WorldGenProbe.Measure(WorldGenStage.MapRoadSearch))
                    states = Search(plans[i], out source, out goal);

                if (states == null)
                    continue;

                Journey journey;

                using (WorldGenProbe.Measure(WorldGenStage.MapRoadAssemble))
                    journey = Assemble(states, source, goal);
                journey.Level = plans[i].Level;
                journey.Relaxed = relaxed;

                if (journey.Self > 0)
                {
                    if (Better(journey, tangled))
                        tangled = journey;

                    continue;
                }

                return Chosen(facing != null && facing.Hard <= journey.Hard ? facing : journey);
            }
        }

        return Chosen(facing != null && (tangled == null || !Better(tangled, facing)) ? facing : tangled);
    }

    private Journey Chosen(Journey journey)
    {
        if (journey == null)
            return null;

        if (journey.Level > 0 || journey.Relaxed)
            Relaxed++;

        if (journey.Relaxed)
            EndpointsRelaxed++;

        if (journey.Level == 1)
            GradeRelaxed++;

        if (journey.Level >= 2)
            GradeLifted++;

        return journey;
    }

    private static bool Better(Journey candidate, Journey current)
    {
        return current == null || candidate.Self < current.Self || (candidate.Self == current.Self && candidate.Hard < current.Hard);
    }
}
