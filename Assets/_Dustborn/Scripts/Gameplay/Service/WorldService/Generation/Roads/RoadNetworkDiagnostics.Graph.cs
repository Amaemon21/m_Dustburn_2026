using System.Collections.Generic;
using UnityEngine;

public static partial class RoadNetworkDiagnostics
{
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
}
