using System;
using System.Collections.Generic;
using UnityEngine;

static class WaterFixtures
{
    private const int WORLD = 2048;
    private const float START_AREA = 0.05f;
    private const float MAX_HEIGHT = 384f;
    private const float SEA = 60f;
    private const float PATCH = 16f;
    private const float HANG = 0.3f;
    private const float TRENCH = 8f;
    private const float PLANE = 40f;
    private const float SPILL_TOLERANCE = 0.25f;
    private const float DRY_END_REACH = 24f;

    public static void Run(WorldGenerationConfig source)
    {
        Fixture(source, "плоская земля", Flat, true, null, (map, water) => { });
        Fixture(source, "вложенные впадины", Nested, true, null, CheckNested);
        Fixture(source, "узкий сток через седловину", Saddle, true, null, CheckSaddle);
        Fixture(source, "крутой склон к морю", Steep, true, null, CheckSteep);
        Fixture(source, "переход во влажный и сухой биом", Tilted, true, Dry, (map, water) => CheckDry(water, Dry(), false));
        Fixture(source, "сухой биом вдоль склона", Tilted, true, DryAlong, (map, water) => CheckDry(water, DryAlong(), true));
        Fixture(source, "котловина у моря", Lagoon, true, null, (map, water) => { });
        Fixture(source, "впадина ниже моря вдали от берега", Pit, true, null, CheckPit);
        Fixture(source, "близкие слияния", Fan, true, null, CheckFan);

        StampChecks.Finish("Фикстуры воды", "плоская земля, вложенные впадины, седловина, крутой склон, сухой биом, котловина у моря, впадина ниже моря и близкие слияния дают граф рек без нарушений, без дыр, висящей воды, траншей и наклонных плоскостей, с озёрами не выше перелива.");
    }

