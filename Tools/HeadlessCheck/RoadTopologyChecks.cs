using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

public static class RoadTopologyChecks
{
    private const string CONTENT = "../../Assets/_Dustborn/Content/World";
    private const long MEDIUM_BUDGET_MILLISECONDS = 120000;
    private const int PROBLEMS_SHOWN = 4;
    private const int PROBLEMS_TRACED = 2;

    private static readonly string[] BIOMES = { "Biome_PineForest", "Biome_BurntForest", "Biome_Desert", "Biome_Wasteland", "Biome_Snow" };

    private static readonly (string Name, int Size, int Cell, int[] Seeds)[] WORLDS =
    {
        ("small", 1024, 2, new[] { 1337, 424243, 90210 }),
        ("medium", 2048, 2, new[] { 1337, 777001, -5150 })
    };

    private sealed class WorldRun
    {
        public WorldGenerationConfig Config;
        public RoadNetwork Network;
        public List<SettlementLayout> Layouts;
        public List<PoiPlacement> Placements;
        public HeightMap Padded;
        public HeightMap Streets;
        public HeightMap Carved;
        public RoadPlanner Planner;
        public List<(Road Road, Vector2[] Points, float[] Profile, bool[] Anchored)> Profiles;
        public float[] Mask;
        public long Milliseconds;
    }

    public static void Run(PoiDatabase pois, string problemDirectory)
    {
        var failures = new List<string>();

        UnitChecks(failures);

        foreach ((string name, int size, int cell, int[] seeds) in WORLDS)
        {
            var fingerprints = new List<string>();

            foreach (int seed in seeds)
            {
                WorldRun first = Generate(name, size, cell, seed, pois);
                WorldRun second = Generate(name, size, cell, seed, pois);
                string print = Fingerprint(first);

                if (print != Fingerprint(second))
                    failures.Add($"{name} seed {seed}: two runs with identical inputs produced different topology, roads, lots or POI");

                if (fingerprints.Contains(print))
                    failures.Add($"{name} seed {seed}: a different seed reproduced an earlier world");

                fingerprints.Add(print);

                RoadNetworkReport report = RoadNetworkDiagnostics.Measure(first.Config, first.Network, first.Layouts, first.Placements, first.Padded, first.Carved);

                Summarise(name, seed, first, report);
                Plausible(name, seed, first, report, failures);

                if (report.Hard > 0)
                {
                    failures.Add($"{name} seed {seed}: {report.Hard} hard violations: {Violations(report)}");
                    ShowProblems(name, seed, first, report, problemDirectory);
                }

                if (name == "medium" && first.Milliseconds > MEDIUM_BUDGET_MILLISECONDS)
                    failures.Add($"{name} seed {seed}: generation took {first.Milliseconds} ms, over the {MEDIUM_BUDGET_MILLISECONDS} ms budget");

                if (seed == seeds[0])
                    Draw.View($"road_topology_{name}.png", first.Carved, first.Network, first.Layouts, first.Placements,
                        new Vector2(size * 0.5f, size * 0.5f), size, 1024, size <= 1024, report);
            }
        }

        if (failures.Count > 0)
        {
            foreach (string failure in failures)
                Console.WriteLine("FAIL: " + failure);

            throw new InvalidOperationException($"{failures.Count} road topology checks failed.");
        }

        Console.WriteLine("PASS: road topology on small and medium worlds over three seeds each, deterministic, zero hard violations");
    }

