using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsSet
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["set.hubs"] = Hubs;
        registry["set.hubsEmpty"] = HubsEmpty;
        registry["set.roads"] = Roads;
        registry["set.edges"] = Edges;
        registry["set.path"] = Path;
        registry["set.smooth"] = Smooth;
        registry["set.carve"] = Carve;
        registry["set.settlements"] = Settlements;
        registry["set.tiles"] = Tiles;
        registry["set.pads"] = Pads;
        registry["set.lots"] = Lots;
        registry["set.mix"] = Mix;
        registry["set.index"] = Index;
        registry["set.carveStreets"] = CarveStreets;
        registry["set.topology"] = Topology;
        registry["set.dirt"] = Dirt;
    }

    private static WorldGenBenchOp Hubs()
    {
        HeightMap map = null;

        return new WorldGenBenchOp("set.hubs", context =>
        {
            List<Hub> hubs = new HubPlacer(context.Profile.Config, map).Place();
            context.Put("hubs", hubs);
        })
        {
            Setup = context =>
            {
                map = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;

                if (context.Args.Has("cities"))
                    WorldGenBenchProfile.Assign(context.Profile.Config.CityProfile, "Count", context.Args.Int("cities", 3));

                context.Count("requestedCities", context.Profile.Config.CityProfile.Count);
            },
            Verify = context =>
            {
                var hubs = context.Get<List<Hub>>("hubs");
                context.Count("hubs", hubs.Count);

                var types = new int[4];

                foreach (Hub hub in hubs)
                {
                    context.Require(hub.Position.x >= 0f && hub.Position.x <= map.WorldSize
                        && hub.Position.y >= 0f && hub.Position.y <= map.WorldSize, "A settlement must stand inside the world.");

                    types[(int)hub.Type]++;
                }

                context.Count("cities", types[(int)SettlementType.City]);
                context.Count("towns", types[(int)SettlementType.Town]);
                context.Count("countryTowns", types[(int)SettlementType.CountryTown]);
                context.Count("ghostTowns", types[(int)SettlementType.GhostTown]);
                context.Print("hubs", Fingerprint(hubs));
            }
        };
    }

    private static WorldGenBenchOp HubsEmpty()
    {
        HeightMap map = null;

        return new WorldGenBenchOp("set.hubsEmpty", context =>
        {
            List<Hub> hubs = new HubPlacer(context.Profile.Config, map).Place();
            context.Put("hubs", hubs);
        })
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                string shape = context.Args.Text("shape", "flooded");

                map = new HeightMap(config.HeightMapResolution, config.WorldSize, config.MaxHeight);

                if (shape == "steep")
                {
                    WorldGenBenchProfile.Assign(config, "MaxHubRelief", 0.01f);
                    WorldGenBenchProfile.Assign(config, "MaxTileRelief", 1f);

                    for (int index = 0; index < map.Heights.Length; index++)
                        map.Heights[index] = (index % 97) / 97f;
                }
                else
                {
                    WorldGenBenchProfile.Assign(config, "SeaLevel", config.MaxHeight);

                    for (int index = 0; index < map.Heights.Length; index++)
                        map.Heights[index] = 0f;
                }

                context.Count("shape", shape == "steep" ? 1 : 0);
            },
            Verify = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;

                var hubs = context.Get<List<Hub>>("hubs");
                context.Count("hubs", hubs.Count);

                List<RegionalLink> plan = new RegionalGraphPlanner(config, map).Plan(hubs);
                List<SettlementLayout> settlements = new SettlementPlanner(config, context.Profile.Pois, map).Plan(hubs, plan);
                RoadGraph graph = new RoadPlanner(config, map).Plan(hubs, plan, settlements);

                context.Count("links", plan.Count);
                context.Count("settlements", settlements.Count);
                context.Count("graphEdges", graph.Edges.Count);
            }
        };
    }

    private static WorldGenBenchOp Roads()
    {
        HeightMap map = null;
        List<Hub> hubs = null;
        List<RegionalLink> plan = null;
        List<SettlementLayout> settlements = null;

        return new WorldGenBenchOp("set.roads", context =>
        {
            RoadGraph graph = new RoadPlanner(context.Profile.Config, map).Plan(hubs, plan, settlements);
            context.Put("graph", graph);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "settlements");
                map = frozen.PaddedHeights;
                hubs = frozen.Hubs;
                settlements = frozen.Settlements;

                int limit = context.Args.Int("hubs", hubs.Count);
                plan = new List<RegionalLink>();

                foreach (RegionalLink link in frozen.Plan)
                {
                    if (link.From < limit && link.To < limit)
                        plan.Add(link);
                }

                if (context.Args.Has("roadCell"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "RoadCellSize", context.Args.Float("roadCell", 32f));

                context.Count("hubs", Math.Min(limit, hubs.Count));
                context.Count("links", plan.Count);
                context.Count("roadCellSize", context.Profile.Config.RoadCellSize);
            },
            Verify = context =>
            {
                var graph = context.Get<RoadGraph>("graph");
                List<Road> roads = graph.BuildRoads(context.Profile.Config);

                double length = 0d;
                int points = 0;
                int junctions = 0;

                foreach (Road road in roads)
                {
                    points += road.Points.Length;

                    for (int index = 1; index < road.Points.Length; index++)
                        length += Vector2.Distance(road.Points[index - 1], road.Points[index]);
                }

                foreach (RoadNode node in graph.Nodes)
                {
                    if (node.Kind == RoadNodeKind.Junction && graph.Degree(node.Id) >= 3)
                        junctions++;
                }

                context.Count("roads", roads.Count);
                context.Count("roadPoints", points);
                context.Count("roadLength", length);
                context.Count("junctions", junctions);
                context.Print("roads", Fingerprint(roads));
            }
        };
    }

    private static WorldGenBenchOp Edges()
    {
        List<Hub> hubs = null;
        HeightMap map = null;
        List<RegionalLink> links = null;

        return new WorldGenBenchOp("set.edges", context => links = new RegionalGraphPlanner(context.Profile.Config, map).Plan(hubs))
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                hubs = frozen.Hubs;
                map = frozen.RawHeights;

                if (context.Args.Has("linkRatio"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "TargetLinkRatio", context.Args.Float("linkRatio", 1.3f));

                context.Count("hubs", hubs.Count);
                context.Count("linkRatio", context.Profile.Config.TargetLinkRatio);
            },
            Verify = context =>
            {
                int backbone = 0;

                foreach (RegionalLink link in links)
                {
                    if (link.Backbone)
                        backbone++;
                }

                context.Count("links", links.Count);
                context.Count("backbone", backbone);
                context.Count("loops", links.Count - backbone);
                context.Require(hubs.Count < 2 || backbone == hubs.Count - 1, "The regional backbone must span every settlement.");
            }
        };
    }

    private static WorldGenBenchOp Path()
    {
        RoadPlanner planner = null;
        Func<Vector2, Vector2, List<int>> find = null;
        Hub from = null;
        Hub to = null;

        return new WorldGenBenchOp("set.path", context =>
        {
            List<int> path = find(from.Position, to.Position);
            context.Put("path", path);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                planner = new RoadPlanner(context.Profile.Config, frozen.RawHeights);
                find = WorldGenBenchReflect.Bind<Func<Vector2, Vector2, List<int>>>(planner, "FindPath");

                List<Hub> hubs = frozen.Hubs;
                context.Require(hubs.Count >= 2, "Path routing needs at least two settlements.");

                from = hubs[0];
                to = Pick(hubs, from, context.Args.Text("route", "long"));

                context.Count("routeMetres", Vector2.Distance(from.Position, to.Position));
            },
            Verify = context =>
            {
                var path = context.Get<List<int>>("path");
                context.Count("cells", path == null ? 0 : path.Count);
            }
        };
    }

    private static WorldGenBenchOp Smooth()
    {
        RoadSmoother smoother = null;
        Vector2[] input = null;

        return new WorldGenBenchOp("set.smooth", context =>
        {
            Vector2[] output = smoother.Smooth(input);
            output = RoadSmoother.EnforceRadius(output, context.Profile.Config.HighwayMinCurveRadius, 0f, 0f, 160);
            context.Put("output", output);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "heights");
                WorldGenerationConfig config = context.Profile.Config;

                smoother = new RoadSmoother(frozen.RawHeights)
                {
                    SimplifyTolerance = config.RoadSimplifyTolerance,
                    ChaikinPasses = config.RoadSmoothPasses,
                    RelaxPasses = config.RoadRelaxPasses,
                    RelaxStrength = config.RoadRelaxStrength,
                    Corridor = config.RoadCorridor,
                    MaxGrade = config.RoadMaxGrade,
                    Spacing = config.RoadPointSpacing
                };

                int count = context.Args.Int("points", 400);
                bool jagged = context.Args.Bool("jagged", true);
                input = new Vector2[count];

                var random = new Random(1234u);
                float span = config.WorldSize * 0.4f;

                for (int index = 0; index < count; index++)
                {
                    float t = index / (float)(count - 1);
                    float wobble = jagged ? random.NextFloat(-config.RoadCellSize, config.RoadCellSize) : 0f;
                    input[index] = new Vector2(config.WorldSize * 0.3f + t * span, config.WorldSize * 0.5f + wobble);
                }

                context.Count("inputPoints", count);
            },
            Verify = context =>
            {
                var output = context.Get<Vector2[]>("output");
                context.Require(output != null && output.Length >= 2, "Smoothing must keep a usable polyline.");
                context.Count("outputPoints", output.Length);
                context.Count("minRadius", RoadSmoother.MinRadius(output, context.Profile.Config.RoadPointSpacing, 0f, 0f));

                Vector2[] simplified = RoadSmoother.Simplify(input, context.Profile.Config.RoadSimplifyTolerance);
                context.Count("simplifiedPoints", simplified.Length);
                context.Count("chaikinPoints", RoadSmoother.Chaikin(simplified, context.Profile.Config.RoadSmoothPasses).Length);
            }
        };
    }

    private static WorldGenBenchOp Carve()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap source = null;

        return new WorldGenBenchOp("set.carve", context =>
        {
            var carver = new TerrainCarver(context.Profile.Config);

            HeightMap padded = carver.CarveSettlements(source, frozen.Settlements);
            HeightMap streets = carver.CarveStreets(padded, frozen.Settlements, out float[] mask);
            HeightMap carved = carver.CarveHighways(streets, frozen.Roads.Roads, mask);

            context.Put("carved", carved);
            context.Put("mask", mask);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "roads");
                context.Count("roads", frozen.Roads.Roads.Count);
                context.Count("streets", frozen.Roads.Streets.Count);
                context.Count("settlements", frozen.Settlements.Count);
                context.Count("cells", (long)frozen.RawHeights.Resolution * frozen.RawHeights.Resolution);
            },
            Prepare = context => source = frozen.CopyRaw(),
            Verify = context =>
            {
                var carved = context.Get<HeightMap>("carved");
                var mask = (float[])context.State["mask"];

                WorldGenBenchOpsHgt.Inspect(context, carved, "carved");
                context.Require(mask.Length == carved.Heights.Length, "The road mask must match the height map.");

                double covered = 0d;

                foreach (float value in mask)
                    covered += value;

                context.Count("maskCoverage", covered / mask.Length);
                context.Print("mask", WorldGenBenchArgs.Hash(mask));
            }
        };
    }

    private static WorldGenBenchOp CarveStreets()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap source = null;

        return new WorldGenBenchOp("set.carveStreets", context =>
        {
            HeightMap carved = new TerrainCarver(context.Profile.Config).CarveStreets(source, frozen.Settlements, out float[] mask);
            context.Put("carved", carved);
            context.Put("mask", mask);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "roads");
                context.Count("settlements", frozen.Settlements.Count);
                context.Count("streets", frozen.Roads.Streets.Count);
            },
            Prepare = context => source = frozen.CopyPadded(),
            Verify = context =>
            {
                WorldGenBenchOpsHgt.Inspect(context, context.Get<HeightMap>("carved"), "streets");
                context.Print("streetMask", WorldGenBenchArgs.Hash((float[])context.State["mask"]));
            }
        };
    }

    private static WorldGenBenchOp Settlements()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap heights = null;
        List<Hub> hubs = null;

        return new WorldGenBenchOp("set.settlements", context =>
        {
            List<SettlementLayout> settlements = new SettlementPlanner(context.Profile.Config, context.Profile.Pois, heights)
                .Plan(hubs, frozen.Plan);

            context.Put("settlements", settlements);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                context.Count("hubs", frozen.Hubs.Count);
            },
            Prepare = context =>
            {
                heights = frozen.CopyRaw();
                hubs = Clone(frozen.Hubs);
            },
            Verify = context =>
            {
                var settlements = context.Get<List<SettlementLayout>>("settlements");
                int tiles = 0;
                int streets = 0;
                int frontages = 0;
                int gateways = 0;
                int empty = 0;
                int violations = 0;

                foreach (SettlementLayout settlement in settlements)
                {
                    tiles += settlement.Tiles.Count;
                    streets += settlement.Streets.Count;
                    frontages += settlement.Frontages.Count;
                    gateways += settlement.Gateways.Count;
                    violations += settlement.TopologyViolations;

                    if (settlement.IsEmpty)
                        empty++;
                }

                context.Count("settlements", settlements.Count);
                context.Count("emptySettlements", empty);
                context.Count("tiles", tiles);
                context.Count("streets", streets);
                context.Count("frontages", frontages);
                context.Count("gateways", gateways);
                context.Count("topologyViolations", violations);
            }
        };
    }

    private static WorldGenBenchOp Tiles()
    {
        WorldGenBenchFrozen frozen = null;
        List<Hub> hubs = null;
        List<SettlementLayout> layouts = null;
        SettlementType type = SettlementType.Town;

        return new WorldGenBenchOp("set.tiles", context =>
        {
            layouts = new SettlementPlanner(context.Profile.Config, context.Profile.Pois, frozen.RawHeights).Plan(hubs, new List<RegionalLink>());
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                context.Require(frozen.Hubs.Count > 0, "Tile layout needs a settlement site.");

                type = (SettlementType)Enum.Parse(typeof(SettlementType), context.Args.Text("type", "Town"), true);

                if (context.Args.Has("loopDensity"))
                    WorldGenBenchProfile.Assign(context.Profile.Config.Profile(type), "LoopDensity", context.Args.Float("loopDensity", 0.5f));

                context.Count("loopDensity", context.Profile.Config.Profile(type).LoopDensity);
            },
            Prepare = context =>
            {
                Hub source = frozen.Hubs[0];
                hubs = new List<Hub> { new(source.Position, source.Radius, source.Relief, source.Houses, type) };
            },
            Verify = context =>
            {
                SettlementLayout layout = layouts[0];

                context.Count("tiles", layout.Tiles.Count);
                context.Count("streets", layout.Streets.Count);
                context.Count("frontages", layout.Frontages.Count);
                context.Count("topologyViolations", layout.TopologyViolations);
                context.Count("radius", layout.Radius);
            }
        };
    }

    private static WorldGenBenchOp Pads()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap source = null;

        return new WorldGenBenchOp("set.pads", context =>
        {
            HeightMap carved = new TerrainCarver(context.Profile.Config).CarveSettlements(source, frozen.Settlements);
            context.Put("carved", carved);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "settlements");

                int tiles = 0;

                foreach (SettlementLayout settlement in frozen.Settlements)
                    tiles += settlement.Tiles.Count;

                context.Count("settlements", frozen.Settlements.Count);
                context.Count("tiles", tiles);
            },
            Prepare = context => source = frozen.CopyRaw(),
            Verify = context => WorldGenBenchOpsHgt.Inspect(context, context.Get<HeightMap>("carved"), "pads")
        };
    }

    private static WorldGenBenchOp Lots()
    {
        WorldGenBenchFrozen frozen = null;
        LotSubdivider subdivider = null;
        SettlementLayout layout = null;

        return new WorldGenBenchOp("set.lots", context =>
        {
            var random = new Random(303u);
            subdivider.Fill(layout, frozen.Proximity, ref random);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                context.Require(frozen.Settlements.Exists(settlement => !settlement.IsEmpty), "Lot subdivision needs a settlement with tiles.");

                if (context.Args.Has("lotGap"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "LotGap", context.Args.Float("lotGap", 6f));
            },
            Prepare = context =>
            {
                subdivider = new LotSubdivider(context.Profile.Config, context.Profile.Pois);
                layout = Detach(frozen.Settlements.Find(settlement => !settlement.IsEmpty));
            },
            Verify = context =>
            {
                context.Count("frontages", layout.Frontages.Count);
                context.Count("lots", layout.Lots.Count);
                context.Count("skippedOverlap", subdivider.SkippedOverlap);
                context.Count("skippedOnRoad", subdivider.SkippedOnRoad);
                context.Count("skippedOutside", subdivider.SkippedOutside);
                context.Count("skippedDepth", subdivider.SkippedDepth);
                context.Count("skippedDensity", subdivider.SkippedDensity);
                context.Count("skippedNoPrefab", subdivider.SkippedNoPrefab);
            }
        };
    }

    private static WorldGenBenchOp Mix()
    {
        HeightMap map = null;

        return new WorldGenBenchOp("set.mix", context =>
        {
            List<Hub> hubs = new HubPlacer(context.Profile.Config, map).Place();
            context.Put("hubs", hubs);
        })
        {
            Setup = context =>
            {
                map = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                float scale = context.Args.Float("scale", 1f);
                WorldGenerationConfig config = context.Profile.Config;

                foreach (SettlementType type in new[] { SettlementType.City, SettlementType.Town, SettlementType.CountryTown, SettlementType.GhostTown })
                {
                    SettlementTypeProfile profile = config.Profile(type);
                    WorldGenBenchProfile.Assign(profile, "Count", Mathf.RoundToInt(profile.Count * scale));
                }

                context.Count("scale", scale);
            },
            Verify = context =>
            {
                var hubs = context.Get<List<Hub>>("hubs");
                var types = new int[4];

                foreach (Hub hub in hubs)
                    types[(int)hub.Type]++;

                context.Count("settlements", hubs.Count);
                context.Count("cities", types[0]);
                context.Count("towns", types[1]);
                context.Count("countryTowns", types[2]);
                context.Count("ghostTowns", types[3]);
            }
        };
    }

    private static WorldGenBenchOp Index()
    {
        WorldGenerationConfig config = null;
        List<Road> roads = null;
        List<Lot> lots = null;
        Vector2[] queries = null;
        int segments = 1000;

        return new WorldGenBenchOp("set.index", context =>
        {
            var proximity = new RoadProximity(config.WorldSize, config.RoadCellSize);

            foreach (Road road in roads)
                proximity.Add(road);

            var index = new LotIndex(config.WorldSize, 32f);

            foreach (Lot lot in lots)
                index.Add(lot);

            var heap = new MinHeap(1024);
            int hits = 0;

            foreach (Vector2 point in queries)
            {
                if (proximity.IsWithin(point, 12f))
                    hits++;

                if (proximity.TryNearestDirection(point, 64f, out _))
                    hits++;
            }

            foreach (Lot lot in lots)
            {
                if (index.Overlaps(lot, 0.5f))
                    hits++;
            }

            for (int step = 0; step < 4096; step++)
                heap.Push(step, (step * 37) % 1024);

            while (heap.TryPop(out _))
                hits++;

            context.Count("hits", hits);
        })
        {
            Setup = context =>
            {
                config = context.Profile.Config;
                segments = context.Args.Int("segments", 1000);

                var random = new Random(6161u);
                roads = new List<Road>();

                for (int index = 0; index < segments; index++)
                {
                    var start = new Vector2(random.NextFloat(0f, config.WorldSize), random.NextFloat(0f, config.WorldSize));
                    var end = start + new Vector2(random.NextFloat(-64f, 64f), random.NextFloat(-64f, 64f));
                    roads.Add(RoadKindProfile.Create(config, new[] { start, end }, RoadKind.Highway));
                }

                lots = new List<Lot>();

                for (int index = 0; index < segments; index++)
                {
                    var center = new Vector2(random.NextFloat(0f, config.WorldSize), random.NextFloat(0f, config.WorldSize));
                    lots.Add(new Lot(center, 12f, 14f, new Vector2(1f, 0f), DistrictType.Residential));
                }

                queries = new Vector2[10000];

                for (int index = 0; index < queries.Length; index++)
                    queries[index] = new Vector2(random.NextFloat(0f, config.WorldSize), random.NextFloat(0f, config.WorldSize));

                context.Count("segments", segments);
                context.Count("lots", lots.Count);
                context.Count("queries", queries.Length);
            }
        };
    }

    private static WorldGenBenchOp Topology()
    {
        WorldGenBenchFrozen frozen = null;
        RoadNetworkReport report = null;

        return new WorldGenBenchOp("set.topology", context =>
        {
            report = RoadNetworkDiagnostics.Measure(context.Profile.Config, frozen.Roads, frozen.Settlements, frozen.Placements,
                frozen.PaddedHeights, frozen.CarvedHeights);
        })
        {
            Setup = context => frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements"),
            Verify = context =>
            {
                foreach ((string name, double value) in report.Metrics)
                    context.Count(name, value);

                foreach (KeyValuePair<string, int> violation in report.Violations)
                    context.Count("violation." + violation.Key, violation.Value);

                context.Require(report.Hard == 0, "The generated road topology has hard violations:\n" + report.ToText());
            }
        };
    }

    private static WorldGenBenchOp Dirt()
    {
        WorldGenBenchFrozen frozen = null;
        RoadGraph graph = null;
        List<RuralSite> sites = null;

        return new WorldGenBenchOp("set.dirt", context =>
        {
            sites = new DirtAccessPlanner(context.Profile.Config, frozen.PaddedHeights).Plan(graph, frozen.Settlements, frozen.Roads.Streets);
        })
        {
            Setup = context => frozen = WorldGenBenchFixtures.Frozen(context.Profile, "roads"),
            Prepare = context => graph = new RoadPlanner(context.Profile.Config, frozen.PaddedHeights).Plan(frozen.Hubs, frozen.Plan, frozen.Settlements),
            Verify = context =>
            {
                int dirt = 0;

                foreach (RoadEdge edge in graph.Edges)
                {
                    if (edge.Alive && edge.Kind == RoadKind.DirtAccess)
                        dirt++;
                }

                context.Count("sites", sites.Count);
                context.Count("dirtEdges", dirt);
                context.Require(dirt == sites.Count, "Every rural site must own exactly one dirt access road.");
            }
        };
    }

    private static List<Hub> Clone(List<Hub> hubs)
    {
        var copy = new List<Hub>(hubs.Count);

        foreach (Hub hub in hubs)
            copy.Add(new Hub(hub.Position, hub.Radius, hub.Relief, hub.Houses, hub.Type));

        return copy;
    }

    private static SettlementLayout Detach(SettlementLayout source)
    {
        var copy = new SettlementLayout(source.Hub, source.Index, source.Angle, source.TileSize);

        foreach (SettlementTile tile in source.Tiles)
        {
            SettlementTile clone = copy.AddTile(tile.I, tile.J);
            clone.Ports = tile.Ports;
            clone.ArterialPorts = tile.ArterialPorts;
            clone.GatewayPorts = tile.GatewayPorts;
            clone.District = tile.District;
        }

        copy.Streets.AddRange(source.Streets);
        copy.StreetNodes.AddRange(source.StreetNodes);
        copy.Frontages.AddRange(source.Frontages);
        copy.Neighbours.AddRange(source.Neighbours);
        copy.Measure();

        return copy;
    }

    private static Hub Pick(List<Hub> hubs, Hub from, string kind)
    {
        Hub best = hubs[1];
        float bestValue = kind == "short" ? float.MaxValue : float.MinValue;

        foreach (Hub hub in hubs)
        {
            if (hub == from)
                continue;

            float distance = Vector2.Distance(from.Position, hub.Position);

            if (kind == "short" ? distance < bestValue : distance > bestValue)
            {
                bestValue = distance;
                best = hub;
            }
        }

        return best;
    }

    public static string Fingerprint(List<Hub> hubs)
    {
        var text = new System.Text.StringBuilder();

        foreach (Hub hub in hubs)
            text.Append(hub.Position.x).Append(',').Append(hub.Position.y).Append(',').Append((int)hub.Type).Append(';');

        return WorldGenBenchArgs.Hash(text.ToString());
    }

    public static string Fingerprint(List<Road> roads)
    {
        var text = new System.Text.StringBuilder();

        foreach (Road road in roads)
        {
            foreach (Vector2 point in road.Points)
                text.Append(point.x).Append(',').Append(point.y).Append(';');

            text.Append((int)road.Kind).Append('|');
        }

        return WorldGenBenchArgs.Hash(text.ToString());
    }
}