    private static void Fixture(WorldGenerationConfig source, string name, Func<float, float, float> terrain, bool coast, Func<WaterClimate> climate, Action<HeightMap, WaterMap> extra)
    {
        WorldGenerationConfig config = WaterChecks.Config(source, WORLD);
        StampLoader.Set(config, "SeaLevel", SEA);
        StampLoader.Set(config.Water, "Coast", coast);
        StampLoader.Set(config.Water, "ThroughRiver", false);
        StampLoader.Set(config.Water, "RiverStartArea", START_AREA);
        WaterStampLoader.Attach(config, false);

        var map = new HeightMap(WORLD + 1, WORLD, MAX_HEIGHT);

        for (int z = 0; z <= WORLD; z++)
            for (int x = 0; x <= WORLD; x++)
                map.Heights[z * (WORLD + 1) + x] = Mathf.Clamp(terrain(x, z), 0f, MAX_HEIGHT) / MAX_HEIGHT;

        HeightMap carved = WorldMapPipeline.Hydrate(config, map, out WaterMap water, out Hydrology hydrology, climate?.Invoke());
        WaterContract contract = WaterContract.Check(water, config.Water.RiverStartArea * 1e6f, !coast);
        WaterAudit.Result audit = WaterAudit.Measure(carved, water, new Vector2(WORLD * 0.5f, WORLD * 0.5f), null);
        RiverShapeAudit.Result shape = RiverShapeAudit.Measure(carved, water);

        var bodies = new List<string>();

        foreach (WaterBody body in water.Bodies)
            bodies.Add($"{body.Kind} {body.Surface:0.0} м {body.Area:0} м²");

        Console.WriteLine($"{name}: {water.Rivers.Count} рек, {water.Bodies.Count} водоёмов [{string.Join(", ", bodies)}]; {contract.Describe()}; дыра {audit.LargestHole:0} м², висит {audit.WorstHang:0.00} м / {audit.LargestHang:0} м², траншея {audit.WorstTrench:0.0} м, плоскость {shape.LongestPlane:0} м");

        StampChecks.Expect(contract.Violations.Count == 0, $"{name}: {(contract.Violations.Count > 0 ? contract.Violations[0].ToString() : "")}");
        StampChecks.Expect(audit.LargestHole <= PATCH, $"{name}: a {audit.LargestHole:0} m² hole under the water level");
        StampChecks.Expect(audit.WorstHang <= HANG && audit.LargestHang <= PATCH, $"{name}: water hangs {audit.WorstHang:0.00} m over dry ground in a {audit.LargestHang:0} m² patch");
        StampChecks.Expect(audit.LargestZFight <= PATCH && audit.LargestStack <= PATCH, $"{name}: {audit.LargestZFight:0} m² z-fighting, {audit.LargestStack:0} m² stacked water");
        StampChecks.Expect(audit.Steps == 0, $"{name}: {audit.Steps} river level steps");
        StampChecks.Expect(audit.WorstTrench <= TRENCH, $"{name}: a {audit.WorstTrench:0.0} m trench");
        StampChecks.Expect(shape.LongestPlane <= PLANE, $"{name}: a {shape.LongestPlane:0} m inclined plane");
        StampChecks.Expect(!float.IsNaN(Checksum(carved)), $"{name}: NaN in the height map");

        if (Environment.GetEnvironmentVariable("FIXTURE") == name)
        {
            WaterAudit.PrintExamples(audit);

            string at = Environment.GetEnvironmentVariable("FIXTURE_AT");

            if (!string.IsNullOrEmpty(at))
                WaterAudit.Inspect(carved, water, float.Parse(at.Split(',')[0]), float.Parse(at.Split(',')[1]));

            string view = Environment.GetEnvironmentVariable("FIXTURE_VIEW");

            if (!string.IsNullOrEmpty(view))
            {
                var scenes = new List<WaterView.Scene>();

                foreach ((string kind, System.Collections.Concurrent.ConcurrentBag<Vector2> points) in audit.Examples)
                {
                    foreach (Vector2 point in points)
                    {
                        scenes.Add(new WaterView.Scene($"{kind.Replace(' ', '_')}_{point.x:0}_{point.y:0}", point, 160f));
                        break;
                    }
                }

                WaterView.Render(view, carved, water, scenes);
            }

            foreach (HydrologyEvent entry in hydrology.Log.Events)
                Console.WriteLine($"  event {entry}");
            Console.WriteLine(RiverShapeAudit.Measure(carved, water).Describe());

            for (int river = 0; river < water.Rivers.Count; river++)
            {
                RiverPath path = water.Rivers[river];
                Console.WriteLine($"  river {river}: {path.Points.Count} points {path.Source} -> {path.Terminal} parent {path.Parent}, head {path.SourceArea:0} donor {path.SourceDonor:0}, ({path.Points[0].Position.x:0}, {path.Points[0].Position.y:0}) {path.Points[0].Surface:0.0} -> ({path.Points[^1].Position.x:0}, {path.Points[^1].Position.y:0}) {path.Points[^1].Surface:0.0}");
            }
        }

        extra(carved, water);
    }

    private static float Checksum(HeightMap map)
    {
        float sum = 0f;

        foreach (float height in map.Heights)
            sum += height;

        return sum;
    }

    private static float Flat(float x, float z)
    {
        return 80f;
    }

    private static float Tilted(float x, float z)
    {
        return 140f - 70f * x / WORLD + 6f * Mathf.Sin(z * 0.012f + Mathf.Sin(x * 0.006f) * 1.2f);
    }

    private static float Nested(float x, float z)
    {
        return Tilted(x, z) - 22f * Bowl(x, z, 1024f, 1024f, 300f) - 12f * Bowl(x, z, 1024f, 1024f, 90f);
    }

    private static float Saddle(float x, float z)
    {
        float radius = Vector2.Distance(new Vector2(x, z), new Vector2(960f, 1024f));
        float ring = 26f * Mathf.Exp(-Mathf.Pow((radius - 260f) / 50f, 2f));
        float notch = Mathf.Abs(z - 1024f) < 16f && x > 960f ? 0.2f : 1f;

        return Tilted(x, z) - 18f * Bowl(x, z, 960f, 1024f, 240f) + ring * notch;
    }