    private static WorldRun Generate(string name, int size, int cell, int seed, PoiDatabase pois)
    {
        WorldGenerationConfig config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        BiomeDatabase biomes = AssetReader.LoadBiomes($"{CONTENT}/Biomes", BIOMES);

        Configure(config, name, size, cell, seed);

        var clock = Stopwatch.StartNew();
        BiomeMap biomeMap = new BiomeMapGenerator(config, biomes).Generate();
        HeightMap raw = new HeightMapGenerator(config, biomes).Generate(biomeMap);

        var network = new RoadNetwork();
        network.Hubs.AddRange(new HubPlacer(config, raw).Place());
        network.SetPlan(new RegionalGraphPlanner(config, raw).Plan(network.Hubs));

        var planner = new SettlementPlanner(config, pois, raw);
        List<SettlementLayout> layouts = planner.Plan(network.Hubs, network.RegionalLinks);
        network.Publish(config, layouts);

        var carver = new TerrainCarver(config) { Profiles = new List<(Road Road, Vector2[] Points, float[] Profile, bool[] Anchored)>() };
        HeightMap padded = carver.CarveSettlements(raw, layouts);
        var roadPlanner = new RoadPlanner(config, padded);
        network.Graph = roadPlanner.Plan(network.Hubs, network.RegionalLinks, layouts);
        network.RuralSites.AddRange(new DirtAccessPlanner(config, padded).Plan(network.Graph, layouts, network.Streets));
        network.Publish(config, layouts);

        HeightMap streets = carver.CarveStreets(padded, layouts, out float[] mask);
        HeightMap carved = carver.CarveHighways(streets, network.Roads, mask);

        var proximity = new RoadProximity(network.Roads, config.WorldSize, config.RoadCellSize);
        proximity.AddRange(network.Streets);
        planner.CutLots(layouts, proximity);

        var placer = new PoiPlacer(config, pois, carved, proximity);
        List<PoiPlacement> placements = placer.Place(layouts, network);
        HeightMap final = carver.CarvePads(carved, placements);
        placer.ApplyHeights(final);

        clock.Stop();

        return new WorldRun
        {
            Config = config,
            Network = network,
            Layouts = layouts,
            Placements = placements,
            Padded = padded,
            Streets = streets,
            Carved = carved,
            Planner = roadPlanner,
            Profiles = carver.Profiles,
            Mask = mask,
            Milliseconds = clock.ElapsedMilliseconds
        };
    }

    private static void Configure(WorldGenerationConfig config, string name, int size, int cell, int seed)
    {
        float scale = size / 8192f;

        Set(config, "WorldSize", size);
        Set(config, "HeightCellSize", cell);
        Set(config, "Seed", seed);
        Set(config, "ErosionPasses", 8);
        Set(config, "HydraulicPasses", 16);

        foreach (string frequency in new[] { "ContinentFrequency", "HillFrequency", "RidgeFrequency", "DuneFrequency", "DetailFrequency", "MountainMaskFrequency" })
            Set(config, frequency, (float)Get(config, frequency) * scale);

        bool small = name == "small";

        Set(config, "TileSize", small ? 100f : 150f);
        Profile(config.CityProfile, small ? 0 : 1, 8, 12, 0.4f);
        Profile(config.TownProfile, small ? 1 : 2, small ? 2 : 3, small ? 4 : 8, small ? 0.3f : 0.4f);
        Profile(config.CountryTownProfile, small ? 2 : 3, small ? 1 : 2, small ? 3 : 4, small ? 0.3f : 0.4f);
        Profile(config.GhostTownProfile, small ? 3 : 4, 1, small ? 2 : 3, small ? 0.3f : 0.4f);

        Set(config, "HubEdgeMargin", 40f);
        Set(config, "SettlementGap", small ? 60f : 100f);
        Set(config, "SiteBuildableShare", 0.3f);
        Set(config, "DirtSettlementClearance", small ? 80f : 120f);
        Set(config, "DirtSpacing", small ? 140f : 200f);
        Set(config, "LoopSpacing", small ? 300f : 600f);
        Set(config, "MaxLinkLength", small ? 1200f : 2200f);
    }

    private static void Profile(SettlementTypeProfile profile, int count, int minTiles, int maxTiles, float spacingScale)
    {
        Set(profile, "Count", count);
        Set(profile, "MinTiles", minTiles);
        Set(profile, "MaxTiles", maxTiles);
        Set(profile, "Spacing", profile.Spacing * spacingScale);
    }

