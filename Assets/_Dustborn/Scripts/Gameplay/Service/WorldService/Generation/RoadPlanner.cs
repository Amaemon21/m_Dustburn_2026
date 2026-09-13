using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class RoadPlanner
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
    private const float CROSS_MARGIN = 0.95f;
    private const float REPAIR_STEP = 4f;
    private const float REPAIR_REACH = 2f;
    private const float CONNECT_WIDENING = 3f;
    private const float REPAIR_TAPER = 1.2f;
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

    public RoadPlanner(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;

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

            RouteLink(link, start, end);
        }

        Debug.Log($"Highways: {Routed} links routed, {Failed} failed, {DroppedLoops} loops dropped for saving too little or failing validation, {Joins} junction ends on existing highways, {Rides} stretches shared with an existing highway, {Crossings} X junctions, {Relaxed} routes needed relaxed grade limits, {Rerouted} reroutes; committed with problems: {ShallowJunctions} shallow junctions, {CommittedIntrusions} settlement intrusions, {CommittedParallel} parallel runs, {CommittedSteep} steep samples, {CommittedTilted} tilted samples, {CommittedTight} tight curve samples, {CommittedSelf} segments touching their own route; grade limits relaxed {GradeRelaxed} times and lifted {GradeLifted} times, endpoint rules relaxed {EndpointsRelaxed} times; {FacingPieces} facing gateway pairs, {DirectPieces} short direct links and {StraightPieces} straight connectors built without a search");

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

    private void RouteLink(RegionalLink link, SettlementGateway start, SettlementGateway end)
    {
        Func<RoadEdge, bool> highways = edge => edge.Kind == RoadKind.Highway;
        int[] components = _graph.Components(highways, out _);
        bool loop = components[start.Node] >= 0 && components[start.Node] == components[end.Node];

        float[] fromStart = _graph.Distances(new[] { (start.Node, 0f) }, highways);
        float[] fromEnd = _graph.Distances(new[] { (end.Node, 0f) }, highways);
        float existing = fromStart[end.Node];
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

        if (best == null)
        {
            start.Neighbours.Remove(link.To);
            end.Neighbours.Remove(link.From);
            Failed++;
            Debug.LogWarning($"Could not route a highway between settlements {link.From} and {link.To}: gateways at ({start.Port.x:F0}, {start.Port.y:F0}) and ({end.Port.x:F0}, {end.Port.y:F0}), approach cells blocked {_blocked[CellOf(start.Approach)]} and {_blocked[CellOf(end.Approach)]}, last attempt had {_sources.Count} sources and {_goals.Count} goals");
            return;
        }

        if (loop)
        {
            float gain = existing - best.Cost;

            if (existing >= float.MaxValue * 0.5f || best.Hard > 0 || gain < existing * _config.MinLoopGain || best.Water > _config.MaxWaterExposure)
            {
                start.Neighbours.Remove(link.To);
                end.Neighbours.Remove(link.From);
                DroppedLoops++;
                return;
            }
        }

        foreach (Route piece in best.Pieces)
        {
            CommittedSteep += piece.Steep;
            CommittedTilted += piece.Tilted;
            CommittedTight += piece.Tight;
            CommittedSelf += piece.Self;

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

                if (toStart <= reach && (!loop || toStart < toEnd) && (point - end.Port).sqrMagnitude >= keep * keep)
                    _sources.Add(Corridor(edge, along, point, toStart));

                if (toEnd <= reach && (!loop || toEnd < toStart) && (point - start.Port).sqrMagnitude >= keep * keep)
                    _goals.Add(Corridor(edge, along, point, toEnd));
            }
        }
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

                if (_config.SeaLevel > 0f && _map.SampleWorldSmooth(point.x, point.y) < _config.SeaLevel + _config.ShoreMargin)
                    wet++;

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
            travelled += length;
        }

        CountSteep(route, points, grace);
        CountTight(route, points);
        CountSelfContacts(route, points);

        route.Water = samples == 0 ? 0f : wet / (float)samples;
        route.Hard = route.Intrusions + route.Shallow + Mathf.Max(0, route.Parallel - 1) + (route.Steep > STEEP_ALLOWANCE ? route.Steep : 0)
            + route.Tilted + route.Tight + route.Self;

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
        Vector2[] points = route.Points;
        int count = points.Length;

        if (count < 5)
            return route;

        float[] distance = Cumulative(points);
        float total = distance[^1];
        float limit = _config.HighwayMaxCrossSlope * CROSS_MARGIN;
        float radius = _config.HighwayMinCurveRadius;
        var field = new float[count];
        bool shifted = false;

        for (int i = 1; i < count - 1; i++)
        {
            Vector2 direction = points[i + 1] - points[i - 1];

            if (distance[i] < startHold || total - distance[i] < endHold || CrossSlopeAt(points[i], direction) <= limit)
                continue;

            float shift = ShiftFor(points[i], direction, limit);

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

        return repaired.Tilted < route.Tilted && repaired.Hard <= route.Hard ? repaired : route;
    }

    private float ShiftFor(Vector2 point, Vector2 direction, float limit)
    {
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized;

        for (float shift = REPAIR_STEP; shift <= _config.RoadCorridor * REPAIR_REACH; shift += REPAIR_STEP)
        {
            if (Acceptable(point + normal * shift, direction, limit))
                return shift;

            if (Acceptable(point - normal * shift, direction, limit))
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

    private void Commit(Journey journey)
    {
        foreach (Route piece in journey.Pieces)
            Commit(piece);

        Rides += journey.Rides;
    }

    private void Commit(Route route)
    {
        route.Crossings.Sort((left, right) => left.Along.CompareTo(right.Along));

        int startNode = NodeFor(route.Start);
        var nodes = new List<(float Along, int Node)> { (0f, startNode) };

        foreach ((float along, int edge, float edgeAlong, Vector2 _) in route.Crossings)
        {
            int node = _graph.Split(edge, edgeAlong);

            if (nodes[^1].Node == node)
                continue;

            nodes.Add((along, node));
            Crossings++;
        }

        int endNode = NodeFor(route.End);

        nodes.Add((Length(route.Points), endNode));

        int routeId = _graph.NewRoute();
        Vector2[] points = route.Points;

        route.Id = routeId;
        float[] distance = Cumulative(points);

        for (int k = 0; k < nodes.Count - 1; k++)
        {
            if (nodes[k].Node == nodes[k + 1].Node)
                continue;

            var piece = new List<Vector2> { _graph.Nodes[nodes[k].Node].Position };

            for (int i = 1; i < points.Length - 1; i++)
            {
                if (distance[i] > nodes[k].Along + JUNCTION_WELD && distance[i] < nodes[k + 1].Along - JUNCTION_WELD)
                    piece.Add(points[i]);
            }

            piece.Add(_graph.Nodes[nodes[k + 1].Node].Position);

            int edge = _graph.AddEdge(nodes[k].Node, nodes[k + 1].Node, piece, RoadKind.Highway, routeId);

            Rasterise(_graph.Edges[edge]);
            IndexSegments(_graph.Edges[edge]);
        }

        IndexSplits();

        if (route.Shallow > 0)
            ShallowJunctions += route.Shallow;

        CommittedIntrusions += route.Intrusions;
        CommittedParallel += route.Parallel > 1 ? 1 : 0;
        Problems.AddRange(route.Problems);
    }

    private int NodeFor(Endpoint endpoint)
    {
        if (endpoint.Direct)
            return endpoint.Gateway.Node;

        Joins++;

        return _graph.Split(endpoint.Edge, endpoint.Along);
    }

    private void IndexSplits()
    {
        for (int i = _indexedEdges; i < _graph.Edges.Count; i++)
        {
            RoadEdge edge = _graph.Edges[i];

            if (edge.Alive && !_indexed.Contains(edge.Id))
            {
                IndexSegments(edge);
                Rasterise(edge);
            }
        }

        _indexedEdges = _graph.Edges.Count;
    }

    private void IndexSegments(RoadEdge edge)
    {
        if (!_indexed.Add(edge.Id))
            return;

        for (int segment = 0; segment < edge.Points.Length - 1; segment++)
        {
            Vector2 a = edge.Points[segment];
            Vector2 b = edge.Points[segment + 1];

            int minX = SegmentCell(Mathf.Min(a.x, b.x));
            int maxX = SegmentCell(Mathf.Max(a.x, b.x));
            int minY = SegmentCell(Mathf.Min(a.y, b.y));
            int maxY = SegmentCell(Mathf.Max(a.y, b.y));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int key = y * 65536 + x;

                    if (!_segments.TryGetValue(key, out List<long> list))
                    {
                        list = new List<long>();
                        _segments[key] = list;
                    }

                    list.Add(((long)edge.Id << 20) | (long)segment);
                }
            }
        }
    }

    private IEnumerable<long> SegmentsNear(Vector2 from, Vector2 to)
    {
        int minX = SegmentCell(Mathf.Min(from.x, to.x));
        int maxX = SegmentCell(Mathf.Max(from.x, to.x));
        int minY = SegmentCell(Mathf.Min(from.y, to.y));
        int maxY = SegmentCell(Mathf.Max(from.y, to.y));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_segments.TryGetValue(y * 65536 + x, out List<long> list))
                    continue;

                foreach (long entry in list)
                    yield return entry;
            }
        }
    }

    private int SegmentCell(float coordinate)
    {
        return Mathf.Max(0, Mathf.FloorToInt(coordinate / SEGMENT_CELL));
    }

    private void Rasterise(RoadEdge edge)
    {
        float spacing = _cellSize / 3f;

        for (float along = 0f; along <= edge.Length; along += spacing)
        {
            Vector2 point = edge.PointAt(along);
            int cell = CellOf(point);
            int x = cell % _resolution;
            int y = cell / _resolution;

            _corridorEdge[cell] = edge.Id;
            _corridorAlong[cell] = along;

            for (int dy = -BAND_CELLS; dy <= BAND_CELLS; dy++)
            {
                for (int dx = -BAND_CELLS; dx <= BAND_CELLS; dx++)
                {
                    int bandX = x + dx;
                    int bandY = y + dy;

                    if (bandX < 0 || bandY < 0 || bandX >= _resolution || bandY >= _resolution)
                        continue;

                    int band = bandY * _resolution + bandX;
                    byte ring = (byte)Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

                    if (ring > _bandDistance[band])
                        continue;

                    _bandDistance[band] = ring;
                    _bandEdge[band] = edge.Id;
                    _bandAlong[band] = along;
                }
            }
        }
    }

    private void Penalise(Vector2 point)
    {
        int cell = CellOf(point);
        int x = cell % _resolution;
        int y = cell / _resolution;

        for (int dy = -PENALTY_CELLS; dy <= PENALTY_CELLS; dy++)
        {
            for (int dx = -PENALTY_CELLS; dx <= PENALTY_CELLS; dx++)
            {
                int px = x + dx;
                int py = y + dy;

                if (px < 0 || py < 0 || px >= _resolution || py >= _resolution)
                    continue;

                _penalty[py * _resolution + px] = REROUTE_PENALTY;
            }
        }
    }

    private void MarkSettlements(IReadOnlyList<SettlementLayout> layouts)
    {
        Array.Clear(_blocked, 0, _blocked.Length);
        _layouts = layouts;
        SettledCells = 0;

        if (layouts == null)
            return;

        float reach = _config.HighwaySettlementClearance + _cellSize * 0.75f;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            int minX = Mathf.Clamp(Mathf.FloorToInt((layout.Min.x - reach) / _cellSize), 0, _resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((layout.Max.x + reach) / _cellSize), 0, _resolution - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((layout.Min.y - reach) / _cellSize), 0, _resolution - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((layout.Max.y + reach) / _cellSize), 0, _resolution - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = y * _resolution + x;

                    if (_blocked[cell] || !layout.IsWithin(CellCenter(cell), reach))
                        continue;

                    _blocked[cell] = true;
                    SettledCells++;
                }
            }
        }
    }

    private bool IsBlocked(Vector2 point)
    {
        return _blocked[CellOf(point)];
    }

    private void PrecomputeSteps()
    {
        float[] valley = ValleyField();

        Parallel.For(0, _resolution, y =>
        {
            for (int x = 0; x < _resolution; x++)
            {
                for (int step = 0; step < HEADINGS; step++)
                {
                    int nextX = x + STEP_X[step];
                    int nextY = y + STEP_Y[step];
                    int index = (y * _resolution + x) * HEADINGS + step;

                    if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                    {
                        _grade[index] = float.MaxValue;
                        continue;
                    }

                    BaseCost(x, y, nextX, nextY, valley[nextY * _resolution + nextX], out _stepCost[index], out _grade[index], out _cross[index]);
                }
            }
        });

        for (int heading = 0; heading < STATES; heading++)
        {
            for (int step = 0; step < HEADINGS; step++)
            {
                _turnCost[heading * HEADINGS + step] = TurnCost(heading, step);
                _turnRadius[heading * HEADINGS + step] = TurnRadius(heading, step);
            }
        }

        for (int step = 0; step < HEADINGS; step++)
            _stepLength[step] = new Vector2(STEP_X[step], STEP_Y[step]).magnitude * _cellSize;
    }

    private float[] ValleyField()
    {
        int cellCount = _resolution * _resolution;
        var heights = new float[cellCount];
        var multiplier = new float[cellCount];

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
                heights[y * _resolution + x] = SampleMeters(x, y);
        }

        if (_config.ValleyPreference <= 0f)
        {
            Array.Fill(multiplier, 1f);
            return multiplier;
        }

        int radius = Mathf.Max(1, Mathf.RoundToInt(_config.ValleyRadius / _cellSize));
        float[] blurred = BoxBlur(heights, _resolution, radius);

        for (int i = 0; i < cellCount; i++)
        {
            float above = Mathf.Max(0f, heights[i] - blurred[i]) / VALLEY_SCALE;

            multiplier[i] = 1f + _config.ValleyPreference * Mathf.Min(above, VALLEY_CAP);
        }

        return multiplier;
    }

    private static float[] BoxBlur(float[] source, int size, int radius)
    {
        var horizontal = new float[source.Length];
        var result = new float[source.Length];
        float scale = 1f / (2 * radius + 1);

        for (int y = 0; y < size; y++)
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += source[y * size + Mathf.Clamp(k, 0, size - 1)];

            for (int x = 0; x < size; x++)
            {
                horizontal[y * size + x] = sum * scale;
                sum += source[y * size + Mathf.Min(x + radius + 1, size - 1)] - source[y * size + Mathf.Max(x - radius, 0)];
            }
        }

        for (int x = 0; x < size; x++)
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += horizontal[Mathf.Clamp(k, 0, size - 1) * size + x];

            for (int y = 0; y < size; y++)
            {
                result[y * size + x] = sum * scale;
                sum += horizontal[Mathf.Min(y + radius + 1, size - 1) * size + x] - horizontal[Mathf.Max(y - radius, 0) * size + x];
            }
        }

        return result;
    }

    private void BaseCost(int fromX, int fromY, int toX, int toY, float valley, out float cost, out float grade, out float cross)
    {
        float deltaX = (toX - fromX) * _cellSize;
        float deltaY = (toY - fromY) * _cellSize;
        float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

        float fromHeight = SampleMeters(fromX, fromY);
        float toHeight = SampleMeters(toX, toY);

        grade = StepGrade(fromX, fromY, toX, toY, distance);
        cross = CrossSlope(fromX, fromY, toX, toY, deltaX, deltaY, distance);

        float gradeOvershoot = Mathf.Max(0f, grade - _config.RoadMaxGrade) / Mathf.Max(_config.RoadMaxGrade, 1e-3f);
        float crossOvershoot = Mathf.Max(0f, cross - _config.HighwayMaxCrossSlope) / Mathf.Max(_config.HighwayMaxCrossSlope, 1e-3f);
        float overshoot = OVERSHOOT_PENALTY * gradeOvershoot * gradeOvershoot + CROSS_OVERSHOOT_PENALTY * crossOvershoot * crossOvershoot;

        cost = distance * (1f + _config.RoadSlopePenalty * grade * grade + _config.RoadCrossSlopePenalty * cross * cross + overshoot) * valley;

        if (_config.SeaLevel > 0f && toHeight < _config.SeaLevel + _config.ShoreMargin)
            cost *= FORD_PENALTY;
    }

    private float TurnCost(int heading, int step)
    {
        if (heading == FREE_HEADING || _config.RoadTurnPenalty <= 0f)
            return 0f;

        float dot = Mathf.Clamp(Vector2.Dot(_headings[heading], _headings[step]), -1f, 1f);

        return Mathf.Acos(dot) * _config.RoadTurnPenalty;
    }

    private float TurnRadius(int heading, int step)
    {
        if (heading == FREE_HEADING)
            return float.MaxValue;

        float dot = Vector2.Dot(_headings[heading], _headings[step]);

        if (dot > 0.9999f)
            return float.MaxValue;

        if (dot <= 0f)
            return 0f;

        Vector2 before = -new Vector2(STEP_X[heading], STEP_Y[heading]) * _cellSize;
        Vector2 after = new Vector2(STEP_X[step], STEP_Y[step]) * _cellSize;

        return RoadSmoother.Circumradius(before, Vector2.zero, after);
    }

    private float CrossSlope(int fromX, int fromY, int toX, int toY, float deltaX, float deltaY, float distance)
    {
        float midX = (fromX + toX + 1f) * 0.5f * _cellSize;
        float midY = (fromY + toY + 1f) * 0.5f * _cellSize;
        float perpendicularX = -deltaY / distance;
        float perpendicularY = deltaX / distance;
        float probe = _config.RoadHalfWidth + _config.RoadShoulder;
        float left = _map.SampleWorldSmooth(midX - perpendicularX * probe, midY - perpendicularY * probe);
        float right = _map.SampleWorldSmooth(midX + perpendicularX * probe, midY + perpendicularY * probe);

        return Mathf.Abs(right - left) / (2f * probe);
    }
    private int HeadingOf(Vector2 direction)
    {
        int best = 0;
        float bestDot = float.MinValue;

        for (int i = 0; i < HEADINGS; i++)
        {
            float dot = Vector2.Dot(_headings[i], direction);

            if (dot <= bestDot)
                continue;

            bestDot = dot;
            best = i;
        }

        return best;
    }

    private float SampleMeters(int cellX, int cellY)
    {
        return _map.SampleWorld(new Vector3((cellX + 0.5f) * _cellSize, 0f, (cellY + 0.5f) * _cellSize));
    }

    private int CellOf(Vector2 position)
    {
        int x = Mathf.Clamp((int)(position.x / _cellSize), 0, _resolution - 1);
        int y = Mathf.Clamp((int)(position.y / _cellSize), 0, _resolution - 1);

        return y * _resolution + x;
    }

    private Vector2 CellCenter(int cell)
    {
        return new Vector2((cell % _resolution + 0.5f) * _cellSize, (cell / _resolution + 0.5f) * _cellSize);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 1; i < points.Length; i++)
            total += Vector2.Distance(points[i - 1], points[i]);

        return total;
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
    }

    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
    {
        t = 0f;
        u = 0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-8f)
            return false;

        Vector2 delta = c - a;

        t = (delta.x * s.y - delta.y * s.x) / denominator;
        u = (delta.x * r.y - delta.y * r.x) / denominator;

        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }
}
