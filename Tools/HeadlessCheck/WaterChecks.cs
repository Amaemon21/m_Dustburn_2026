using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

static class WaterChecks
{
    private const int WORLD = 2048;
    private const float MAX_HEIGHT = 384f;
    private const float EPSILON = 1e-3f;
    private const int QUERY_SKIP = 16;

    private static int _world = WORLD;

    public static void Run(WorldGenerationConfig source, bool stamped = false)
    {
        if (!stamped)
            CheckDryCrossing();

        CheckClimate(Config(source, WORLD), stamped, false);
        CheckClimate(Config(source, WORLD), stamped, true);

        Run(Config(source, WORLD), stamped, WORLD, Terrain, stamped ? "Вода со штампами" : "Вода");
    }

    public static WaterAudit.Result Run(WorldGenerationConfig config, bool stamped, int world, Func<HeightMap> terrain, string label, float hangLimit = 1.5f)
    {
        _world = world;
        WaterStampLoader.Attach(config, stamped);

        var clock = Stopwatch.StartNew();
        HeightMap first = WorldMapPipeline.Hydrate(config, terrain(), out WaterMap water, out Hydrology hydrology);
        double millis = clock.Elapsed.TotalMilliseconds;

        WorldMapPipeline.Hydrate(config, terrain(), out WaterMap repeat, out _);

        if (config.Water.ThroughRiver)
            CheckMainRiver(config, hydrology, water, world);

        CheckCoast(config, water);
        CheckOffshore(config, water);
        CheckWaterLod(water);

        string inspect = Environment.GetEnvironmentVariable("WATER_AT");

        if (!string.IsNullOrEmpty(inspect))
        {
            foreach (string spot in inspect.Split(';'))
            {
                string[] xz = spot.Split(',');
                WaterAudit.Inspect(first, water, float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
            }

            return null;
        }

        Console.WriteLine($"{label}: синтетический мир {world} м, seed {config.Seed}: {water.Rivers.Count} рек, {hydrology.Lakes} озёр, {hydrology.Ponds} прудов, "
            + $"{hydrology.Basins.Count} замкнутых впадин, {millis:0} мс");

        if (stamped)
            WaterStampChecks.Report(water, first, repeat);

        Console.WriteLine($"  отпечаток: вода {Fingerprint(WaterMapFormat.Write(water))}, рельеф {Fingerprint(Bytes(first.Heights))}");

        CheckRivers(water, first);
        CheckBodies(water, first);
        CheckFinite(water);
        CheckDeterminism(water, repeat);
        CheckRoundTrip(water);
        CheckMeshes(water);
        CheckQueries(water, first);
        WaterAudit.Result audit = CheckArtifacts(first, water, hangLimit);
        HeightMap coasted = terrain();
        CoastShaper.Shape(coasted, config);
        CheckShore(first, coasted, water, repeat, config.Water.RiverBankWidth);
        StampChecks.Expect(WaterAudit.LooseEnds(water, config.Water.RiverStartArea * 1e6f) == 0, $"{WaterAudit.LooseEnds(water, config.Water.RiverStartArea * 1e6f)} river ends start or stop outside a lake, a pond, the sea, another river or the map border");
        Console.WriteLine(WaterAudit.DescribeEnds(water, config.Water.RiverStartArea * 1e6f));
        CheckTopology(water);

        WaterContract contract = WaterContract.Check(water, config.Water.RiverStartArea * 1e6f, !CoastShaper.Active(config));
        Console.WriteLine(contract.Describe());
        StampChecks.Expect(contract.Violations.Count == 0, contract.Violations.Count == 0 ? "" : $"river graph violation {contract.Violations[0]}");

        StampChecks.Finish(label, "реки текут только вниз, расход и ширина вниз по течению не убывают, озёра плоские и выше дна, без NaN, внутри мира, одинаковый seed даёт одинаковую воду и меши, сериализация без потерь; берега без сетки гидрологии, обрывков, дырок и T-стыков, устья без сухих разрывов, сечения рек без обрывов; каждая река начинается и кончается в водоёме, другой реке или у края карты; реки не пересекаются, сливаются вниз по течению и не бегут рядом.");

        return audit;
    }

    private static void CheckTopology(WaterMap water)
    {
        RiverTopologyAudit.Result topology = RiverTopologyAudit.Measure(water);

        Console.WriteLine(topology.Describe());
        StampChecks.Expect(topology.Crossings == 0, $"{topology.Crossings} river pairs cross away from a confluence: {string.Join(" ", topology.CrossingExamples)}");
        StampChecks.Expect(topology.SideOutflows == 0, $"{topology.SideOutflows} rivers start from the side of another river: {string.Join(" ", topology.OutflowExamples)}");
        StampChecks.Expect(topology.UpstreamJoins == 0, $"{topology.UpstreamJoins} tributaries join pointing upstream: {string.Join(" ", topology.UpstreamExamples)}");
        StampChecks.Expect(topology.NarrowingJoins == 0, $"{topology.NarrowingJoins} trunks narrow below a confluence: {string.Join(" ", topology.NarrowExamples)}");
        StampChecks.Expect(topology.SideRuns == 0, $"{topology.SideRuns} river runs lie beside another river: {string.Join(" ", topology.SideExamples)}");
    }

    private static void CheckDryCrossing()
    {
        var config = new WorldGenerationConfig();

        (HeightMap culvertMap, WaterMap _, WaterCrossing culvert) = Crossing(config, 3f);
        float road = Lowest(culvertMap, 119, 137);

        StampChecks.Expect(!culvert.Bridge, "A 3 m river must be crossed by a culvert");
        StampChecks.Expect(road > 100f + TerrainCarver.CULVERT_CLEARANCE - 0.3f, $"The road is submerged at a culvert: {road:0.00} m");
        StampChecks.Expect(culvert.Span > 2f * culvert.RoadWidth && culvert.Span < 80f, $"The culvert pipe does not span the embankment: {culvert.Span:0.0} m");
        StampChecks.Expect(Vector2.Distance(culvert.Center, new Vector2(128f, 128f)) < culvert.Span * 0.25f, "The culvert pipe is not centred on the road");

        (HeightMap bridgeMap, WaterMap _, WaterCrossing bridge) = Crossing(config, 20f);
        float channel = Lowest(bridgeMap, 124, 132);
        float abutment = Mathf.Min(bridgeMap.SampleWorldSmooth(128f - bridge.Span * 0.5f - 2f, 128f), bridgeMap.SampleWorldSmooth(128f + bridge.Span * 0.5f + 2f, 128f));
        BridgeMesh mesh = BridgeMesh.Build(bridge);

        StampChecks.Expect(bridge.Bridge, "A 20 m river must be crossed by a bridge");
        StampChecks.Expect(channel < 100f, $"The channel under a bridge is filled: {channel:0.00} m");
        StampChecks.Expect(bridge.Span >= 20f && bridge.Span < 80f, $"The bridge does not span the channel: {bridge.Span:0.0} m");
        StampChecks.Expect(Mathf.Abs(bridge.DeckHeight - abutment) < 0.6f, $"The bridge deck at {bridge.DeckHeight:0.00} m misses its abutments at {abutment:0.00} m");
        StampChecks.Expect(bridge.DeckHeight > 100f + TerrainCarver.BRIDGE_CLEARANCE - 0.6f, $"The bridge deck is too low: {bridge.DeckHeight:0.00} m");
        StampChecks.Expect(mesh.Piers >= 1 && mesh.Triangles.Count > 0, "A 20 m bridge needs a pier");

        (HeightMap obliqueMap, WaterMap obliqueWater, WaterCrossing oblique) = Crossing(config, 20f, true);
        Vector2 diagonal = new Vector2(1f, 1f).normalized;
        Vector2 edgeOffset = new Vector2(-diagonal.y, diagonal.x) * RoadKindProfile.For(config, RoadKind.Highway).HalfWidth;
        int edgeInRiver = 0, edgeSamples = 0;

        for (float along = 0f; along < 250f; along += 0.5f)
        {
            Vector2 axis = new Vector2(40f, 40f) + diagonal * along;

            foreach (Vector2 edge in new[] { axis + edgeOffset, axis - edgeOffset })
            {
                if (Mathf.Abs(edge.x - 128f) > 10f)
                    continue;

                edgeSamples++;

                if (!WaterCrossings.OverSpan(obliqueWater, edge))
                    edgeInRiver++;
            }
        }

        StampChecks.Expect(oblique.Bridge && edgeSamples > 0 && edgeInRiver == 0, $"A 45° bridge leaves {edgeInRiver} of {edgeSamples} road edge samples over the river");
        StampChecks.Expect(oblique.Span < 80f, $"A 45° crossing of a 20 m river needs a {oblique.Span:0} m bridge");

        Console.WriteLine($"Crossings: culvert road {road - 100f:0.00} m over water, pipe {culvert.Span:0.0} m; bridge deck {bridge.DeckHeight - 100f:0.00} m over water, span {bridge.Span:0.0} m, {mesh.Piers} piers, channel bed {channel:0.00} m; 45° bridge span {oblique.Span:0.0} m, road edges over the river outside the deck {edgeInRiver}");
    }

    private static (HeightMap, WaterMap, WaterCrossing) Crossing(WorldGenerationConfig config, float width, bool oblique = false)
    {
        var map = new HeightMap(257, 256, MAX_HEIGHT);
        var water = new WaterMap(32, 8f, 256f, 0f);
        var river = new RiverPath();
        float half = width * 0.5f;

        river.Points.Add(new RiverPoint { Position = new Vector2(128f, 0f), Surface = 100f, Bed = 97f, Width = width });
        river.Points.Add(new RiverPoint { Position = new Vector2(128f, 256f), Surface = 100f, Bed = 97f, Width = width });
        water.Rivers.Add(river);

        for (int z = 0; z <= 256; z++)
            for (int x = 0; x <= 256; x++)
                map.Set(x, z, (Mathf.Abs(x - 128f) < half ? 97f : 103f) / MAX_HEIGHT);

        var roads = new[] { new Road(oblique ? new[] { new Vector2(40f, 40f), new Vector2(216f, 216f) } : new[] { new Vector2(16f, 128f), new Vector2(240f, 128f) }, 10f, RoadKind.Highway) };
        water.FindCrossings(roads);
        var carver = new TerrainCarver(config, water);
        HeightMap carved = carver.CarveHighways(map, roads, null);
        WaterCrossings.Finish(water, carved, config, carver.Bridges);

        StampChecks.Expect(water.Crossings.Count == 1, $"One road over one river must give one crossing, not {water.Crossings.Count}");
        return (carved, water, water.Crossings[0]);
    }

    private static float Lowest(HeightMap map, int fromX, int toX)
    {
        float lowest = float.MaxValue;

        for (int z = 125; z <= 131; z++)
            for (int x = fromX; x <= toX; x++)
                lowest = Mathf.Min(lowest, map.SampleWorldSmooth(x, z));

        return lowest;
    }

    private static void CheckClimate(WorldGenerationConfig config, bool stamped, bool coast)
    {
        const float SNOW = 700f, DESERT = 1350f;

        StampLoader.Set(config.Water, "Coast", coast);
        float shore = coast ? CoastShaper.Width(config) * (1f + config.Water.CoastVariation) + CoastShaper.FIELD_STEP : 0f;

        var biomes = new BiomeMap(WORLD / 8, WORLD);

        for (int z = 0; z < biomes.Resolution; z++)
            for (int x = 0; x < biomes.Resolution; x++)
                biomes.Set(x, z, (byte)((x + 0.5f) * 8f < SNOW ? 1 : (x + 0.5f) * 8f > DESERT ? 2 : 0));

        WaterStampLoader.Attach(config, stamped);
        WaterClimate climate = WaterClimate.From(biomes, new[] { false, false, true }, new[] { false, true, false });
        HeightMap map = WorldMapPipeline.Hydrate(config, Terrain(), out WaterMap water, out _, climate);
        int drySource = 0, dryLake = 0, drySea = 0, crossing = 0;
        var fed = new bool[water.Bodies.Count];

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            RiverPoint first = water.Rivers[river].Points[0];

            if (climate.Dry(first.Position.x, first.Position.y) && RiverEnds.Start(water, river) == RiverEnd.Loose)
                drySource++;

            foreach (RiverPoint point in water.Rivers[river].Points)
            {
                if (climate.Dry(point.Position.x, point.Position.y))
                    crossing++;

                int body = Hydrology.LakeNear(water, point.Position.x, point.Position.y, 0.5f * point.Width + water.CellSize);

                if (body >= 0)
                    fed[body] = true;
            }
        }

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            Vector2 center = water.CellCenter(cell);

            if (water.BodyIds[cell] >= 0 && !fed[water.BodyIds[cell]] && climate.Dry(center.x, center.y))
                dryLake++;

            if (water.Kinds[cell] == (byte)WaterKind.Sea && center.x > DESERT + WaterClimate.DRY_MARGIN + WaterClimate.LIFT_BLEND && CoastShaper.Border(center.x, center.y, WORLD, 0f) > shore)
                drySea++;
        }