    private static void ShowProblems(string name, int seed, WorldRun run, RoadNetworkReport report, string problemDirectory)
    {
        var shown = new Dictionary<string, int>();

        foreach ((Vector2 point, string kind) in report.Problems)
        {
            if (Array.IndexOf(RoadNetworkDiagnostics.HARD, kind) < 0)
                continue;

            shown.TryGetValue(kind, out int count);

            if (count >= PROBLEMS_SHOWN)
                continue;

            shown[kind] = count + 1;
            Console.WriteLine($"  {name} seed {seed}: {kind} at ({point.x:F0}, {point.y:F0})");

            if (problemDirectory == null || count >= PROBLEMS_TRACED)
                continue;

            if (kind == RoadNetworkDiagnostics.GRADE)
                Harness.TraceGrade(run.Profiles, run.Streets, run.Carved, point);

            if (kind == RoadNetworkDiagnostics.CURVE_RADIUS || kind == RoadNetworkDiagnostics.GATEWAY_MISALIGNED || kind == RoadNetworkDiagnostics.CROSS_SLOPE)
                Harness.DescribeRoute(run.Network, run.Planner, point);

            if (kind == RoadNetworkDiagnostics.CURVE_RADIUS)
                Harness.TraceCurve(run.Network, point);

            if (kind == RoadNetworkDiagnostics.GATEWAY_MISALIGNED)
                Harness.DescribeGateway(run.Network, run.Layouts, point);

            if (kind == RoadNetworkDiagnostics.UNEXPLAINED_CROSSINGS)
                Harness.DescribeCrossing(run.Network, run.Layouts, point);

            System.IO.Directory.CreateDirectory(problemDirectory);
            Draw.View(System.IO.Path.Combine(problemDirectory, $"{name}_{seed}_{kind}_{count}.png"), run.Carved, run.Network, run.Layouts, run.Placements,
                point, 360f, 800, false, report);
        }
    }

    private static void Summarise(string name, int seed, WorldRun run, RoadNetworkReport report)
    {
        var text = new StringBuilder();

        text.Append($"{name} seed {seed}: {run.Milliseconds} ms, settlements {report.Get("settlements")}")
            .Append($" (cities {report.Get("settlements.City.count")}, towns {report.Get("settlements.Town.count")}, country {report.Get("settlements.CountryTown.count")}, ghost {report.Get("settlements.GhostTown.count")})")
            .Append($", planned links per settlement {report.Get("regional.plannedLinksPerSettlement"):0.00}")
            .Append($", highways {report.Get("roads.Highway.count")} / {report.Get("roads.Highway.km"):0.0} km")
            .Append($", arterials {report.Get("roads.Arterial.count")}, streets {report.Get("roads.LocalStreet.count")}, dirt {report.Get("roads.DirtAccess.count")}")
            .Append($", junctions {Value(report, "graph.nodes.junction")}, loops {Value(report, "regional.loops")}, components {Value(report, "regional.components")}")
            .Append($", POI {report.Get("poi.total")}, parallel runs {report.Count(RoadNetworkDiagnostics.PARALLEL_CORRIDORS)}, hard {report.Hard}");

        Console.WriteLine(text.ToString());
    }

    private static double Value(RoadNetworkReport report, string key)
    {
        double value = report.Get(key);

        return double.IsNaN(value) ? 0.0 : value;
    }

