using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class RoadNetworkReport
{
    private const int PROBLEM_CAP = 4000;

    private readonly List<(string Name, double Value)> _metrics = new();
    private readonly Dictionary<string, int> _metricIndex = new();

    public Dictionary<string, int> Violations { get; } = new();
    public List<(Vector2 Point, string Kind)> Problems { get; } = new();

    public IReadOnlyList<(string Name, double Value)> Metrics => _metrics;

    public void Metric(string name, double value)
    {
        if (_metricIndex.TryGetValue(name, out int index))
        {
            _metrics[index] = (name, value);
            return;
        }

        _metricIndex[name] = _metrics.Count;
        _metrics.Add((name, value));
    }

    public double Get(string name)
    {
        return _metricIndex.TryGetValue(name, out int index) ? _metrics[index].Value : double.NaN;
    }

    public void Declare(string name)
    {
        if (!Violations.ContainsKey(name))
            Violations[name] = 0;
    }

    public void Violation(string name, Vector2 point)
    {
        Violations.TryGetValue(name, out int count);
        Violations[name] = count + 1;

        if (Problems.Count < PROBLEM_CAP)
            Problems.Add((point, name));
    }

    public int Count(string name)
    {
        return Violations.TryGetValue(name, out int count) ? count : 0;
    }

    public int Hard
    {
        get
        {
            int total = 0;

            foreach (string name in RoadNetworkDiagnostics.HARD)
                total += Count(name);

            return total;
        }
    }

    public string ToText()
    {
        var text = new StringBuilder();

        foreach ((string name, double value) in _metrics)
            text.Append("  ").Append(name.PadRight(44)).Append(' ').AppendLine(Format(value));

        var names = new List<string>(Violations.Keys);
        names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
            text.Append("  violation ").Append(name.PadRight(34)).Append(' ').Append(Violations[name]).AppendLine(Array.IndexOf(RoadNetworkDiagnostics.HARD, name) >= 0 ? " (hard)" : string.Empty);

        return text.ToString();
    }

    private static string Format(double value)
    {
        return Math.Abs(value - Math.Round(value)) < 1e-9 && Math.Abs(value) < 1e12
            ? ((long)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public static class RoadNetworkDiagnostics
{
    public const string DISCONNECTED_SETTLEMENTS = "disconnectedSettlements";
    public const string ORPHAN_TILES = "orphanTiles";
    public const string PORT_MISMATCH = "portMismatch";
    public const string DANGLING_PORTS = "danglingPorts";
    public const string ISOLATED_TILES = "isolatedTiles";
    public const string ORPHAN_ROADS = "orphanRoadIslands";
    public const string UNEXPLAINED_CROSSINGS = "unexplainedCrossings";
    public const string HIGHWAY_IN_SETTLEMENT = "highwayInSettlement";
    public const string ZERO_SEGMENTS = "zeroLengthSegments";
    public const string DUPLICATE_SEGMENTS = "duplicateSegments";
    public const string INVALID_COORDINATES = "invalidCoordinates";
    public const string OUT_OF_BOUNDS = "outOfBounds";
    public const string GRADE = "gradeOverLimit";
    public const string CROSS_SLOPE = "crossSlopeOverLimit";
    public const string CURVE_RADIUS = "curveRadiusUnderLimit";
    public const string POI_ON_ROAD = "poiOnRoad";
    public const string POI_OVERLAP = "poiOverlap";
    public const string GATEWAY_MISALIGNED = "gatewayMisaligned";
    public const string UNUSED_GATEWAYS = "unusedGateways";
    public const string DISTRICT_MIX = "districtMix";
    public const string DIRT_NETWORK = "dirtNetwork";
    public const string PARALLEL_CORRIDORS = "parallelCorridors";
    public const string SHALLOW_JUNCTIONS = "shallowJunctions";
    public const string EMPTY_DIRT_SPURS = "emptyDirtSpurs";

    public static readonly string[] HARD =
    {
        DISCONNECTED_SETTLEMENTS, ORPHAN_TILES, PORT_MISMATCH, DANGLING_PORTS, ISOLATED_TILES, ORPHAN_ROADS,
        UNEXPLAINED_CROSSINGS, HIGHWAY_IN_SETTLEMENT, ZERO_SEGMENTS, DUPLICATE_SEGMENTS, INVALID_COORDINATES, OUT_OF_BOUNDS,
        GRADE, CROSS_SLOPE, CURVE_RADIUS, POI_ON_ROAD, POI_OVERLAP, GATEWAY_MISALIGNED, UNUSED_GATEWAYS, DISTRICT_MIX, DIRT_NETWORK
    };

    private const float NODE_WELD = 1.5f;
    private const float CONTACT = 0.5f;
    private const float GRID_CELL = 32f;
    private const float PARALLEL_STEP = 10f;
    private const float PARALLEL_GAP = 20f;
    private const float PARALLEL_DOT = 0.9f;
    private const int PARALLEL_RUN = 6;
    private const float NODE_CLEARANCE = 40f;
    private const float SETTLEMENT_STEP = 8f;
    private const float GRADE_STEP = 4f;
    private const float GRADE_WINDOW = 24f;
    private const float CROSS_STEP = 8f;
    private const float RADIUS_TOLERANCE = 0.95f;
    private const float GATEWAY_ALIGN_DEGREES = 3f;
    private const float FACING_DOT = 0.9f;
    private const float CURVE_WINDOW = 30f;
    private const int SELF_INDEX_GAP = 1;
    private const float SELF_EPSILON = 1e-3f;

    private sealed class SegmentGrid
    {
        private readonly Dictionary<long, List<int>> _buckets = new();
        private int[] _visited = new int[1024];
        private int _stamp;

        public readonly List<(int Road, int Index, Vector2 A, Vector2 B)> Segments = new();

        public void Add(int road, int index, Vector2 a, Vector2 b)
        {
            int id = Segments.Count;

            Segments.Add((road, index, a, b));

            if (_visited.Length <= id)
                Array.Resize(ref _visited, _visited.Length * 2);

            Visit(Vector2.Min(a, b), Vector2.Max(a, b), key =>
            {
                if (!_buckets.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    _buckets[key] = list;
                }

                list.Add(id);
            });
        }

        public void Query(Vector2 min, Vector2 max, List<int> result)
        {
            result.Clear();
            _stamp++;

            Visit(min, max, key =>
            {
                if (!_buckets.TryGetValue(key, out List<int> list))
                    return;

                foreach (int id in list)
                {
                    if (_visited[id] == _stamp)
                        continue;

                    _visited[id] = _stamp;
                    result.Add(id);
                }
            });
        }

        private static void Visit(Vector2 min, Vector2 max, Action<long> visit)
        {
            int minX = Mathf.FloorToInt(min.x / GRID_CELL);
            int maxX = Mathf.FloorToInt(max.x / GRID_CELL);
            int minY = Mathf.FloorToInt(min.y / GRID_CELL);
            int maxY = Mathf.FloorToInt(max.y / GRID_CELL);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                    visit(((long)y << 32) ^ (uint)x);
            }
        }
    }

    public static RoadNetworkReport Measure(WorldGenerationConfig config, RoadNetwork network, IReadOnlyList<SettlementLayout> layouts,
        IReadOnlyList<PoiPlacement> placements, HeightMap prepared, HeightMap carved)
    {
        var report = new RoadNetworkReport();

        foreach (string name in HARD)
            report.Declare(name);

        report.Declare(PARALLEL_CORRIDORS);
        report.Declare(SHALLOW_JUNCTIONS);
        report.Declare(EMPTY_DIRT_SPURS);

        var roads = new List<Road>(network.Roads.Count + network.Streets.Count);
        roads.AddRange(network.Roads);
        roads.AddRange(network.Streets);

        SegmentGrid grid = BuildGrid(roads);
        List<Vector2> nodes = DeclaredNodes(network, layouts);
        var connected = new int[roads.Count];

        for (int i = 0; i < connected.Length; i++)
            connected[i] = i;

        MeasureGraph(config, network, layouts, report);
        MeasureRoads(roads, report);
        CheckGeometry(config, roads, grid, report);
        CheckCrossings(roads, grid, nodes, connected, report);
        CheckParallel(roads, grid, nodes, report);
        CheckSettlementIntrusion(network, layouts, report);
        CheckConnectivity(roads, grid, network, connected, report);
        CheckGrades(config, roads, carved, report);
        CheckCrossSlopes(config, network.Roads, prepared, report);
        CheckCurvature(config, roads, report);
        CheckGateways(config, network, layouts, report);
        CheckTiles(config, layouts, report);
        CheckPois(config, roads, layouts, placements, report);

        return report;
    }

    private static SegmentGrid BuildGrid(List<Road> roads)
    {
        var grid = new SegmentGrid();

        for (int road = 0; road < roads.Count; road++)
        {
            Vector2[] points = roads[road].Points;

            if (points == null)
                continue;

            for (int i = 0; i < points.Length - 1; i++)
                grid.Add(road, i, points[i], points[i + 1]);
        }

        return grid;
    }

    private static List<Vector2> DeclaredNodes(RoadNetwork network, IReadOnlyList<SettlementLayout> layouts)
    {
        var nodes = new List<Vector2>();

        if (network.Graph != null)
        {
            foreach (RoadNode node in network.Graph.Nodes)
            {
                if (network.Graph.Degree(node.Id) > 0)
                    nodes.Add(node.Position);
            }
        }

        if (layouts == null)
            return nodes;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null)
                continue;

            nodes.AddRange(layout.StreetNodes);

            foreach (SettlementGateway gateway in layout.Gateways)
                nodes.Add(gateway.Port);
        }

        return nodes;
    }

    private static void MeasureGraph(WorldGenerationConfig config, RoadNetwork network, IReadOnlyList<SettlementLayout> layouts, RoadNetworkReport report)
    {
        int settlements = 0;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout != null && !layout.IsEmpty)
                settlements++;
        }

        report.Metric("settlements", settlements);
        report.Metric("regional.plannedLinks", network.RegionalLinks.Count);
        report.Metric("regional.plannedLinksPerSettlement", settlements == 0 ? 0.0 : network.RegionalLinks.Count / (double)settlements);

        RoadGraph graph = network.Graph;

        if (graph == null)
            return;

        var nodeKinds = new int[3];
        int degreeSum = 0;
        int degreeNodes = 0;
        int degreeMax = 0;
        var degreeHistogram = new int[6];

        foreach (RoadNode node in graph.Nodes)
        {
            int degree = graph.Degree(node.Id);

            if (degree == 0)
                continue;

            nodeKinds[(int)node.Kind]++;

            if (node.Kind == RoadNodeKind.Terminal)
                continue;

            degreeSum += degree;
            degreeNodes++;
            degreeMax = Mathf.Max(degreeMax, degree);
            degreeHistogram[Mathf.Min(5, degree)]++;
        }

        report.Metric("graph.nodes.gateway", nodeKinds[(int)RoadNodeKind.Gateway]);
        report.Metric("graph.nodes.junction", nodeKinds[(int)RoadNodeKind.Junction]);
        report.Metric("graph.nodes.terminal", nodeKinds[(int)RoadNodeKind.Terminal]);
        report.Metric("graph.degree.mean", degreeNodes == 0 ? 0.0 : degreeSum / (double)degreeNodes);
        report.Metric("graph.degree.max", degreeMax);

        for (int degree = 1; degree < degreeHistogram.Length; degree++)
            report.Metric($"graph.degree.{degree}{(degree == 5 ? "plus" : string.Empty)}", degreeHistogram[degree]);

        int highwayEdges = 0;
        int dirtEdges = 0;
        var highwayNodes = new HashSet<int>();

        foreach (RoadEdge edge in graph.Edges)
        {
            if (!edge.Alive)
                continue;

            if (edge.Kind == RoadKind.DirtAccess)
            {
                dirtEdges++;
                CheckDirtEdge(graph, edge, report);
                continue;
            }

            highwayEdges++;
            highwayNodes.Add(edge.From);
            highwayNodes.Add(edge.To);
        }

        graph.Components(edge => edge.Kind == RoadKind.Highway, out int components);

        report.Metric("graph.edges.highway", highwayEdges);
        report.Metric("graph.edges.dirt", dirtEdges);
        report.Metric("highway.fragments", components);
        report.Metric("highway.loops", highwayEdges - highwayNodes.Count + components);

        CheckSettlementConnectivity(graph, layouts, network, report);
        MeasureJunctionAngles(config, graph, report);
    }

    private static void CheckDirtEdge(RoadGraph graph, RoadEdge edge, RoadNetworkReport report)
    {
        int fromDegree = graph.Degree(edge.From);
        int toDegree = graph.Degree(edge.To);
        bool fromTerminal = graph.Nodes[edge.From].Kind == RoadNodeKind.Terminal && fromDegree == 1;
        bool toTerminal = graph.Nodes[edge.To].Kind == RoadNodeKind.Terminal && toDegree == 1;
        bool fromHighway = graph.Degree(edge.From, RoadKind.Highway) >= 2;
        bool toHighway = graph.Degree(edge.To, RoadKind.Highway) >= 2;

        if ((fromTerminal && toHighway) || (toTerminal && fromHighway))
            return;

        report.Violation(DIRT_NETWORK, edge.Points[0]);
    }

    private static void CheckSettlementConnectivity(RoadGraph graph, IReadOnlyList<SettlementLayout> layouts, RoadNetwork network, RoadNetworkReport report)
    {
        if (network.RegionalLinks.Count == 0)
            return;

        int[] labels = graph.Components(edge => edge.Kind == RoadKind.Highway, out int components);
        var parent = new int[components + layouts.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        var connected = new List<SettlementLayout>();

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            bool any = false;

            foreach (SettlementGateway gateway in layout.Gateways)
            {
                if (gateway.Node < 0 || labels[gateway.Node] < 0)
                    continue;

                RoadGraph.Union(parent, components + layout.Index, labels[gateway.Node]);
                any = true;
            }

            if (!any)
            {
                report.Violation(DISCONNECTED_SETTLEMENTS, layout.Origin);
                continue;
            }

            connected.Add(layout);
        }

        var groups = new Dictionary<int, int>();

        foreach (SettlementLayout layout in connected)
        {
            int root = RoadGraph.Find(parent, components + layout.Index);
            groups.TryGetValue(root, out int count);
            groups[root] = count + 1;
        }

        int main = -1;
        int mainCount = -1;

        foreach (KeyValuePair<int, int> pair in groups)
        {
            if (pair.Value > mainCount || (pair.Value == mainCount && pair.Key < main))
            {
                main = pair.Key;
                mainCount = pair.Value;
            }
        }

        foreach (SettlementLayout layout in connected)
        {
            if (RoadGraph.Find(parent, components + layout.Index) != main)
                report.Violation(DISCONNECTED_SETTLEMENTS, layout.Origin);
        }

        report.Metric("regional.connectedSettlements", mainCount < 0 ? 0 : mainCount);
        MeasureContracted(graph, report);
    }

    private static void MeasureContracted(RoadGraph graph, RoadNetworkReport report)
    {
        var ids = new Dictionary<int, int>();
        var parent = new List<int>();
        int edges = 0;

        int Id(int node)
        {
            RoadNode source = graph.Nodes[node];
            int key = source.Kind == RoadNodeKind.Gateway && source.Settlement >= 0 ? -1 - source.Settlement : node;

            if (ids.TryGetValue(key, out int id))
                return id;

            id = parent.Count;
            ids[key] = id;
            parent.Add(id);

            return id;
        }

        int Find(int node)
        {
            while (parent[node] != node)
                node = parent[node] = parent[parent[node]];

            return node;
        }

        foreach (RoadEdge edge in graph.Edges)
        {
            if (!edge.Alive || edge.Kind != RoadKind.Highway)
                continue;

            int from = Find(Id(edge.From));
            int to = Find(Id(edge.To));

            edges++;

            if (from != to)
                parent[Mathf.Max(from, to)] = Mathf.Min(from, to);
        }

        var roots = new HashSet<int>();

        for (int i = 0; i < parent.Count; i++)
            roots.Add(Find(i));

        report.Metric("regional.components", roots.Count);
        report.Metric("regional.loops", edges - parent.Count + roots.Count);
    }

    private static void MeasureJunctionAngles(WorldGenerationConfig config, RoadGraph graph, RoadNetworkReport report)
    {
        var angles = new List<float>();

        foreach (RoadNode node in graph.Nodes)
        {
            if (node.Kind != RoadNodeKind.Junction || graph.Degree(node.Id) < 3)
                continue;

            float smallest = 180f;
            var directions = new List<Vector2>();

            foreach (int id in node.Edges)
                directions.Add(graph.Edges[id].DirectionFrom(node.Id, 12f));

            for (int a = 0; a < directions.Count; a++)
            {
                for (int b = a + 1; b < directions.Count; b++)
                {
                    float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(directions[a], directions[b]), -1f, 1f)) * Mathf.Rad2Deg;
                    smallest = Mathf.Min(smallest, angle);
                }
            }

            angles.Add(smallest);

            if (smallest < config.JunctionAngle * 0.8f)
                report.Violation(SHALLOW_JUNCTIONS, node.Position);
        }

        Percentiles(report, "junction.minAngleDegrees", angles);
    }

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

    private static void CheckConnectivity(List<Road> roads, SegmentGrid grid, RoadNetwork network, int[] parent, RoadNetworkReport report)
    {
        var candidates = new List<int>();

        for (int road = 0; road < roads.Count; road++)
        {
            Vector2[] points = roads[road].Points;

            foreach (Vector2 end in new[] { points[0], points[^1] })
            {
                grid.Query(end - Vector2.one * CONTACT, end + Vector2.one * CONTACT, candidates);

                foreach (int id in candidates)
                {
                    (int other, int _, Vector2 a, Vector2 b) = grid.Segments[id];

                    if (other != road && DistanceSqr(end, a, b) <= CONTACT * CONTACT)
                        RoadGraph.Union(parent, road, other);
                }
            }
        }

        var roots = new HashSet<int>();

        for (int road = 0; road < roads.Count; road++)
            roots.Add(RoadGraph.Find(parent, road));

        report.Metric("roads.components", roots.Count);

        if (roots.Count <= 1 || network.RegionalLinks.Count == 0)
            return;

        var sizes = new Dictionary<int, int>();

        for (int road = 0; road < roads.Count; road++)
        {
            int root = RoadGraph.Find(parent, road);
            sizes.TryGetValue(root, out int size);
            sizes[root] = size + 1;
        }

        int main = -1;
        int mainSize = -1;

        foreach (KeyValuePair<int, int> pair in sizes)
        {
            if (pair.Value > mainSize || (pair.Value == mainSize && pair.Key < main))
            {
                main = pair.Key;
                mainSize = pair.Value;
            }
        }

        foreach (KeyValuePair<int, int> pair in sizes)
        {
            if (pair.Key != main)
                report.Violation(ORPHAN_ROADS, roads[pair.Key].Points[0]);
        }
    }

    private static void CheckGrades(WorldGenerationConfig config, List<Road> roads, HeightMap carved, RoadNetworkReport report)
    {
        if (carved == null)
            return;

        var grades = new Dictionary<RoadKind, List<float>>();
        int window = Mathf.RoundToInt(GRADE_WINDOW / GRADE_STEP);

        foreach (Road road in roads)
        {
            Vector2[] points = RoadSmoother.Resample(road.Points, GRADE_STEP);

            if (points.Length <= window)
                continue;

            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);

            if (!grades.TryGetValue(road.Kind, out List<float> list))
            {
                list = new List<float>();
                grades[road.Kind] = list;
            }

            var heights = new float[points.Length];
            var distance = new float[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                heights[i] = carved.SampleWorldSmooth(points[i].x, points[i].y);
                distance[i] = i == 0 ? 0f : distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }

            for (int i = window; i < points.Length; i++)
            {
                float span = distance[i] - distance[i - window];

                if (span < GRADE_WINDOW * 0.5f)
                    continue;

                float grade = Mathf.Abs(heights[i] - heights[i - window]) / span;
                list.Add(grade);

                if (grade > profile.MaxGrade)
                    report.Violation(GRADE, points[i]);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in grades)
            Percentiles(report, $"grade.{pair.Key}", pair.Value);
    }

    private static void CheckCrossSlopes(WorldGenerationConfig config, IReadOnlyList<Road> roads, HeightMap prepared, RoadNetworkReport report)
    {
        if (prepared == null)
            return;

        var slopes = new Dictionary<RoadKind, List<float>>();

        foreach (Road road in roads)
        {
            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);

            if (profile.MaxCrossSlope >= float.MaxValue)
                continue;

            if (!slopes.TryGetValue(road.Kind, out List<float> list))
            {
                list = new List<float>();
                slopes[road.Kind] = list;
            }

            float probe = profile.HalfWidth + profile.Shoulder;
            Vector2[] points = RoadSmoother.Resample(road.Points, CROSS_STEP);

            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
                Vector2 normal = new(-tangent.y, tangent.x);
                Vector2 left = points[i] - normal * probe;
                Vector2 right = points[i] + normal * probe;
                float slope = Mathf.Abs(prepared.SampleWorldSmooth(right.x, right.y) - prepared.SampleWorldSmooth(left.x, left.y)) / (2f * probe);

                list.Add(slope);

                if (slope > profile.MaxCrossSlope)
                    report.Violation(CROSS_SLOPE, points[i]);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in slopes)
            Percentiles(report, $"crossSlope.{pair.Key}", pair.Value);
    }

    private static void CheckCurvature(WorldGenerationConfig config, List<Road> roads, RoadNetworkReport report)
    {
        var turns = new Dictionary<RoadKind, List<float>>();
        var radii = new Dictionary<RoadKind, List<float>>();

        foreach (Road road in roads)
        {
            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);
            float spacing = RoadKindProfile.SampleSpacing(road.Kind);
            Vector2[] even = RoadSmoother.Resample(road.Points, spacing);

            if (!radii.TryGetValue(road.Kind, out List<float> radiusList))
            {
                radiusList = new List<float>();
                radii[road.Kind] = radiusList;
                turns[road.Kind] = new List<float>();
            }

            for (int i = 1; i < even.Length - 1; i++)
            {
                if (Vector2.Distance(even[i - 1], even[i]) < spacing * 0.5f || Vector2.Distance(even[i], even[i + 1]) < spacing * 0.5f)
                    continue;

                float radius = RoadSmoother.Circumradius(even[i - 1], even[i], even[i + 1]);

                if (radius >= float.MaxValue)
                    continue;

                radiusList.Add(radius);

                if (radius < profile.MinCurveRadius * RADIUS_TOLERANCE)
                    report.Violation(CURVE_RADIUS, even[i]);
            }

            Vector2[] coarse = RoadSmoother.Resample(road.Points, CURVE_WINDOW);

            for (int i = 1; i < coarse.Length - 1; i++)
            {
                Vector2 back = coarse[i] - coarse[i - 1];
                Vector2 forward = coarse[i + 1] - coarse[i];

                if (back.sqrMagnitude < 1f || forward.sqrMagnitude < 1f)
                    continue;

                turns[road.Kind].Add(Mathf.Acos(Mathf.Clamp(Vector2.Dot(back.normalized, forward.normalized), -1f, 1f)) * Mathf.Rad2Deg);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in turns)
            Percentiles(report, $"turnPer30m.{pair.Key}", pair.Value);

        foreach (KeyValuePair<RoadKind, List<float>> pair in radii)
        {
            pair.Value.Sort();
            report.Metric($"curveRadius.{pair.Key}.min", pair.Value.Count == 0 ? 0.0 : pair.Value[0]);
            report.Metric($"curveRadius.{pair.Key}.p05", pair.Value.Count == 0 ? 0.0 : pair.Value[(int)((pair.Value.Count - 1) * 0.05f)]);
        }
    }

    private static void CheckGateways(WorldGenerationConfig config, RoadNetwork network, IReadOnlyList<SettlementLayout> layouts, RoadNetworkReport report)
    {
        RoadGraph graph = network.Graph;
        int gateways = 0;
        int used = 0;
        float worst = 0f;

        if (graph == null)
            return;

        var byNode = new Dictionary<int, SettlementGateway>();
        var routeEnds = new Dictionary<int, Dictionary<int, int>>();

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null)
                continue;

            foreach (SettlementGateway gateway in layout.Gateways)
            {
                if (gateway.Node >= 0)
                    byNode[gateway.Node] = gateway;
            }
        }

        foreach (RoadEdge edge in graph.Edges)
        {
            if (!edge.Alive || edge.Kind != RoadKind.Highway)
                continue;

            if (!routeEnds.TryGetValue(edge.Route, out Dictionary<int, int> counts))
            {
                counts = new Dictionary<int, int>();
                routeEnds[edge.Route] = counts;
            }

            counts[edge.From] = counts.TryGetValue(edge.From, out int from) ? from + 1 : 1;
            counts[edge.To] = counts.TryGetValue(edge.To, out int to) ? to + 1 : 1;
        }

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null)
                continue;

            foreach (SettlementGateway gateway in layout.Gateways)
            {
                gateways++;

                if (gateway.Node < 0 || graph.Degree(gateway.Node, RoadKind.Highway) == 0)
                {
                    if (gateway.Neighbours.Count > 0 && network.RegionalLinks.Count > 0)
                        report.Violation(UNUSED_GATEWAYS, gateway.Port);

                    continue;
                }

                used++;

                foreach (int id in graph.Nodes[gateway.Node].Edges)
                {
                    RoadEdge edge = graph.Edges[id];

                    if (edge.Kind != RoadKind.Highway)
                        continue;

                    float deviation = ApproachDeviation(config, graph, edge, gateway, byNode, FarEnd(routeEnds, edge, gateway.Node));
                    worst = Mathf.Max(worst, deviation);

                    if (deviation > GATEWAY_ALIGN_DEGREES)
                        report.Violation(GATEWAY_MISALIGNED, gateway.Port);
                }
            }
        }

        report.Metric("gateways.total", gateways);
        report.Metric("gateways.withHighway", used);
        report.Metric("gateways.worstApproachDegrees", worst);
    }

    private static int FarEnd(Dictionary<int, Dictionary<int, int>> routeEnds, RoadEdge edge, int node)
    {
        if (!routeEnds.TryGetValue(edge.Route, out Dictionary<int, int> counts))
            return edge.Other(node);

        foreach (KeyValuePair<int, int> pair in counts)
        {
            if (pair.Value == 1 && pair.Key != node)
                return pair.Key;
        }

        return edge.Other(node);
    }

    private static float ApproachDeviation(WorldGenerationConfig config, RoadGraph graph, RoadEdge edge, SettlementGateway gateway,
        Dictionary<int, SettlementGateway> gateways, int other)
    {
        float worst = 0f;
        float step = 4f;
        bool forward = edge.From == gateway.Node;
        float limit = Mathf.Min(config.GatewayApproachLength, edge.Length);

        if (graph.Nodes[other].Kind == RoadNodeKind.Gateway && gateways.TryGetValue(other, out SettlementGateway facing)
            && Vector2.Dot(gateway.Tangent, facing.Tangent) <= -FACING_DOT)
        {
            float reach = RoadSmoother.FacingReach(gateway.Port, gateway.Tangent, facing.Port, facing.Tangent,
                config.GatewayApproachLength, config.HighwayMinCurveRadius, out List<Vector2> curve);

            if (curve != null)
                limit = Mathf.Min(limit, reach);
        }

        for (float along = step; along <= limit; along += step)
        {
            float from = forward ? along - step : edge.Length - along + step;
            float to = forward ? along : edge.Length - along;
            Vector2 direction = (edge.PointAt(to) - edge.PointAt(from)).normalized;
            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(direction, gateway.Tangent), -1f, 1f)) * Mathf.Rad2Deg;

            worst = Mathf.Max(worst, angle);
        }

        return worst;
    }

    private static void CheckTiles(WorldGenerationConfig config, IReadOnlyList<SettlementLayout> layouts, RoadNetworkReport report)
    {
        var typeCounts = new int[4];
        var typeTiles = new int[4];
        var districts = new int[4, 5];
        var shapes = new int[4, 6];
        int courts = 0;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            int type = (int)layout.Type;

            typeCounts[type]++;
            typeTiles[type] += layout.Tiles.Count;

            foreach (SettlementTile tile in layout.Tiles)
            {
                districts[type, (int)tile.District]++;
                shapes[type, (int)tile.Shape]++;

                CheckPorts(layout, tile, report);
            }

            CheckReachability(layout, report);
            CheckDistrictMix(config, layout, report);

            foreach (Road street in layout.Streets)
            {
                if (street.Points.Length == 2 && street.Kind == RoadKind.LocalStreet)
                    courts++;
            }
        }

        foreach (SettlementType type in new[] { SettlementType.City, SettlementType.Town, SettlementType.CountryTown, SettlementType.GhostTown })
        {
            int index = (int)type;

            report.Metric($"settlements.{type}.count", typeCounts[index]);
            report.Metric($"settlements.{type}.tiles", typeTiles[index]);

            foreach (DistrictType district in new[] { DistrictType.Downtown, DistrictType.Commercial, DistrictType.Industrial, DistrictType.Residential, DistrictType.Rural })
                report.Metric($"settlements.{type}.district.{district}", districts[index, (int)district]);

            foreach (TileShape shape in new[] { TileShape.Cap, TileShape.Straight, TileShape.Corner, TileShape.Tee, TileShape.Intersection })
                report.Metric($"settlements.{type}.shape.{shape}", shapes[index, (int)shape]);
        }

        report.Metric("settlements.straightTwoPointStreets", courts);
    }

    private static void CheckPorts(SettlementLayout layout, SettlementTile tile, RoadNetworkReport report)
    {
        if (tile.Ports == TilePorts.None)
            report.Violation(ISOLATED_TILES, tile.Center);

        foreach (TilePorts side in TilePortRules.SIDES)
        {
            SettlementTile neighbour = layout.Neighbour(tile, side);
            bool has = TilePortRules.Has(tile.Ports, side);

            if (neighbour == null)
            {
                if (has && !TilePortRules.Has(tile.GatewayPorts, side))
                    report.Violation(DANGLING_PORTS, layout.PortPoint(tile, side));

                continue;
            }

            if (has != TilePortRules.Has(neighbour.Ports, TilePortRules.Opposite(side)))
                report.Violation(PORT_MISMATCH, layout.PortPoint(tile, side));
        }
    }

    private static void CheckReachability(SettlementLayout layout, RoadNetworkReport report)
    {
        var reached = new HashSet<SettlementTile>();
        var queue = new Queue<SettlementTile>();

        foreach (SettlementGateway gateway in layout.Gateways)
        {
            if (reached.Add(gateway.Tile))
                queue.Enqueue(gateway.Tile);
        }

        if (layout.Gateways.Count == 0 && layout.Neighbours.Count == 0 && layout.Tiles.Count > 0 && reached.Add(layout.Tiles[0]))
            queue.Enqueue(layout.Tiles[0]);

        while (queue.Count > 0)
        {
            SettlementTile tile = queue.Dequeue();

            foreach (TilePorts side in TilePortRules.SIDES)
            {
                if (!TilePortRules.Has(tile.Ports, side))
                    continue;

                SettlementTile next = layout.Neighbour(tile, side);

                if (next != null && reached.Add(next))
                    queue.Enqueue(next);
            }
        }

        foreach (SettlementTile tile in layout.Tiles)
        {
            if (!reached.Contains(tile))
                report.Violation(ORPHAN_TILES, tile.Center);
        }
    }

    private static void CheckDistrictMix(WorldGenerationConfig config, SettlementLayout layout, RoadNetworkReport report)
    {
        SettlementTypeProfile profile = config.Profile(layout.Type);
        var counts = new int[5];

        foreach (SettlementTile tile in layout.Tiles)
            counts[(int)tile.District]++;

        Expect(report, layout, counts[(int)DistrictType.Downtown], profile.DowntownShare);
        Expect(report, layout, counts[(int)DistrictType.Commercial], profile.CommercialShare);
        Expect(report, layout, counts[(int)DistrictType.Industrial], profile.IndustrialShare);
    }

    private static void Expect(RoadNetworkReport report, SettlementLayout layout, int count, float share)
    {
        int tiles = layout.Tiles.Count;

        if (share <= 0f && count > 0)
        {
            report.Violation(DISTRICT_MIX, layout.Origin);
            return;
        }

        if (share > 0f && tiles >= 3 && count == 0)
            report.Violation(DISTRICT_MIX, layout.Origin);
    }

    private static void CheckPois(WorldGenerationConfig config, List<Road> roads, IReadOnlyList<SettlementLayout> layouts,
        IReadOnlyList<PoiPlacement> placements, RoadNetworkReport report)
    {
        if (placements == null)
            return;

        var proximity = new RoadProximity(roads, config.WorldSize, config.RoadCellSize);
        var index = new LotIndex(config.WorldSize, 48f);
        var districts = new int[5];

        foreach (PoiPlacement placement in placements)
        {
            districts[(int)placement.District]++;

            Vector2 forward = placement.Forward;
            Vector2 right = new(forward.y, -forward.x);
            bool onRoad = false;

            for (int i = 0; i <= 6 && !onRoad; i++)
            {
                for (int j = 0; j <= 6 && !onRoad; j++)
                {
                    Vector2 point = placement.Ground + right * ((i / 6f - 0.5f) * placement.Footprint.x) + forward * ((j / 6f - 0.5f) * placement.Footprint.y);

                    onRoad = proximity.IsWithin(point, 0f);
                }
            }

            if (onRoad)
                report.Violation(POI_ON_ROAD, placement.Ground);

            var footprint = new Lot(placement.Ground, placement.Footprint.x, placement.Footprint.y, forward, placement.District);

            if (index.Overlaps(footprint, 0.98f))
                report.Violation(POI_OVERLAP, placement.Ground);

            index.Add(footprint);
        }

        report.Metric("poi.total", placements.Count);

        foreach (DistrictType district in new[] { DistrictType.Downtown, DistrictType.Commercial, DistrictType.Industrial, DistrictType.Residential, DistrictType.Rural })
            report.Metric($"poi.{district}", districts[(int)district]);

        double frontage = 0.0;
        double covered = 0.0;
        int lots = 0;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null)
                continue;

            foreach (Frontage line in layout.Frontages)
                frontage += line.Length;

            foreach (Lot lot in layout.Lots)
                covered += lot.Width;

            lots += layout.Lots.Count;
        }

        report.Metric("poi.lots", lots);
        report.Metric("poi.frontageKm", frontage / 1000.0);
        report.Metric("poi.frontageCoverage", frontage <= 0.0 ? 0.0 : covered / frontage);
    }

    private static void Percentiles(RoadNetworkReport report, string name, List<float> values)
    {
        if (values.Count == 0)
            return;

        values.Sort();

        report.Metric($"{name}.p50", values[(int)((values.Count - 1) * 0.5f)]);
        report.Metric($"{name}.p90", values[(int)((values.Count - 1) * 0.9f)]);
        report.Metric($"{name}.p99", values[(int)((values.Count - 1) * 0.99f)]);
        report.Metric($"{name}.max", values[^1]);
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
}
