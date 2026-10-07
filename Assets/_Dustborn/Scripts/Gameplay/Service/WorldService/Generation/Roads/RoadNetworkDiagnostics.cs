using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class RoadNetworkDiagnostics
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
    public const string ROAD_ON_WATER = "roadOnWater";

    public static readonly string[] HARD =
    {
        DISCONNECTED_SETTLEMENTS, ORPHAN_TILES, PORT_MISMATCH, DANGLING_PORTS, ISOLATED_TILES, ORPHAN_ROADS,
        UNEXPLAINED_CROSSINGS, HIGHWAY_IN_SETTLEMENT, ZERO_SEGMENTS, DUPLICATE_SEGMENTS, INVALID_COORDINATES, OUT_OF_BOUNDS,
        GRADE, CROSS_SLOPE, CURVE_RADIUS, POI_ON_ROAD, POI_OVERLAP, GATEWAY_MISALIGNED, UNUSED_GATEWAYS, DISTRICT_MIX, DIRT_NETWORK,
        ROAD_ON_WATER
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
    private const float WATER_STEP = 4f;
    private const float WATER_MARGIN = 0.3f;

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
        IReadOnlyList<PoiPlacement> placements, HeightMap prepared, HeightMap carved, WaterMap water)
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
        CheckGrades(config, roads, carved, water, report);
        CheckWater(roads, carved, water, report);
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
}