    private static void Plausible(string name, int seed, WorldRun run, RoadNetworkReport report, List<string> failures)
    {
        string prefix = $"{name} seed {seed}: ";
        double settlements = report.Get("settlements");

        if (settlements < 2)
        {
            failures.Add(prefix + "fewer than two settlements were planned");
            return;
        }

        if (report.Get("regional.components") != 1)
            failures.Add(prefix + $"the highway network falls into {report.Get("regional.components")} components");

        double ratio = report.Get("regional.plannedLinksPerSettlement");

        if (ratio < (settlements - 1) / settlements - 1e-6 || ratio > run.Config.TargetLinkRatio + 1e-6)
            failures.Add(prefix + $"planned link ratio {ratio:0.00} is outside the backbone to TargetLinkRatio range");

        if (report.Get("roads.Highway.count") <= 0 || report.Get("roads.Arterial.count") <= 0)
            failures.Add(prefix + "no highway or arterial was produced");

        if (report.Get("poi.total") <= 0)
            failures.Add(prefix + "no POI was placed");

        foreach (SettlementLayout layout in run.Layouts)
        {
            if (layout.IsEmpty)
                continue;

            SettlementTypeProfile profile = run.Config.Profile(layout.Type);

            if (layout.Tiles.Count > profile.HighTiles + 2 * profile.MaxGateways + 2)
                failures.Add(prefix + $"{layout.Type} at {layout.Origin} grew to {layout.Tiles.Count} tiles, far past its profile");

            if (profile.Shape == SettlementShape.Linear)
            {
                foreach (SettlementTile tile in layout.Tiles)
                {
                    if (tile.J != layout.Tiles[0].J)
                        failures.Add(prefix + $"linear {layout.Type} at {layout.Origin} left its main street");
                }
            }
        }
    }

    private static string Violations(RoadNetworkReport report)
    {
        var parts = new List<string>();

        foreach (string name in RoadNetworkDiagnostics.HARD)
        {
            if (report.Count(name) > 0)
                parts.Add($"{name} {report.Count(name)}");
        }

        return string.Join(", ", parts);
    }

    private static string Fingerprint(WorldRun run)
    {
        var text = new StringBuilder();

        foreach (Road road in run.Network.Paved())
        {
            text.Append((int)road.Kind).Append(':');

            foreach (Vector2 point in road.Points)
                text.Append(point.x.ToString("R")).Append(',').Append(point.y.ToString("R")).Append(';');

            text.Append('\n');
        }

        foreach (SettlementLayout layout in run.Layouts)
        {
            text.Append((int)layout.Type).Append('@').Append(layout.Origin.x.ToString("R")).Append(',').Append(layout.Angle.ToString("R")).Append('\n');

            foreach (SettlementTile tile in layout.Tiles)
                text.Append(tile.I).Append(',').Append(tile.J).Append(',').Append((int)tile.Ports).Append(',').Append((int)tile.District).Append(';');

            foreach (Lot lot in layout.Lots)
                text.Append(lot.Center.x.ToString("R")).Append(',').Append(lot.Center.y.ToString("R")).Append(',').Append(lot.Width.ToString("R")).Append(';');

            text.Append('\n');
        }

        foreach (PoiPlacement placement in run.Placements)
            text.Append(placement.Prefab.name).Append(',').Append(placement.Position.x.ToString("R")).Append(',').Append(placement.Position.y.ToString("R"))
                .Append(',').Append(placement.Position.z.ToString("R")).Append(',').Append(placement.Rotation.ToString("R")).Append('\n');

        using var sha = System.Security.Cryptography.SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void UnitChecks(List<string> failures)
    {
        CheckShapes(failures);
        CheckDelaunay(failures);
        CheckGraph(failures);
        CheckRadius(failures);
        CheckSolver(failures);
        Console.WriteLine($"unit checks done, {failures.Count} failures so far");
    }

    private static void CheckShapes(List<string> failures)
    {
        var expected = new Dictionary<TileShape, int> { [TileShape.Empty] = 1, [TileShape.Cap] = 4, [TileShape.Straight] = 2, [TileShape.Corner] = 4, [TileShape.Tee] = 4, [TileShape.Intersection] = 1 };
        var counts = new Dictionary<TileShape, int>();

        for (int mask = 0; mask < 16; mask++)
        {
            TileShape shape = TilePortRules.ShapeOf((TilePorts)mask);
            counts.TryGetValue(shape, out int count);
            counts[shape] = count + 1;
        }

        foreach (KeyValuePair<TileShape, int> pair in expected)
        {
            if (!counts.TryGetValue(pair.Key, out int count) || count != pair.Value)
                failures.Add($"tile shape {pair.Key} should cover {pair.Value} port masks");
        }
    }

    private static void CheckDelaunay(List<string> failures)
    {
        var random = new Unity.Mathematics.Random(99u);
        var points = new List<Vector2>();

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 6; x++)
                points.Add(new Vector2(x * 300f + random.NextFloat(-90f, 90f), y * 300f + random.NextFloat(-90f, 90f)));
        }