    private static float Steep(float x, float z)
    {
        return 260f - 190f * x / WORLD + 14f * Mathf.Sin(z * 0.02f) * Mathf.Min(1f, x / 300f);
    }

    private static float Lagoon(float x, float z)
    {
        return Tilted(x, z) - 28f * Bowl(x, z, 1760f, 1024f, 110f);
    }

    private static float Pit(float x, float z)
    {
        return Tilted(x, z) - 80f * Bowl(x, z, 900f, 1024f, 90f);
    }

    private static float Fan(float x, float z)
    {
        float valleys = 0f;

        foreach (float angle in new[] { -0.5f, 0f, 0.5f })
        {
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 offset = new Vector2(x - 1400f, z - 1024f);
            float along = -Vector2.Dot(offset, axis);
            float across = Mathf.Abs(offset.x * axis.y - offset.y * axis.x);

            if (along > 0f || angle == 0f)
                valleys = Mathf.Max(valleys, 14f * Mathf.Exp(-across * across / (90f * 90f)));
        }

        return 150f - 80f * x / WORLD - valleys;
    }

    private static float Bowl(float x, float z, float cx, float cz, float radius)
    {
        float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (radius * radius);

        return Mathf.Max(0f, 1f - d2) * Mathf.Max(0f, 1f - d2);
    }

    private static WaterClimate Dry()
    {
        return Climate((x, z) => x > WORLD * 0.4f);
    }

    private static WaterClimate DryAlong()
    {
        return Climate((x, z) => z > WORLD * 0.55f);
    }

    private static WaterClimate Climate(Func<float, float, bool> dry)
    {
        var biomes = new BiomeMap(WORLD / 8, WORLD);

        for (int z = 0; z < biomes.Resolution; z++)
            for (int x = 0; x < biomes.Resolution; x++)
                biomes.Set(x, z, (byte)(dry((x + 0.5f) * 8f, (z + 0.5f) * 8f) ? 1 : 0));

        return WaterClimate.From(biomes, new[] { false, true }, new[] { false, false });
    }

    private static void CheckDry(WaterMap water, WaterClimate climate, bool drainsToSea)
    {
        int wetInDry = 0, dryHeads = 0, endsAtDry = 0, bodiesInDry = 0;

        foreach (RiverPath river in water.Rivers)
        {
            if (river.Points.Count == 0)
                continue;

            if (river.Source == RiverSource.Headwater && climate.Dry(river.Points[0].Position.x, river.Points[0].Position.y))
                dryHeads++;

            Vector2 end = river.Points[^1].Position;

            if (river.Terminal != RiverTerminal.Sea && river.Terminal != RiverTerminal.Lake && river.Terminal != RiverTerminal.Junction && DryWithin(climate, end, DRY_END_REACH))
                endsAtDry++;

            foreach (RiverPoint point in river.Points)
            {
                if (water.RiverOpen(point) && climate.Dry(point.Position.x, point.Position.y))
                    wetInDry++;
            }
        }

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            Vector2 center = water.CellCenter(cell);

            if (water.BodyIds[cell] >= 0 && climate.Dry(center.x, center.y))
                bodiesInDry++;
        }

