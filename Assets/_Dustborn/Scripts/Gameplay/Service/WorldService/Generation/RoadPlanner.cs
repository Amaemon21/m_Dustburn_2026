using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private static readonly int[] STEP_X = { 1, 1, 0, -1, -1, -1, 0, 1, 2, 1, -1, -2, -2, -1, 1, 2 };
    private static readonly int[] STEP_Y = { 0, 1, 1, 1, 0, -1, -1, -1, 1, 2, 2, 1, -1, -2, -2, -1 };
    private static readonly bool[] EXACT_APPROACH_FIRST = { true, false };
    private static readonly bool[] LOOSE_APPROACH = { false };

    private const int HEADINGS = 16;
    private const int FREE_HEADING = HEADINGS;
    private const int STATES = HEADINGS + 1;
    private const int BAND_CELLS = 2;
    private const int PENALTY_CELLS = 2;
    private const int RADIUS_PASSES = 400;
    private const int STEEP_ALLOWANCE = 0;
    private const float GRADE_PROBE = 24f;
    private const float PROFILE_CELL = 8f;
    private const float MIN_CARVE_REACH = 24f;
    private const float MAX_CARVE_REACH = 160f;
    private const int PROFILE_BLUR_PASSES = 2;
    private const float OVERSHOOT_PENALTY = 12f;
    private const float CROSS_OVERSHOOT_PENALTY = 60f;
    private const float CARVE_GRADE_MARGIN = 0.9f;
    private const float TURN_RADIUS_SHARE = 0.85f;
    private const float RELAXED_TURN_SHARE = 0.5f;
    private const float FORD_PENALTY = 8f;
    private const float STANDING_PENALTY = 400f;
    private const float FLOOD_MARGIN = 1f;
    private const float MAX_BRIDGE_COSINE = 0.5f;
    private const float MAX_BRIDGE_COSINE_HARD = 0.7f;
    private const float OBLIQUE_PENALTY = 12f;
    private const float BANK_PENALTY = 60f;
    private const int GATEWAY_TRIES = 6;
    private const int COMPARED_GATEWAYS = 3;
    private const float TIGHT_CLEARANCE = 0.5f;
    private const float HARD_RELAX = 1.6f;
    private const float CROSS_RELAX = 1.25f;
    private const float CHAMFER_SCALE = 0.92f;
    private const float JOIN_REACH_SHARE = 0.75f;
    private const float MAX_JOIN_REACH = 3000f;
    private const float VALLEY_SCALE = 16f;
    private const float VALLEY_CAP = 3f;
    private const float REROUTE_PENALTY = 40f;
    private const float SAMPLE_STEP = 8f;
    private const float END_GRACE = 40f;
    private const float BRANCH_LENGTH = 24f;
    private const float BRANCH_HOLD = 8f;
    private const float STUB_HOLD = 1f;
    private const float RIDE_ALIGNMENT = 0.85f;
    private const float APPROACH_ALIGNMENT = 0.7f;
    private const float APPROACH_ZONE = 0.75f;
    private const float APPROACH_TURN_RADII = 1.5f;
    private const int MIN_RIDE_STEPS = 2;
    private const int DEGENERATE_PENALTY = 10;
    private const float MIN_PIECE = 0.5f;
    private const float BLEND_RADII = 2f;
    private const float BLEND_SHARE = 0.45f;
    private const float FACING_DOT = 0.9f;
    private const float FACING_SLACK_RADII = 2f;
    private const float CONNECT_RADII = 4f;
    private const float CONNECT_SHARE = 0.45f;
    private const float CONNECT_MAX_TURN_DEGREES = 180f;
    private const float CONNECT_DETOUR = 1.5f;
    private const float DIRECT_RADII = 8f;
    private const float DIRECT_DETOUR = 1.6f;
    private const float DIRECT_MAX_TURN_DEGREES = 180f;
    private const float SEARCH_CROSS_MARGIN = 0.9f;
    private const int CROSS_SAMPLES = 3;
    private const float BRIDGE_JOIN_GUARD = 48f;
    private const float CROSS_MARGIN = 0.95f;
    private const float REPAIR_STEP = 4f;
    private const float REPAIR_REACH = 2f;
    private const float CONNECT_WIDENING = 3f;
    private const float REPAIR_TAPER = 1.2f;
    private const float REPAIR_END_GUARD = 1.5f;
    private const int REPAIR_ROUNDS = 2;
    private const float TIGHT_TOLERANCE = 0.97f;
    private const float OVERRUN_TOLERANCE = 0.5f;
    private const float LOOP_RATIO = 3f;
    private const float LOOP_SLACK = 300f;
    private const float JUNCTION_WELD = 2f;
    private const float SEGMENT_CELL = 64f;
    private const float SELF_GAP = 120f;

    public enum PieceShape
    {
        Routed,
        Facing,
        Straight,
        Direct
    }

    public readonly struct RouteRecord
    {
        public int Route { get; }
        public PieceShape Shape { get; }
        public bool FromGateway { get; }
        public bool ToGateway { get; }
        public int Level { get; }
        public bool Relaxed { get; }
        public int Hard { get; }

        public RouteRecord(int route, PieceShape shape, bool fromGateway, bool toGateway, int level, bool relaxed, int hard)
        {
            Route = route;
            Shape = shape;
            FromGateway = fromGateway;
            ToGateway = toGateway;
            Level = level;
            Relaxed = relaxed;
            Hard = hard;
        }
    }

    private sealed class Endpoint
    {
        public bool Direct;
        public SettlementGateway Gateway;
        public int Edge = -1;
        public float Along;
        public float Cost;
        public Vector2 Point;
        public Vector2 Tangent;
    }

    private sealed class Route
    {
        public Vector2[] Points;
        public Endpoint Start;
        public Endpoint End;
        public float Cost;
        public int Hard;
        public int Shallow;
        public int Intrusions;
        public int Parallel;
        public int Steep;
        public int Tilted;
        public int Tight;
        public int Self;
        public int Flooded;
        public int Oblique;
        public float Water;
        public PieceShape Shape;
        public int Id = -1;
        public readonly List<(float Along, int Edge, float EdgeAlong, Vector2 Point)> Crossings = new();
        public readonly List<Vector2> Problems = new();
    }

    private sealed class Journey
    {
        public readonly List<Route> Pieces = new();
        public readonly List<Vector2> Problems = new();
        public float Cost;
        public int Hard;
        public int Self;
        public int Rides;
        public float Water;
        public int Level;
        public bool Relaxed;
    }

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly WaterMap _water;
    private readonly RoadSmoother _smoother;

    private readonly int _resolution;
    private readonly float _cellSize;
    private readonly Vector2[] _headings = new Vector2[HEADINGS];

    private readonly float[] _cost;
    private readonly int[] _previous;
    private readonly bool[] _closed;
    private readonly MinHeap _open;
    private readonly float[] _stepCost;
    private readonly float[] _grade;
    private readonly float[] _cross;
    private readonly float[] _turnCost;
    private readonly float[] _turnRadius;
    private readonly bool[] _blocked;
    private readonly float[] _penalty;
    private readonly float[] _heuristic;
    private readonly float[] _profile;
    private readonly int _profileResolution;
    private readonly int[] _corridorEdge;
    private readonly float[] _corridorAlong;
    private readonly int[] _bandEdge;
    private readonly float[] _bandAlong;
    private readonly byte[] _bandDistance;
    private readonly float[] _goalCost;
    private readonly int[] _goalIndex;
    private readonly int[] _sourceIndex;

    private readonly Dictionary<int, List<long>> _segments = new();
    private readonly HashSet<int> _indexed = new();
    private readonly List<Endpoint> _sources = new();
    private readonly List<Endpoint> _goals = new();
    private readonly List<(Vector2 Point, Vector2 Outward, Vector2 Travel)> _zones = new();
    private readonly float[] _stepLength = new float[HEADINGS];

    private RoadGraph _graph;
    private float[] _gatewayDistance;
    private bool _strictApproach;
    private bool _exactApproach;
    private bool _forceFacing;
    private float _minTurnRadius;
    private readonly float _strictTurnRadius;
    private float _connectCorridor;
    private readonly List<Vector2> _approachPoints = new();
    private IReadOnlyList<SettlementLayout> _layouts;
    private int _indexedEdges;
    private float _hardGrade;
    private float _hardCross;

    public int SettledCells { get; private set; }
    public int Routed { get; private set; }
    public int Failed { get; private set; }
    public int GatewaySwaps { get; private set; }
    public int Squeezed { get; private set; }
    public int StubCells { get; private set; }
    public int Relaxed { get; private set; }
    public int Rerouted { get; private set; }
    public int DroppedLoops { get; private set; }
    public int Joins { get; private set; }
    public int Rides { get; private set; }
    public int Crossings { get; private set; }
    public int ShallowJunctions { get; private set; }
    public int CommittedIntrusions { get; private set; }
    public int CommittedParallel { get; private set; }
    public int CommittedSteep { get; private set; }
    public int CommittedTilted { get; private set; }
    public int CommittedFlooded { get; private set; }
    public int CommittedOblique { get; private set; }
    public int CommittedTight { get; private set; }
    public int CommittedSelf { get; private set; }
    public int FacingPieces { get; private set; }
    public int DirectPieces { get; private set; }
    public int StraightPieces { get; private set; }
    public int EndpointsRelaxed { get; private set; }
    public int GradeRelaxed { get; private set; }
    public int GradeLifted { get; private set; }
    public List<Vector2> Problems { get; } = new();
    public List<RouteRecord> Records { get; } = new();

    public RoadPlanner(WorldGenerationConfig config, HeightMap map, WaterMap water)
    {
        _config = config;
        _map = map;
        _water = water;

        _cellSize = config.RoadCellSize;
        _strictTurnRadius = config.HighwayMinCurveRadius * TURN_RADIUS_SHARE;
        _connectCorridor = config.RoadCorridor;
        _resolution = Mathf.Max(2, Mathf.RoundToInt(config.WorldSize / _cellSize));

        for (int i = 0; i < HEADINGS; i++)
            _headings[i] = new Vector2(STEP_X[i], STEP_Y[i]).normalized;

        int cellCount = _resolution * _resolution;

        _cost = new float[cellCount * STATES];
        _previous = new int[cellCount * STATES];
        _closed = new bool[cellCount * STATES];
        _open = new MinHeap(cellCount / 2);
        _stepCost = new float[cellCount * HEADINGS];
        _grade = new float[cellCount * HEADINGS];
        _cross = new float[cellCount * HEADINGS];
        _turnCost = new float[STATES * HEADINGS];
        _turnRadius = new float[STATES * HEADINGS];
        _blocked = new bool[cellCount];
        _penalty = new float[cellCount];
        _heuristic = new float[cellCount];
        _corridorEdge = new int[cellCount];
        _corridorAlong = new float[cellCount];
        _bandEdge = new int[cellCount];
        _bandAlong = new float[cellCount];
        _bandDistance = new byte[cellCount];
        _goalCost = new float[cellCount];
        _goalIndex = new int[cellCount];
        _sourceIndex = new int[cellCount];

        Array.Fill(_corridorEdge, -1);
        Array.Fill(_bandEdge, -1);
        Array.Fill(_bandDistance, byte.MaxValue);
        Array.Fill(_penalty, 1f);

        _profileResolution = Mathf.Max(2, Mathf.CeilToInt(config.WorldSize / PROFILE_CELL) + 1);
        _profile = BuildProfileField();

        PrecomputeSteps();

        _smoother = new RoadSmoother(map)
        {
            SimplifyTolerance = config.RoadSimplifyTolerance,
            ChaikinPasses = config.RoadSmoothPasses,
            RelaxPasses = config.RoadRelaxPasses,
            RelaxStrength = config.RoadRelaxStrength,
            Corridor = config.RoadCorridor,
            MaxGrade = config.RoadMaxGrade,
            MaxCrossSlope = config.HighwayMaxCrossSlope * CROSS_MARGIN,
            CrossProbe = config.RoadHalfWidth + config.RoadShoulder,
            Spacing = config.RoadPointSpacing
        };
    }

    public RoadGraph Plan(IReadOnlyList<Hub> hubs, IReadOnlyList<RegionalLink> links, IReadOnlyList<SettlementLayout> layouts)
    {
        _graph = new RoadGraph();
        _segments.Clear();
        _indexed.Clear();
        _indexedEdges = 0;
        Problems.Clear();
        Array.Fill(_corridorEdge, -1);
        Array.Fill(_bandEdge, -1);
        Array.Fill(_bandDistance, byte.MaxValue);

        MarkSettlements(layouts);
        CreateGatewayNodes(layouts);

        if (hubs.Count < 2 || links == null)
        {
            Debug.LogWarning($"No roads built: {hubs.Count} settlements, at least 2 are needed");

            return _graph;
        }

        foreach (RegionalLink link in links)
        {
            SettlementLayout from = link.From < layouts.Count ? layouts[link.From] : null;
            SettlementLayout to = link.To < layouts.Count ? layouts[link.To] : null;
            SettlementGateway start = from?.GatewayToward(link.To);
            SettlementGateway end = to?.GatewayToward(link.From);

            if (start == null || end == null)
            {
                Failed++;
                Debug.LogWarning($"Link {link.From}-{link.To} has no gateway on one side: the settlement found no buildable core");
                continue;
            }

            if (RouteBest(link, from, to, start, end))
                continue;

            MarkSettlements(layouts, TIGHT_CLEARANCE);
            start.Neighbours.Add(link.To);
            end.Neighbours.Add(link.From);
            bool squeezed = RouteBest(link, from, to, start, end);
            MarkSettlements(layouts);

            if (squeezed)
            {
                Squeezed++;
                continue;
            }

            Failed++;
            Debug.LogWarning($"Could not route a highway between settlements {link.From} and {link.To} through any of their gateways: preferred pair at ({start.Port.x:F0}, {start.Port.y:F0}) and ({end.Port.x:F0}, {end.Port.y:F0}), approach cells blocked {_blocked[CellOf(start.Approach)]} and {_blocked[CellOf(end.Approach)]}");
        }

        Debug.Log($"Highways: {Routed} links routed, {Failed} failed, {GatewaySwaps} through another gateway, {Squeezed} past a neighbour at half the settlement clearance, {DroppedLoops} loops dropped for saving too little or failing validation, {Joins} junction ends on existing highways, {Rides} stretches shared with an existing highway, {Crossings} X junctions, {Relaxed} routes needed relaxed grade limits, {Rerouted} reroutes; committed with problems: {ShallowJunctions} shallow junctions, {CommittedIntrusions} settlement intrusions, {CommittedParallel} parallel runs, {CommittedSteep} steep samples, {CommittedTilted} tilted samples, {CommittedTight} tight curve samples, {CommittedSelf} segments touching their own route, {CommittedFlooded} samples on water or in a river channel, {CommittedOblique} oblique river crossings; grade limits relaxed {GradeRelaxed} times and lifted {GradeLifted} times, endpoint rules relaxed {EndpointsRelaxed} times; {FacingPieces} facing gateway pairs, {DirectPieces} short direct links and {StraightPieces} straight connectors built without a search");

        return _graph;
    }

    private List<int> FindPath(Vector2 from, Vector2 to)
    {
        _sources.Clear();
        _goals.Clear();
        _zones.Clear();
        _approachPoints.Clear();
        _sources.Add(new Endpoint { Direct = true, Point = from, Tangent = Vector2.zero });
        _goals.Add(new Endpoint { Direct = true, Point = to, Tangent = Vector2.zero });
        _hardGrade = float.MaxValue;
        _hardCross = float.MaxValue;
        _minTurnRadius = 0f;
        Array.Fill(_penalty, 1f);

        List<int> states = Search(out _, out _);

        if (states == null)
            return null;

        var cells = new List<int>(states.Count);

        foreach (int state in states)
            cells.Add(state / STATES);

        return cells;
    }

    private void CreateGatewayNodes(IReadOnlyList<SettlementLayout> layouts)
    {
        if (layouts == null)
            return;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null)
                continue;

            foreach (SettlementGateway gateway in layout.Gateways)
                gateway.Node = _graph.AddNode(gateway.Port, RoadNodeKind.Gateway, layout.Index, gateway.Index);
        }
    }

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

    private struct LinkPlan
    {
        public SettlementGateway Start;
        public SettlementGateway End;
        public Journey Journey;
        public bool Loop;
        public float Existing;
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

            for (int level = 0; level < gradeLimits.Length; level++)
            {
                _hardGrade = _config.RoadMaxGrade * gradeLimits[level];
                _hardCross = _config.HighwayMaxCrossSlope * SEARCH_CROSS_MARGIN * crossLimits[level];

                foreach (bool exact in relaxed ? LOOSE_APPROACH : EXACT_APPROACH_FIRST)
                {
                    _exactApproach = exact;

                    List<int> states = Search(out Endpoint source, out Endpoint goal);

                    if (states == null)
                        continue;

                    Journey journey = Assemble(states, source, goal);
                    journey.Level = level;
                    journey.Relaxed = relaxed;

                    if (journey.Self > 0)
                    {
                        if (Better(journey, tangled))
                            tangled = journey;

                        continue;
                    }

                    _exactApproach = false;

                    return Chosen(facing != null && facing.Hard <= journey.Hard ? facing : journey);
                }
            }
        }

        _exactApproach = false;

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