        List<(int From, int To)> edges = Delaunay.Edges(points);

        if (edges.Count < points.Count - 1 || edges.Count > 3 * points.Count - 6)
            failures.Add($"Delaunay produced {edges.Count} edges for {points.Count} points");

        var parent = new int[points.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        foreach ((int from, int to) in edges)
            RoadGraph.Union(parent, from, to);

        for (int i = 1; i < parent.Length; i++)
        {
            if (RoadGraph.Find(parent, i) != RoadGraph.Find(parent, 0))
            {
                failures.Add("Delaunay edges do not connect every point");
                break;
            }
        }

        for (int a = 0; a < edges.Count; a++)
        {
            for (int b = a + 1; b < edges.Count; b++)
            {
                (int p, int q) = edges[a];
                (int r, int s) = edges[b];

                if (p == r || p == s || q == r || q == s)
                    continue;

                if (Crosses(points[p], points[q], points[r], points[s]))
                {
                    failures.Add("Delaunay edges cross each other");
                    return;
                }
            }
        }
    }

    private static void CheckGraph(List<string> failures)
    {
        var graph = new RoadGraph();
        int a = graph.AddNode(new Vector2(0f, 0f), RoadNodeKind.Gateway);
        int b = graph.AddNode(new Vector2(100f, 0f), RoadNodeKind.Gateway);
        var points = new List<Vector2>();

        for (int i = 0; i <= 10; i++)
            points.Add(new Vector2(i * 10f, 0f));

        int route = graph.NewRoute();
        int edge = graph.AddEdge(a, b, points, RoadKind.Highway, route);
        int junction = graph.Split(edge, 37f);
        float along = 60f;
        int resolved = graph.Resolve(edge, ref along);

        if (graph.Degree(junction) != 2 || graph.Degree(a) != 1 || graph.Degree(b) != 1)
            failures.Add("splitting an edge must leave degree 2 at the new junction and 1 at both ends");

        if (Mathf.Abs(along - 23f) > 1e-3f || graph.Edges[resolved].To != b)
            failures.Add("an arclength on a split edge must resolve onto the right half");

        int c = graph.AddNode(new Vector2(37f, 50f), RoadNodeKind.Terminal);
        graph.AddEdge(junction, c, new[] { new Vector2(37f, 0f), new Vector2(37f, 50f) }, RoadKind.DirtAccess, graph.NewRoute());

        var config = new WorldGenerationConfig();
        List<Road> roads = graph.BuildRoads(config);
        Road highway = roads.Find(road => road.Kind == RoadKind.Highway);

        if (roads.Count != 2 || highway == null || highway.Points.Length != 12 || graph.Degree(junction) != 3)
            failures.Add("a split highway must publish as one road through its junction, with the branch separate");

        graph.Components(null, out int components);

        if (components != 1)
            failures.Add("the split graph must stay one component");
    }

    private static void CheckRadius(List<string> failures)
    {
        var corner = new[] { new Vector2(0f, 0f), new Vector2(300f, 0f), new Vector2(300f, 300f) };
        Vector2[] even = RoadSmoother.Resample(corner, 12f);
        Vector2[] relaxed = RoadSmoother.EnforceRadius(even, 70f, 0f, 0f, 2000);
        float radius = RoadSmoother.MinRadius(relaxed, 12f, 24f, 24f);

        if (radius < 70f * 0.9f)
            failures.Add($"radius enforcement left a {radius:0.0} m curve against a 70 m limit");

        var arc = new List<Vector2> { new(0f, 0f), new(100f, 0f), new(100f, 100f) };
        Vector2[] fillet = TileStreetBuilder.Fillet(arc, 16f);
        float filletRadius = RoadSmoother.MinRadius(fillet, 4f, 0f, 0f);

        if (Mathf.Abs(filletRadius - 16f) > 1.5f)
            failures.Add($"a 16 m street fillet measured {filletRadius:0.0} m");
    }