        StampChecks.Expect(endsAtDry == 0, $"{endsAtDry} rivers run up to the dry biome and end there");
        StampChecks.Expect(wetInDry == 0, $"{wetInDry} open river points lie in the dry biome");
        StampChecks.Expect(bodiesInDry == 0, $"{bodiesInDry} lake cells lie in the dry biome");
        StampChecks.Expect(dryHeads == 0, $"{dryHeads} rivers rise in the dry biome");
        StampChecks.Expect(!drainsToSea || water.Rivers.Count > 0, "The wet biome lost every river although it drains to the sea");
    }

    private static bool DryWithin(WaterClimate climate, Vector2 point, float reach)
    {
        for (int k = 0; k <= 8; k++)
        {
            Vector2 probe = k == 8 ? point : point + reach * new Vector2(Mathf.Cos(k * Mathf.PI / 4f), Mathf.Sin(k * Mathf.PI / 4f));

            if (climate.Dry(Mathf.Clamp(probe.x, 0f, WORLD - 1f), Mathf.Clamp(probe.y, 0f, WORLD - 1f)))
                return true;
        }

        return false;
    }

    private static void CheckNested(HeightMap map, WaterMap water)
    {
        CheckSpill(map, water, "вложенные впадины");
        StampChecks.Expect(water.Bodies.Exists(body => body.Area > 0f), "The nested basin holds no water");
    }

    private static void CheckSaddle(HeightMap map, WaterMap water)
    {
        CheckSpill(map, water, "седловина");

        bool outlet = false;

        foreach (RiverPath river in water.Rivers)
            outlet |= river.Source == RiverSource.LakeOutlet && river.Terminal == RiverTerminal.Sea;

        StampChecks.Expect(outlet, "The basin behind the saddle has no outlet river to the sea");
    }

    private static void CheckSteep(HeightMap map, WaterMap water)
    {
        StampChecks.Expect(water.Rivers.Count > 0, "The steep slope formed no river");
    }

    private static void CheckPit(HeightMap map, WaterMap water)
    {
        WaterSample sample = water.Sample(900f, 1024f);
        StampChecks.Expect(sample.Kind != WaterKind.Sea, "A pit below sea level far from the coast is filled by the sea");
    }

    private static void CheckFan(HeightMap map, WaterMap water)
    {
        RiverTopologyAudit.Result topology = RiverTopologyAudit.Measure(water);
        int junctions = 0;

        foreach (RiverPath river in water.Rivers)
            junctions += river.Terminal == RiverTerminal.Junction ? 1 : 0;

        StampChecks.Expect(topology.Crossings == 0 && topology.SideRuns == 0, $"Close confluences cross {topology.Crossings} times and run beside each other {topology.SideRuns} times");
        StampChecks.Expect(junctions > 0, "Three converging valleys formed no confluence");
    }

    private static void CheckSpill(HeightMap map, WaterMap water, string name)
    {
        for (int body = 0; body < water.Bodies.Count; body++)
        {
            WaterBody data = water.Bodies[body];

            if (data.Area <= 0f || data.Kind == WaterKind.Sea)
                continue;

            float spill = Spill(map, water, body);
            StampChecks.Expect(data.Surface <= spill + SPILL_TOLERANCE, $"{name}: body {body} stands at {data.Surface:0.00} m over its spill at {spill:0.00} m");
        }
    }

    private static float Spill(HeightMap map, WaterMap water, int body)
    {
        int nodes = water.NodeResolution;
        float step = water.NodeStep;
        var level = new float[nodes * nodes];
        var heap = new MinHeap(1024);
        Array.Fill(level, float.PositiveInfinity);

        for (int j = 0; j < nodes; j++)
        {
            for (int i = 0; i < nodes; i++)
            {
                if (!water.Covers(i * step, j * step, out short owner) || owner != body)
                    continue;

                int node = j * nodes + i;
                level[node] = map.SampleWorldSmooth(i * step, j * step);
                heap.Push(node, level[node]);
            }
        }

        while (heap.TryPop(out int current))
        {
            int i = current % nodes, j = current / nodes;

            if (i == 0 || j == 0 || i == nodes - 1 || j == nodes - 1)
                return level[current];

            if (water.Covers(i * step, j * step, out short owner) && owner != body && owner != WaterMap.OWNER_NONE)
                return level[current];

            foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int next = (j + dj) * nodes + i + di;
                float x = (i + di) * step, z = (j + dj) * step;
                float reach = Mathf.Max(level[current], map.SampleWorldSmooth(x, z));

                if (water.TryRiver(x, z, out WaterSample river))
                    reach = Mathf.Max(reach, river.Surface);

                if (reach >= level[next])
                    continue;

                level[next] = reach;
                heap.Push(next, reach);
            }
        }

        return float.PositiveInfinity;
    }
}
