using System;
using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;
using UnityEngine;

[Category("WorldGen.WaterPolicy")]
public class WaterPolicyTests
{
    private const int WORLD = 2048;
    private const float MAX_HEIGHT = 384f;
    private const float SEA_LEVEL = 60f;
    private const float SNOW = 700f;
    private const float DESERT = 1350f;
    private const float DRY_END_REACH = 24f;

    private readonly List<UnityEngine.Object> _created = new();

    [TearDown]
    public void Clean()
    {
        foreach (UnityEngine.Object created in _created)
        {
            if (created != null)
                UnityEngine.Object.DestroyImmediate(created);
        }

        _created.Clear();
    }

    [Test]
    public void MainRiverRunsDownhillAcrossItsRegionToTheSea()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out Hydrology hydrology);
        MainRiverPlan plan = hydrology.MainRiver;
        WaterGenerationSettings settings = config.Water;

        Assert.IsTrue(plan.Success, plan.Describe());
        Assert.AreEqual(MainRiverOutlet.Sea, plan.Outlet, "The main river must reach the coast when the coast is on");
        Assert.GreaterOrEqual(plan.Coverage, settings.MainRiverMinCoverage);

        RiverPath main = water.Rivers[0];
        List<RiverPoint> points = main.Points;
        float widest = 0f;

        Assert.GreaterOrEqual(main.Length, settings.MainRiverMinLength * WORLD, "The main river is too short for the world");

        for (int i = 1; i < points.Count; i++)
        {
            Assert.LessOrEqual(points[i].Surface, points[i - 1].Surface + 1e-3f, $"The main river climbs at point {i}");
            widest = Mathf.Max(widest, points[i].Width);
        }

        Assert.GreaterOrEqual(widest, 0.9f * settings.MainRiverWidth, "The main river never gets wide");
        Assert.AreEqual(RiverEnd.Standing, RiverEnds.End(water, 0), "The main river must end in the sea");
    }

    [Test]
    public void EveryRiverStartsAtASourceAndEndsInWater()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out _);
        float startArea = config.Water.RiverStartArea * 1e6f;

        Assert.Greater(water.Rivers.Count, 1, "The test world formed no tributaries");

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            if (water.Rivers[river].Points.Count < 2)
                continue;

            Assert.AreNotEqual(RiverEnd.Loose, RiverEnds.End(water, river), $"River {river} ends in nothing");

            if (RiverEnds.Start(water, river) == RiverEnd.Loose)
                Assert.IsTrue(RiverEnds.Headwater(water, river, startArea), $"River {river} starts wide out of nothing");
        }

        foreach (WaterBody body in water.Bodies)
            Assert.IsFalse(body.SeedRadius > 0f && body.Kind == WaterKind.Pond, "A river end is capped by an artificial pond");
    }

    [Test]
    public void RiverGraphHasJustifiedEndsAndNoViolations()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out _);
        WaterContract contract = WaterContract.Check(water, config.Water.RiverStartArea * 1e6f, false);

        Assert.Greater(contract.Rivers, 0, "The test world formed no rivers");
        Assert.AreEqual(0, contract.Violations.Count, contract.Violations.Count == 0 ? "" : $"{contract.Violations[0]} ({contract.Describe()})");
    }

    [Test]
    public void CoastRingsTheWorldWithOneConnectedSea()
    {
        WaterMap water = Hydrate(Config(SEA_LEVEL), null, out _);
        int n = water.Resolution;
        byte[] kinds = water.Kinds;
        const byte sea = (byte)WaterKind.Sea;

        for (int side = 0; side < 4; side++)
        {
            int wet = 0, measured = 0;

            for (int t = n / 8; t < n - n / 8; t++)
            {
                int x = side == 0 ? 0 : side == 1 ? n - 1 : t;
                int z = side == 2 ? 0 : side == 3 ? n - 1 : t;
                measured++;

                if (kinds[z * n + x] == sea)
                    wet++;
            }

            Assert.GreaterOrEqual(wet, measured * 0.99f, $"World side {side} is not sea");
        }

        var connected = new bool[kinds.Length];
        var queue = new Queue<int>();

        for (int i = 0; i < kinds.Length; i++)
        {
            int x = i % n, z = i / n;

            if (kinds[i] != sea || x != 0 && z != 0 && x != n - 1 && z != n - 1)
                continue;

            connected[i] = true;
            queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % n, z = cell / n;

            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;

                if (nx < 0 || nz < 0 || nx >= n || nz >= n || connected[nz * n + nx] || kinds[nz * n + nx] != sea)
                    continue;

                connected[nz * n + nx] = true;
                queue.Enqueue(nz * n + nx);
            }
        }

        for (int i = 0; i < kinds.Length; i++)
            Assert.IsFalse(kinds[i] == sea && !connected[i], $"Sea cell {i} is cut off from the ocean");
    }

    [Test]
    public void SmallWaterKeepsItsDistanceFromOtherWater()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out _);
        float gap = config.Water.MinShoreGap;
        int n = water.Resolution;
        short[] ids = water.BodyIds;
        byte[] kinds = water.Kinds;
        int reach = Mathf.FloorToInt(gap / water.CellSize) - 1;
        HashSet<int> rivers = RiverCells(water);

        for (int body = 0; body < water.Bodies.Count; body++)
        {
            if (water.Bodies[body].Kind != WaterKind.Pond)
                continue;

            bool onRiver = false, crowded = false;

            for (int i = 0; i < ids.Length && !crowded; i++)
            {
                if (ids[i] != body)
                    continue;

                onRiver |= rivers.Contains(i);

                for (int dz = -reach; dz <= reach && !crowded; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = i % n + dx, z = i / n + dz;

                        if (x < 0 || z < 0 || x >= n || z >= n || dx * dx + dz * dz > reach * reach)
                            continue;

                        int other = ids[z * n + x];

                        if (other >= 0 && other != body || kinds[z * n + x] == (byte)WaterKind.Sea)
                        {
                            crowded = true;
                            break;
                        }
                    }
                }
            }

            Assert.IsFalse(crowded && !onRiver, $"Pond {body} lies within {gap} m of other water");
        }
    }

    [Test]
    public void DryAndFrozenBiomesFollowTheirPolicy()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        SerializedFields.Set(config.Water, "Coast", false);
        WaterClimate climate = Climate();
        WaterMap water = Hydrate(config, climate, out _);
        short[] ids = water.BodyIds;
        bool[] ice = water.Frozen;

        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            if (points.Count == 0)
                continue;

            Vector2 source = points[0].Position;

            if (river.Source == RiverSource.Headwater)
                Assert.IsFalse(climate.Dry(source.x, source.y), $"A river rises in the desert at {source}");

            foreach (RiverPoint point in points)
            {
                if (water.RiverOpen(point))
                    Assert.IsFalse(climate.Dry(point.Position.x, point.Position.y), $"A river runs through the desert at {point.Position}");
            }

            Vector2 end = points[^1].Position;

            if (river.Terminal != RiverTerminal.Sea && river.Terminal != RiverTerminal.Lake && river.Terminal != RiverTerminal.Junction)
                Assert.IsFalse(DryWithin(climate, end, DRY_END_REACH), $"A river runs up to the desert and ends at {end}");
        }

        for (int i = 0; i < ids.Length; i++)
        {
            Vector2 center = water.CellCenter(i);

            if (ids[i] >= 0)
                Assert.IsFalse(climate.Dry(center.x, center.y), $"A lake lies in the desert at {center}");

            if (ice[i])
                Assert.IsTrue(climate.Frozen(center.x, center.y), $"Ice outside the snow at {center}");
        }

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var root = new GameObject("Ice Test");
        _created.Add(material);
        _created.Add(root);
        GameObject built = WaterSurfaceBuilder.Build(water, material, material, root.transform, 0f);

        Assert.IsTrue(Array.Exists(ice, cell => cell), "The snow biome holds no frozen water in the test world");

        foreach (Transform part in built.transform)
        {
            _created.Add(part.GetComponent<MeshFilter>().sharedMesh);

            if (part.name.EndsWith(" ice"))
                Assert.IsNotNull(part.GetComponent<MeshCollider>(), $"Ice part {part.name} cannot be walked on");
            else
                Assert.IsNull(part.GetComponent<MeshCollider>(), $"Liquid water part {part.name} is solid");
        }
    }

    [Test]
    public void SeaLevelZeroMakesNoCoastAndNoSea()
    {
        WorldGenerationConfig config = Config(0f);

        Assert.IsFalse(CoastShaper.Active(config), "The coast must switch itself off without a sea level");

        WaterMap water = Hydrate(config, null, out _);

        Assert.IsFalse(Array.Exists(water.Kinds, kind => kind == (byte)WaterKind.Sea), "Sea cells appeared with SeaLevel 0");
    }

    [Test]
    public void SameSeedGivesTheSameWater()
    {
        WaterMap first = Hydrate(Config(SEA_LEVEL), null, out _);
        WaterMap second = Hydrate(Config(SEA_LEVEL), null, out _);

        CollectionAssert.AreEqual(WaterMapFormat.Write(first), WaterMapFormat.Write(second), "The same seed produced different water");
    }

    [Test]
    public void OffshoreSeaMeetsTheInlandSeaVertexForVertex()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out _);
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        WaterMeshPart ring = WaterMeshes.Offshore(water, parts, config.Water.OffshoreReach);
        var ringVertices = new HashSet<Vector2Int>();

        foreach (Vector3 vertex in ring.Vertices)
            ringVertices.Add(new Vector2Int(Mathf.RoundToInt(vertex.x * 100f), Mathf.RoundToInt(vertex.z * 100f)));

        int border = 0;

        foreach (WaterMeshPart part in parts)
        {
            if (part.Kind != WaterKind.Sea)
                continue;

            foreach (Vector3 vertex in part.Vertices)
            {
                if (vertex.x > 1e-3f && vertex.z > 1e-3f && vertex.x < WORLD - 1e-3f && vertex.z < WORLD - 1e-3f)
                    continue;

                border++;
                Assert.IsTrue(ringVertices.Contains(new Vector2Int(Mathf.RoundToInt(vertex.x * 100f), Mathf.RoundToInt(vertex.z * 100f))), $"Border sea vertex {vertex} has no offshore partner");
            }
        }

        Assert.Greater(border, 0, "The sea never reaches the world border");
    }

    [Test]
    public void ObliqueBridgeCoversBothRoadEdges()
    {
        var config = ScriptableObject.CreateInstance<WorldGenerationConfig>();
        _created.Add(config);

        var map = new HeightMap(257, 256, MAX_HEIGHT);
        var water = new WaterMap(32, 8f, 256f, 0f);
        var river = new RiverPath();

        foreach (float z in new[] { 0f, 256f })
            river.Points.Add(new RiverPoint { Position = new Vector2(128f, z), Surface = 100f, Bed = 97f, Width = 20f });

        water.Rivers.Add(river);

        for (int z = 0; z <= 256; z++)
            for (int x = 0; x <= 256; x++)
                map.Heights[z * 257 + x] = (Mathf.Abs(x - 128f) < 10f ? 97f : 103f) / MAX_HEIGHT;

        var roads = new[] { new Road(new Vector2[] { new(40f, 40f), new(216f, 216f) }, 8f, RoadKind.Highway) };
        water.FindCrossings(roads);

        var carver = new TerrainCarver(config, water);
        HeightMap carved = carver.CarveHighways(map, roads, null);
        WaterCrossings.Finish(water, carved, config, carver.Bridges);

        Vector2 direction = new Vector2(1f, 1f).normalized;
        Vector2 side = new Vector2(-direction.y, direction.x) * 4f;
        int wet = 0;

        for (float along = 0f; along < 250f; along += 1f)
        {
            Vector2 axis = new Vector2(40f, 40f) + direction * along;

            foreach (Vector2 edge in new[] { axis + side, axis - side })
            {
                if (Mathf.Abs(edge.x - 128f) > 10f)
                    continue;

                wet++;
                Assert.IsTrue(WaterCrossings.OverSpan(water, edge), $"The road edge at {edge} runs through the river beside the bridge");
            }
        }

        Assert.Greater(wet, 0, "The test road never crossed the river");

        var root = new GameObject("Bridge Test");
        _created.Add(root);
        GameObject bridges = WaterCrossingBuilder.Build(water, null, null, root.transform);
        Transform bridge = bridges.transform.Find("Bridge");

        Assert.IsNotNull(bridge, "No bridge was built over a 20 m river");
        Assert.IsNotNull(bridge.GetComponent<MeshCollider>(), "The bridge deck has no collider");
        Assert.Greater(bridge.position.y, 100f, "The bridge deck sits under the water");
        _created.Add(bridge.GetComponent<MeshFilter>().sharedMesh);
    }

    [Test]
    public void WorldEdgeBuildsTheOffshoreSeaFloorAndWalls()
    {
        WorldGenerationConfig config = Config(SEA_LEVEL);
        WaterMap water = Hydrate(config, null, out _);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var root = new GameObject("World Edge Test");
        _created.Add(material);
        _created.Add(root);

        float reach = WorldEdgeBuilder.OffshoreReach(config);
        GameObject waterRoot = WaterSurfaceBuilder.Build(water, material, material, root.transform, reach);
        GameObject edgeRoot = WorldEdgeBuilder.Build(config, material, root.transform);

        Assert.Greater(reach, 0f, "The coast is on but nothing is drawn past the border");
        Assert.IsNotNull(waterRoot.transform.Find("Sea offshore"), "The offshore sea mesh is missing");
        Assert.Greater(waterRoot.transform.Find("Sea offshore").GetComponent<MeshFilter>().sharedMesh.triangles.Length, 0);
        Assert.IsNotNull(edgeRoot.transform.Find("Sea floor offshore"), "The sea floor past the border is missing");
        Assert.AreEqual(4, edgeRoot.GetComponentsInChildren<BoxCollider>().Length, "The world needs four border walls");

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            _created.Add(filter.sharedMesh);
    }

    private static HashSet<int> RiverCells(WaterMap water)
    {
        var cells = new HashSet<int>();

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
                cells.Add(water.CellIndex(point.Position.x, point.Position.y));
        }

        return cells;
    }

    private WorldGenerationConfig Config(float seaLevel)
    {
        var config = ScriptableObject.CreateInstance<WorldGenerationConfig>();
        _created.Add(config);

        SerializedFields.Set(config, "Seed", 1613419011);
        SerializedFields.Set(config, "WorldSize", WORLD);
        SerializedFields.Set(config, "SeaLevel", seaLevel);
        SerializedFields.Set(config.Water, "RiverStartArea", 0.12f);
        SerializedFields.Set(config.Water, "LakeDensity", 1f);
        SerializedFields.Set(config.Water, "PondDensity", 1f);
        SerializedFields.Set(config.Water, "MinLakeArea", 8000f);
        SerializedFields.Set(config.Water, "LakeSpacing", 200f);
        SerializedFields.Set(config.Water.Stamps, "Enabled", false);

        return config;
    }

    private static WaterClimate Climate()
    {
        var map = new BiomeMap(WORLD / 8, WORLD);

        for (int z = 0; z < WORLD / 8; z++)
        {
            for (int x = 0; x < WORLD / 8; x++)
            {
                float metres = (x + 0.5f) * 8f;
                map.Set(x, z, (byte)(metres < SNOW ? 1 : metres > DESERT ? 2 : 0));
            }
        }

        return WaterClimate.From(map, new[] { false, false, true }, new[] { false, true, false }, new[] { true, false, false }, new[] { 1f, 1f, 0f });
    }

    private static WaterMap Hydrate(WorldGenerationConfig config, WaterClimate climate, out Hydrology hydrology)
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

        WorldMapPipeline.Hydrate(config, map, out WaterMap water, out hydrology, climate);

        return water;
    }

    private static float Bowl(float x, float z, float cx, float cz, float radius)
    {
        float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (radius * radius);

        return Mathf.Max(0f, 1f - d2) * Mathf.Max(0f, 1f - d2);
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
}
