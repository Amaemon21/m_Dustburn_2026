using System.Collections.Generic;
using UnityEngine;

public static partial class RoadNetworkDiagnostics
{
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
}