        int frozen = 0, misplaced = 0;

        foreach (WaterMeshPart part in WaterMeshes.Build(water))
        {
            if (!part.Frozen)
                continue;

            frozen += part.Triangles.Count / 3;

            foreach (Vector3 vertex in part.Vertices)
                if (vertex.x > SNOW + 64f)
                    misplaced++;
        }

        bool snowWater = water.Rivers.Exists(river => river.Points.Exists(point => point.Position.x < SNOW - 32f));

        StampChecks.Expect(drySource == 0, $"{drySource} rivers rise in the desert");
        StampChecks.Expect(dryLake == 0, $"{dryLake} cells of lakes no river feeds lie in the desert");
        StampChecks.Expect(drySea == 0, $"{drySea} sea cells lie in the desert");
        StampChecks.Expect(coast || !snowWater || frozen > 0, "Water in the snow biome is not frozen");
        StampChecks.Expect(misplaced == 0, $"{misplaced} ice vertices lie outside the snow biome");
        Console.WriteLine($"Climate{(coast ? " with the coast" : "")}: 0 river sources, 0 cells of standalone lakes and 0 sea cells past the coast in the desert, {crossing} river points crossing it, {frozen} ice triangles in the snow");
    }

    public static void CheckMainRiver(WorldGenerationConfig config, Hydrology hydrology, WaterMap water, int world)
    {
        MainRiverPlan plan = hydrology.MainRiver;
        StampChecks.Expect(plan != null && plan.Success, $"No main river was planned: {plan?.Describe()}");

        if (plan == null || !plan.Success || water.Rivers.Count == 0)
            return;

        List<RiverPoint> points = water.Rivers[0].Points;
        float rise = 0f, narrowing = 0f, widest = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            rise = Mathf.Max(rise, points[i].Surface - points[i - 1].Surface);
            narrowing = Mathf.Max(narrowing, points[i - 1].Width - points[i].Width);
            widest = Mathf.Max(widest, points[i].Width);
        }

        float length = water.Rivers[0].Length;
        RiverEnd end = RiverEnds.End(water, 0);

        StampChecks.Expect(length >= config.Water.MainRiverMinLength * world, $"The main river is {length:0} m, under {config.Water.MainRiverMinLength:P0} of the {world} m world");
        StampChecks.Expect(plan.Coverage >= config.Water.MainRiverMinCoverage, $"The main river crosses {plan.Coverage:P0} of its region");
        StampChecks.Expect(rise <= EPSILON, $"The main river climbs {rise:0.000} m between two points");
        StampChecks.Expect(narrowing <= 0.05f * widest, $"The main river narrows by {narrowing:0.00} m downstream");
        StampChecks.Expect(widest >= 0.9f * config.Water.MainRiverWidth * config.Water.RiverWidthScale, $"The main river is at most {widest:0.0} m wide");
        StampChecks.Expect(end != RiverEnd.Loose, "The main river ends in nothing");

        if (CoastShaper.Active(config))
            StampChecks.Expect(plan.Outlet != MainRiverOutlet.Border, "The main river runs off the map although the coast is on");

        Console.WriteLine($"Главная река: {length / 1000f:0.0} км, {plan.Coverage:P0} своего региона, ширина до {widest:0} м, устье {plan.Outlet}, подъём уровня {rise:0.000} м");
    }

    public static void CheckCoast(WorldGenerationConfig config, WaterMap water)
    {
        if (!CoastShaper.Active(config))
            return;

        WaterPolicyAudit.Result policy = WaterPolicyAudit.Measure(config, null, null, null, water, null);
        float least = Mathf.Min(Mathf.Min(policy.CoastSea[0], policy.CoastSea[1]), Mathf.Min(policy.CoastSea[2], policy.CoastSea[3]));

        StampChecks.Expect(least >= 0.99f, $"Only {least:P0} of a world side is sea");
        StampChecks.Expect(policy.InlandSeaCells == 0, $"{policy.InlandSeaCells} sea cells are cut off from the ocean");
        StampChecks.Expect(policy.CoastStd >= 0.05f * CoastShaper.Width(config) || config.Water.CoastVariation <= 0f, $"The coastline runs parallel to the border (spread {policy.CoastStd:0} m)");
        StampChecks.Expect(policy.CrowdedBodies == 0, $"{policy.CrowdedBodies} small water bodies sit within {config.Water.MinShoreGap:0} m of other water: {string.Join(" ", policy.CrowdedPairs)}");
        Console.WriteLine($"Побережье: море на всех сторонах не меньше {least:P0}, отрезанного моря {policy.InlandSeaCells}, берег {policy.CoastMin:0}..{policy.CoastMax:0} м от края (разброс {policy.CoastStd:0} м)");
    }

    public static void CheckOffshore(WorldGenerationConfig config, WaterMap water)
    {
        float reach = CoastShaper.Active(config) ? config.Water.OffshoreReach : 0f;

        if (reach <= 0f)
            return;

        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        WaterMeshPart ring = WaterMeshes.Offshore(water, parts, reach);
        WaterMeshPart floor = WaterMeshes.OffshoreFloor(water.WorldSize, CoastShaper.Floor(config), reach);
        var ringVertices = new HashSet<(long, long)>();

        foreach (Vector3 vertex in ring.Vertices)
            ringVertices.Add(((long)Math.Round(vertex.x * 1000f), (long)Math.Round(vertex.z * 1000f)));

        int border = 0, unmatched = 0;

        foreach (WaterMeshPart part in parts)
        {
            if (part.Kind != WaterKind.Sea)
                continue;

            foreach (Vector3 vertex in part.Vertices)
            {
                bool edge = Mathf.Abs(vertex.x) < 1e-3f || Mathf.Abs(vertex.z) < 1e-3f || Mathf.Abs(vertex.x - water.WorldSize) < 1e-3f || Mathf.Abs(vertex.z - water.WorldSize) < 1e-3f;

                if (!edge)
                    continue;

                border++;

                if (!ringVertices.Contains(((long)Math.Round(vertex.x * 1000f), (long)Math.Round(vertex.z * 1000f))))
                    unmatched++;
            }
        }

        float outside = Mathf.CeilToInt(reach / WaterMeshes.OFFSHORE_STEP) * WaterMeshes.OFFSHORE_STEP;
        float expected = (water.WorldSize + 2f * outside) * (water.WorldSize + 2f * outside) - water.WorldSize * water.WorldSize;
        float ringArea = Area(ring, out int ringDown);
        float floorArea = Area(floor, out int floorDown);

        StampChecks.Expect(border > 0, "The sea mesh never reaches the world border");
        StampChecks.Expect(unmatched == 0, $"{unmatched} of {border} sea vertices on the world border have no offshore partner, so the ring leaves T-junctions");
        StampChecks.Expect(Mathf.Abs(ringArea - expected) < expected * 1e-4f, $"The offshore sea covers {ringArea / 1e6f:0.00} km², not {expected / 1e6f:0.00}");
        StampChecks.Expect(Mathf.Abs(floorArea - expected) < expected * 1e-4f, $"The offshore floor covers {floorArea / 1e6f:0.00} km², not {expected / 1e6f:0.00}");
        StampChecks.Expect(ringDown == 0 && floorDown == 0, $"{ringDown + floorDown} offshore triangles face down");
        Console.WriteLine($"Море за краем: {ring.Triangles.Count / 3} треугольников воды и {floor.Triangles.Count / 3} дна на {expected / 1e6f:0.0} км², {border} вершин кромки совпали");
    }

    private static float Area(WaterMeshPart part, out int down)
    {
        float area = 0f;
        down = 0;

        for (int i = 0; i < part.Triangles.Count; i += 3)
        {
            Vector3 a = part.Vertices[part.Triangles[i]], b = part.Vertices[part.Triangles[i + 1]], c = part.Vertices[part.Triangles[i + 2]];
            float facing = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);

            if (facing <= 0f)
                down++;

            area += Mathf.Abs(facing) * 0.5f;
        }

        return area;
    }

    private static void CheckWaterLod(WaterMap water)
    {
        var voxels = new VoxelConfig();
        var viewer = new Vector2(0f, 0f);
        var plan = new VoxelStreamPlan(voxels, 6, 96f, water.WorldSize, water);
        var rings = new VoxelStreamPlan(voxels, 6, 96f, water.WorldSize);
        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);
        int bad = 0;

        foreach (RiverPath river in water.Rivers)
            foreach (RiverPoint point in river.Points)
                if (plan.LodAt(viewer, point.Position * 0.999999f) > Mathf.Max(1, rings.LodAt(viewer, point.Position * 0.999999f) - VoxelStreamPlan.WATER_LOD_SLACK))
                    bad++;

        float area = 0f;

        foreach (VoxelColumnKey key in keys)
        {
            float size = plan.ChunkMetres(key.Lod);
            area += size * size;

            foreach (Vector2 neighbor in new[]
            {
                new Vector2(key.X * size - 0.1f, (key.Z + 0.5f) * size),
                new Vector2((key.X + 1) * size + 0.1f, (key.Z + 0.5f) * size),
                new Vector2((key.X + 0.5f) * size, key.Z * size - 0.1f),
                new Vector2((key.X + 0.5f) * size, (key.Z + 1) * size + 0.1f)
            })
            {
                int lod = plan.LodAt(viewer, neighbor);

                if (lod >= 0 && Mathf.Abs(lod - key.Lod) > 1)
                    bad++;
            }
        }

        StampChecks.Expect(bad == 0, $"Water LOD protection or neighbor balance failed at {bad} points");
        StampChecks.Expect(Mathf.Abs(area - water.WorldSize * water.WorldSize) < 1f, "Adaptive terrain must cover the world exactly once");
        Console.WriteLine($"Water LOD: {keys.Count} columns, {bad} protection or balance errors");
    }

    private static string Fingerprint(byte[] bytes)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();

        return Convert.ToHexString(hash.ComputeHash(bytes)).Substring(0, 16);
    }

    private static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);

        return bytes;
    }

    public static WorldGenerationConfig Config(WorldGenerationConfig source, int world)
    {
        var config = new WorldGenerationConfig();

        StampLoader.Set(config, "Seed", source.Seed);
        StampLoader.Set(config, "WorldSize", world);
        StampLoader.Set(config, "SeaLevel", 60f);

        WaterGenerationSettings water = config.Water;
        StampLoader.Set(water, "RiverStartArea", 0.12f);
        StampLoader.Set(water, "LakeDensity", 1f);
        StampLoader.Set(water, "PondDensity", 1f);
        StampLoader.Set(water, "MinLakeArea", 8000f);
        StampLoader.Set(water, "LakeSpacing", 200f);

        return config;
    }

    private static HeightMap Terrain()
    {
        var map = new HeightMap(WORLD + 1, WORLD, MAX_HEIGHT);

        for (int z = 0; z <= WORLD; z++)
        {
            for (int x = 0; x <= WORLD; x++)
            {
                float slope = 150f - 70f * x / WORLD;
                float valleys = 9f * Mathf.Sin(z * 0.009f + Mathf.Sin(x * 0.004f) * 1.5f);
                float hills = 4f * Mathf.Sin(x * 0.021f) * Mathf.Cos(z * 0.017f);
                float bowl = -26f * Bowl(x, z, 700f, 1300f, 160f) - 18f * Bowl(x, z, 1500f, 500f, 110f) - 5f * Bowl(x, z, 400f, 400f, 40f);

                map.Heights[z * (WORLD + 1) + x] = (slope + valleys + hills + bowl) / MAX_HEIGHT;
            }
        }

        return map;
    }

    private static float Bowl(float x, float z, float cx, float cz, float radius)
    {
        float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (radius * radius);

        return Mathf.Max(0f, 1f - d2) * Mathf.Max(0f, 1f - d2);
    }

    private static WaterAudit.Result CheckArtifacts(HeightMap carved, WaterMap water, float hangLimit)
    {
        WaterAudit.Result audit = WaterAudit.Measure(carved, water, new Vector2(_world * 0.5f, _world * 0.5f), "water_checks_audit.png");
        WaterAudit.PrintExamples(audit);
        float probed = audit.Area(audit.Probes);

        StampChecks.Expect(audit.Probes > 0, "the water audit probed nothing");
        StampChecks.Expect(audit.Area(audit.Holes) <= probed * 0.001f, $"{audit.Area(audit.Holes):0} m² of ground under water with no water mesh");
        StampChecks.Expect(audit.Area(audit.Hanging) <= probed * 0.001f && audit.WorstHang < hangLimit, $"{audit.Area(audit.Hanging):0} m² of water hanging over dry land, up to {audit.WorstHang:0.00} m (limit {hangLimit:0.00} m)");
        StampChecks.Expect(audit.Area(audit.ZFight) <= probed * 0.0005f, $"{audit.Area(audit.ZFight):0} m² of coplanar water surfaces");
        StampChecks.Expect(audit.Area(audit.Stacked) <= probed * 0.002f, $"{audit.Area(audit.Stacked):0} m² of water drawn twice");
        StampChecks.Expect(audit.Steps == 0, $"{audit.Steps} river level steps steeper than 15%, up to {audit.WorstStep:0.00}");
        StampChecks.Expect(audit.WorstTrench < 9f, $"a river trench {audit.WorstTrench:0.0} m deep");

        long nearFlood = audit.FloodByLod[0] + audit.FloodByLod[1] + audit.FloodByLod[2];
        long nearMissing = audit.MissingByLod[0] + audit.MissingByLod[1] + audit.MissingByLod[2];

        StampChecks.Expect(audit.Area(nearFlood + nearMissing) <= probed * 0.001f, $"{audit.Area(nearFlood + nearMissing):0} m² of water disagreeing with the near LOD rings");

        Console.WriteLine($"  аудит артефактов на {probed / 1e6f:0.00} км² у воды: дыры {audit.Area(audit.Holes):0} м², висит {audit.Area(audit.Hanging):0} м², "
            + $"z-fight {audit.Area(audit.ZFight):0} м², двойная {audit.Area(audit.Stacked):0} м², ступеньки {audit.Steps}, траншея до {audit.WorstTrench:0.0} м, "
            + $"ближние кольца LOD {audit.Area(nearFlood + nearMissing):0} м²");

        return audit;
    }

    private static void CheckShore(HeightMap carved, HeightMap raw, WaterMap water, WaterMap repeat, float bankWidth)
    {
        WaterShoreChecks.Result shore = WaterShoreChecks.Measure(carved, raw, water, bankWidth);

        StampChecks.Expect(shore.Bodies > 0 && shore.SplitBodies == 0, $"{shore.SplitBodies} lakes carry a detached piece smaller than a pond");
        StampChecks.Expect(shore.Fragments == 0, $"{shore.Fragments} standing water fragments under 16 m²");
        StampChecks.Expect(shore.TinyHoles == 0, $"{shore.TinyHoles} tiny holes in standing water with ground below the surface");
        StampChecks.Expect(shore.GridShare < 0.05f, $"{shore.GridShare:P1} of the shoreline runs along hydrology grid lines");
        StampChecks.Expect(shore.ShoreP95 < WaterShore.SHELF, $"shoreline sits {shore.ShoreP95:0.00} m off the waterline at p95");
        StampChecks.Expect(shore.ShoreHighP95 < 0.5f, $"the water mesh reaches {shore.ShoreHighP95:0.00} m up dry ground at p95");
        StampChecks.Expect(shore.Hanging <= shore.ShoreSamples / 50, $"{shore.Hanging} of {shore.ShoreSamples} shoreline samples end over submerged ground");
        StampChecks.Expect(shore.SpikesPerKm <= 1f, $"{shore.SpikesPerKm:0.00} one-node spikes per km of shoreline");
        StampChecks.Expect(shore.Mouths > 0 && shore.MouthGaps <= shore.MouthSamples / 100, $"{shore.MouthGaps} of {shore.MouthSamples} river mouth samples have no water");
        StampChecks.Expect(shore.Sections > 0 && shore.WetCenters == shore.Sections, $"{shore.Sections - shore.WetCenters} of {shore.Sections} river sections are dry in the middle");
        StampChecks.Expect(shore.FallingBanks <= shore.Sections / 25, $"{shore.FallingBanks} river banks fall away from the water on rising ground");
        StampChecks.Expect(shore.Cliffs <= shore.Sections / 25, $"{shore.Cliffs} river banks carved steeper than 45° on gentler ground");
        StampChecks.Expect(shore.TJunctions == 0 && shore.NonManifold == 0, $"{shore.TJunctions} T-junctions and {shore.NonManifold} over-shared edges in standing water");
        StampChecks.Expect(WaterShoreChecks.SameMeshes(water, repeat), "the same seed produced different water meshes");

        Console.WriteLine(shore.Describe());
    }

    private static void CheckRivers(WaterMap water, HeightMap carved)
    {
        StampChecks.Expect(water.Rivers.Count > 0, "no river formed");

        int uphill = 0, narrowing = 0, shrinking = 0, dry = 0, points = 0;
        float steepest = 0f;

        foreach (RiverPath river in water.Rivers)
        {
            for (int i = 1; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i - 1];
                RiverPoint b = river.Points[i];
                points++;

                if (b.Surface > a.Surface + EPSILON)
                {
                    uphill++;
                    steepest = Mathf.Max(steepest, b.Surface - a.Surface);
                }

                if (b.Flow < a.Flow - EPSILON)
                    shrinking++;

                if (b.Width < a.Width - EPSILON - (water.Stamps != null ? 0.15f * Vector2.Distance(a.Position, b.Position) : 0f))
                    narrowing++;

                if (!b.Submerged && carved.SampleWorldSmooth(b.Position.x, b.Position.y) > b.Surface + 0.05f)
                    dry++;
            }
        }

        StampChecks.Expect(uphill == 0, $"{uphill} river steps flow uphill, up to {steepest:0.000} m");
        StampChecks.Expect(shrinking == 0, $"{shrinking} river steps lose flow downstream");
        StampChecks.Expect(narrowing == 0, $"{narrowing} river steps narrow downstream");
        StampChecks.Expect(dry <= points / 100, $"{dry} of {points} river points have ground above the water after carving");

        Console.WriteLine($"  реки: {points} точек, вверх по склону {uphill}, расход убывает {shrinking}, сужение {narrowing}, "
            + $"русло над водой после врезки {dry}");
    }

    private static void CheckBodies(WaterMap water, HeightMap carved)
    {
        StampChecks.Expect(water.Bodies.Count > 0, "no lake or pond formed");

        int above = 0, cells = 0;

        for (int i = 0; i < water.Kinds.Length; i++)
        {
            var kind = (WaterKind)water.Kinds[i];

            if (kind != WaterKind.Lake && kind != WaterKind.Pond)
                continue;

            cells++;

            Vector2 center = water.CellCenter(i);
            float surface = water.Bodies[water.BodyIds[i]].Surface;
            float lowest = float.MaxValue;

            for (int dz = 0; dz <= 2; dz++)
            {
                for (int dx = 0; dx <= 2; dx++)
                    lowest = Mathf.Min(lowest, carved.SampleWorldSmooth(center.x + (dx - 1) * water.CellSize * 0.5f, center.y + (dz - 1) * water.CellSize * 0.5f));
            }

            if (lowest >= surface)
                above++;
        }

        StampChecks.Expect(above == 0, $"{above} of {cells} lake cells have no ground under the water surface");

        var surfaces = new List<string>();

        foreach (WaterBody body in water.Bodies)
            surfaces.Add($"{body.Kind} {body.Area / 10000f:0.0} га на {body.Surface:0.0} м");

        Console.WriteLine($"  озёра и пруды: {water.Bodies.Count}, клеток {cells}, без дна под водой {above}; {string.Join(", ", surfaces)}");
    }

    private static void CheckFinite(WaterMap water)
    {
        int bad = 0, outside = 0;

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (!float.IsFinite(point.Surface) || !float.IsFinite(point.Bed) || !float.IsFinite(point.Width) || !float.IsFinite(point.Position.x) || !float.IsFinite(point.Position.y))
                    bad++;

                if (point.Position.x < 0f || point.Position.y < 0f || point.Position.x > water.WorldSize || point.Position.y > water.WorldSize)
                    outside++;
            }
        }

        foreach (WaterBody body in water.Bodies)
        {
            if (!float.IsFinite(body.Surface) || !float.IsFinite(body.Area))
                bad++;
        }

        for (int i = 0; i < water.ShoreDistance.Length; i++)
        {
            if (float.IsNaN(water.ShoreDistance[i]) || float.IsNaN(water.ShoreSurface[i]))
                bad++;

            if (float.IsFinite(water.ShoreDistance[i]) && !float.IsFinite(water.ShoreSurface[i]))
                bad++;
        }

        StampChecks.Expect(bad == 0, $"{bad} NaN or infinite water values");
        StampChecks.Expect(outside == 0, $"{outside} river points outside the world");
        Console.WriteLine($"  значения: NaN/Infinity {bad}, точек вне мира {outside}");
    }

    private static void CheckDeterminism(WaterMap first, WaterMap second)
    {
        byte[] a = WaterMapFormat.Write(first);
        byte[] b = WaterMapFormat.Write(second);
        bool same = a.Length == b.Length;

        for (int i = 0; same && i < a.Length; i++)
            same = a[i] == b[i];

        StampChecks.Expect(same, "the same seed produced different water");
        Console.WriteLine($"  детерминизм: два прогона дают побайтно одинаковую карту воды ({a.Length / 1024} КБ)");
    }

    private static void CheckRoundTrip(WaterMap water)
    {
        byte[] bytes = WaterMapFormat.Write(water);
        WaterMap read = WaterMapFormat.Read(bytes);
        byte[] again = WaterMapFormat.Write(read);
        bool same = bytes.Length == again.Length;

        for (int i = 0; same && i < bytes.Length; i++)
            same = bytes[i] == again[i];

        StampChecks.Expect(same, "water map changes through serialization");
    }

    private static void CheckMeshes(WaterMap water)
    {
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        int triangles = 0, bad = 0;

        foreach (WaterMeshPart part in parts)
        {
            triangles += part.Triangles.Count / 3;

            foreach (Vector3 vertex in part.Vertices)
            {
                if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                    bad++;
            }

            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                Vector3 a = part.Vertices[part.Triangles[t]];
                Vector3 b = part.Vertices[part.Triangles[t + 1]];
                Vector3 c = part.Vertices[part.Triangles[t + 2]];

                float facing = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);

                if (facing < -1e-4f)
                    bad++;

                if (part.Kind != WaterKind.River && (Mathf.Abs(a.y - b.y) > 1e-4f || Mathf.Abs(a.y - c.y) > 1e-4f))
                    bad++;
            }
        }

        StampChecks.Expect(parts.Count > 0 && triangles > 0, "no water mesh");
        StampChecks.Expect(bad == 0, $"{bad} water mesh problems: NaN, faces pointing down, or a tilted lake quad");
        Console.WriteLine($"  меши воды: {parts.Count} частей, {triangles} треугольников, проблем {bad}");
    }

    private static void CheckQueries(WaterMap water, HeightMap carved)
    {
        RiverPoint probe = default;
        bool found = false;

        foreach (RiverPath river in water.Rivers)
        {
            for (int i = Mathf.Min(river.Points.Count - 1, QUERY_SKIP); i < river.Points.Count; i++)
            {
                RiverPoint point = river.Points[i];

                if (point.Submerged || point.Width < 3f)
                    continue;

                probe = point;
                found = true;
                break;
            }

            if (found)
                break;
        }

        StampChecks.Expect(found, "no river wide enough to query");

        if (!found)
            return;

        float ground = carved.SampleWorldSmooth(probe.Position.x, probe.Position.y);
        WaterSample sample = water.Sample(probe.Position.x, probe.Position.y);

        StampChecks.Expect(sample.Kind == WaterKind.River, $"river centre at ({probe.Position.x:0}, {probe.Position.y:0}) reads as {sample.Kind}");
        StampChecks.Expect(water.IsWater(probe.Position.x, probe.Position.y, ground), "river centre is not water");
        StampChecks.Expect(water.Depth(probe.Position.x, probe.Position.y, ground) > 0.1f, "river centre has no depth");
        StampChecks.Expect(sample.Flow.sqrMagnitude > 0.9f, "river centre has no flow direction");
        StampChecks.Expect(water.IsWet(probe.Position.x + probe.Width, probe.Position.y, probe.Surface + 1f, 6f), "a bank a metre above the river is not wet within ShoreMargin");
    }
}