    private static void CheckSolver(List<string> failures)
    {
        var config = new WorldGenerationConfig();
        var map = new HeightMap(129, 1024, 384f);

        Array.Fill(map.Heights, 0.5f);

        var hub = new Hub(new Vector2(512f, 512f), 200f, 0f, 0, SettlementType.City);
        var layout = new SettlementLayout(hub, 0, 0.2f, 100f);

        for (int j = -2; j <= 1; j++)
        {
            for (int i = -2; i <= 2; i++)
                layout.AddTile(i, j);
        }

        for (int i = -2; i < 2; i++)
        {
            layout.TileAt(i, 0).ArterialPorts |= TilePorts.East;
            layout.TileAt(i + 1, 0).ArterialPorts |= TilePorts.West;
        }

        layout.TileAt(2, 0).GatewayPorts = TilePorts.East;
        layout.TileAt(2, 0).ArterialPorts |= TilePorts.East;
        layout.TileAt(-1, 1).District = DistrictType.Downtown;

        var random = new Unity.Mathematics.Random(5u);
        new TileTopologySolver(config, map).Solve(layout, config.CityProfile, ref random);

        foreach (SettlementTile tile in layout.Tiles)
        {
            if (tile.Ports == TilePorts.None)
                failures.Add($"solver left tile {tile.I},{tile.J} without ports");

            foreach (TilePorts side in TilePortRules.SIDES)
            {
                SettlementTile neighbour = layout.Neighbour(tile, side);
                bool has = TilePortRules.Has(tile.Ports, side);

                if (neighbour == null && has && !TilePortRules.Has(tile.GatewayPorts, side))
                    failures.Add($"solver left a dangling port on {tile.I},{tile.J}");

                if (neighbour != null && has != TilePortRules.Has(neighbour.Ports, TilePortRules.Opposite(side)))
                    failures.Add($"solver ports disagree between {tile.I},{tile.J} and its {side} neighbour");
            }

            if (tile.Hops == int.MaxValue)
                failures.Add($"solver left tile {tile.I},{tile.J} unreachable from the gateway");
        }

        if (!TilePortRules.Has(layout.TileAt(0, 0).Ports, TilePorts.East) || !TilePortRules.Has(layout.TileAt(0, 0).Ports, TilePorts.West))
            failures.Add("solver dropped a required arterial port");

        layout.Measure();
        new TileStreetBuilder(config).Build(layout, config.CityProfile, ref random);

        if (layout.Streets.Count == 0 || layout.Frontages.Count == 0)
            failures.Add("tile templates produced no streets or frontages");

        foreach (Road street in layout.Streets)
        {
            if (!IsNode(layout, street.Points[0]) || !IsNode(layout, street.Points[^1]))
                failures.Add("a street ends away from a declared street node");
        }
    }

    private static bool IsNode(SettlementLayout layout, Vector2 point)
    {
        foreach (Vector2 node in layout.StreetNodes)
        {
            if ((node - point).sqrMagnitude < 0.01f)
                return true;
        }

        return false;
    }

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = (d.x - c.x) * (a.y - c.y) - (d.y - c.y) * (a.x - c.x);
        float d2 = (d.x - c.x) * (b.y - c.y) - (d.y - c.y) * (b.x - c.x);
        float d3 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        float d4 = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);

        return ((d1 > 1e-3f && d2 < -1e-3f) || (d1 < -1e-3f && d2 > 1e-3f)) && ((d3 > 1e-3f && d4 < -1e-3f) || (d3 < -1e-3f && d4 > 1e-3f));
    }

    private static object Get(object target, string property)
    {
        return target.GetType().GetProperty(property).GetValue(target);
    }

    private static void Set(object target, string property, object value)
    {
        target.GetType().GetProperty(property).SetValue(target, value);
    }
}
