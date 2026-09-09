using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsSet
{
    public delegate void LayoutStep(CityLayout layout, IReadOnlyList<Road> trunks, ref Random random);

    public delegate void FillStep(CityLayout layout, RoadProximity roads, ref Random random);

    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["set.hubs"] = Hubs;
        registry["set.hubsEmpty"] = HubsEmpty;
        registry["set.roads"] = Roads;
        registry["set.edges"] = Edges;
        registry["set.path"] = Path;
        registry["set.smooth"] = Smooth;
        registry["set.carve"] = Carve;
        registry["set.cities"] = Cities;
        registry["set.streets"] = Streets;
        registry["set.stitch"] = Stitch;
        registry["set.lots"] = Lots;
        registry["set.retry"] = Retry;
        registry["set.index"] = Index;
        registry["set.carveStreets"] = CarveStreets;
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

                if (context.Args.Has("hubCount"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "HubCount", context.Args.Int("hubCount", 81));

                context.Count("requested", context.Profile.Config.HubCount);
            },
            Verify = context =>
            {
                var hubs = context.Get<List<Hub>>("hubs");
                context.Count("hubs", hubs.Count);

                int cities = 0;
                int towns = 0;
                int villages = 0;

                foreach (Hub hub in hubs)
                {
                    context.Require(hub.Position.x >= 0f && hub.Position.x <= map.WorldSize
                        && hub.Position.y >= 0f && hub.Position.y <= map.WorldSize, "A hub must stand inside the world.");

                    if (hub.Tier == SettlementTier.City)
                        cities++;
                    else if (hub.Tier == SettlementTier.Town)
                        towns++;
                    else
                        villages++;
                }

                context.Count("cities", cities);
                context.Count("towns", towns);
                context.Count("villages", villages);
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
                var hubs = context.Get<List<Hub>>("hubs");
                context.Count("hubs", hubs.Count);

                var network = new RoadNetwork();
                network.Hubs.AddRange(hubs);
                List<Road> roads = new RoadPlanner(context.Profile.Config, map).Plan(network.Hubs);
                context.Count("roads", roads.Count);

                List<CityLayout> cities = new CityPlanner(context.Profile.Config, context.Profile.Pois, map)
                    .Plan(network.Hubs, roads);
                context.Count("cities", cities.Count);
            }
        };
    }

    private static WorldGenBenchOp Roads()
    {
        HeightMap map = null;
        List<Hub> hubs = null;

        return new WorldGenBenchOp("set.roads", context =>
        {
            List<Road> roads = new RoadPlanner(context.Profile.Config, map).Plan(hubs);
            context.Put("roads", roads);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                map = frozen.RawHeights;
                hubs = new List<Hub>(frozen.Hubs);

                int limit = context.Args.Int("hubs", hubs.Count);

                if (limit < hubs.Count)
                    hubs = hubs.GetRange(0, limit);

                if (context.Args.Has("extraEdges"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "RoadExtraEdges", context.Args.Int("extraEdges", 60));

                if (context.Args.Has("roadCell"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "RoadCellSize", context.Args.Float("roadCell", 32f));

                context.Count("hubs", hubs.Count);
                context.Count("roadCellSize", context.Profile.Config.RoadCellSize);
            },
            Verify = context =>
            {
                var roads = context.Get<List<Road>>("roads");
                context.Count("roads", roads.Count);

                double length = 0d;
                int points = 0;

                foreach (Road road in roads)
                {
                    points += road.Points.Length;

                    for (int index = 1; index < road.Points.Length; index++)
                        length += Vector2.Distance(road.Points[index - 1], road.Points[index]);
                }

                context.Count("roadPoints", points);
                context.Count("roadLength", length);
                context.Print("roads", Fingerprint(roads));
            }
        };
    }

    private static WorldGenBenchOp Edges()
    {
        HeightMap map = null;
        List<Hub> hubs = null;
        RoadPlanner planner = null;
        Func<List<Hub>, List<(int, int)>> build = null;
        Action<List<Hub>, List<(int, int)>> extra = null;
        List<(int, int)> edges = null;

        return new WorldGenBenchOp("set.edges", context =>
        {
            edges = build(hubs);
            extra(hubs, edges);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                map = frozen.RawHeights;
                hubs = new List<Hub>(frozen.Hubs);

                WorldGenBenchProfile.Assign(context.Profile.Config, "RoadExtraEdges", context.Args.Int("extraEdges", 60));

                planner = new RoadPlanner(context.Profile.Config, map);
                build = WorldGenBenchReflect.Bind<Func<List<Hub>, List<(int, int)>>>(planner, "BuildEdges");
                extra = WorldGenBenchReflect.Bind<Action<List<Hub>, List<(int, int)>>>(planner, "AddExtraEdges");

                context.Count("hubs", hubs.Count);
                context.Count("extraRequested", context.Profile.Config.RoadExtraEdges);
            },
            Verify = context =>
            {
                context.Count("edges", edges.Count);
                context.Require(hubs.Count < 2 || edges.Count >= hubs.Count - 1,
                    "The road graph must at least span every hub.");
            }
        };
    }

    private static WorldGenBenchOp Path()
    {
        RoadPlanner planner = null;
        Func<Hub, Hub, List<int>> find = null;
        Hub from = null;
        Hub to = null;

        return new WorldGenBenchOp("set.path", context =>
        {
            List<int> path = find(from, to);
            context.Put("path", path);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "hubs");
                planner = new RoadPlanner(context.Profile.Config, frozen.RawHeights);
                find = WorldGenBenchReflect.Bind<Func<Hub, Hub, List<int>>>(planner, "FindPath");

                List<Hub> hubs = frozen.Hubs;
                context.Require(hubs.Count >= 2, "Path routing needs at least two hubs.");

                string kind = context.Args.Text("route", "long");
                from = hubs[0];
                to = Pick(hubs, from, kind);

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
            HeightMap carved = new TerrainCarver(context.Profile.Config).Carve(source, frozen.Roads, out float[] mask);
            context.Put("carved", carved);
            context.Put("mask", mask);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "roads");
                context.Count("roads", frozen.Roads.Roads.Count);
                context.Count("hubs", frozen.Roads.Hubs.Count);
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
        float[] mask = null;

        return new WorldGenBenchOp("set.carveStreets", context =>
        {
            HeightMap carved = new TerrainCarver(context.Profile.Config).CarveStreets(source, frozen.Cities, mask);
            context.Put("carved", carved);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                context.Count("cities", frozen.Cities.Count);
                context.Count("streets", frozen.Roads.Streets.Count);
            },
            Prepare = context =>
            {
                source = frozen.CopyCarved();
                mask = frozen.CopyMask();
            },
            Verify = context =>
            {
                WorldGenBenchOpsHgt.Inspect(context, context.Get<HeightMap>("carved"), "streets");
                context.Print("streetMask", WorldGenBenchArgs.Hash(mask));
            }
        };
    }

    private static WorldGenBenchOp Cities()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap heights = null;

        return new WorldGenBenchOp("set.cities", context =>
        {
            List<CityLayout> cities = new CityPlanner(context.Profile.Config, context.Profile.Pois, heights)
                .Plan(frozen.Roads.Hubs, frozen.Roads.Roads);

            context.Put("cities", cities);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");

                if (context.Args.Has("attempts"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "SettlementPlanAttempts", context.Args.Int("attempts", 3));

                context.Count("hubs", frozen.Roads.Hubs.Count);
                context.Count("attempts", context.Profile.Config.SettlementPlanAttempts);
            },
            Prepare = context => heights = frozen.CopyCarved(),
            Verify = context =>
            {
                var cities = context.Get<List<CityLayout>>("cities");
                int streets = 0;
                int lots = 0;
                int met = 0;
                int planned = 0;

                foreach (CityLayout city in cities)
                {
                    streets += city.Streets.Count;
                    lots += city.Lots.Count;
                    met += city.Composition.Met;
                    planned += city.Composition.Planned;
                }

                context.Count("cities", cities.Count);
                context.Count("streets", streets);
                context.Count("lots", lots);
                context.Count("compositionMet", met);
                context.Count("compositionPlanned", planned);
            }
        };
    }

    private static WorldGenBenchOp Streets()
    {
        WorldGenBenchFrozen frozen = null;
        StreetGrower grower = null;
        LayoutStep grow = null;
        CityLayout layout = null;
        RoadProximity index = null;

        return new WorldGenBenchOp("set.streets", context =>
        {
            var random = new Random(77u);
            grow(layout, frozen.Roads.Roads, ref random);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                WorldGenerationConfig config = context.Profile.Config;

                if (context.Args.Has("fillPasses"))
                    WorldGenBenchProfile.Assign(config, "StreetFillPasses", context.Args.Int("fillPasses", 3));

                if (context.Args.Has("spacing"))
                    WorldGenBenchProfile.Assign(config, "StreetSpacingFraction", context.Args.Float("spacing", 0.7f));

                context.Require(frozen.Roads.Hubs.Count > 0, "Street growth needs a hub.");
                context.Count("fillPasses", config.StreetFillPasses);
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                index = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
                grower = new StreetGrower(config, frozen.CarvedHeights, index);
                grow = WorldGenBenchReflect.Bind<LayoutStep>(grower, "Grow");
                layout = Layout(context, frozen.Roads.Hubs[0]);
            },
            Verify = context =>
            {
                context.Count("streets", layout.Streets.Count);
                context.Count("seeded", grower.Seeded);
                context.Count("crowded", grower.Crowded);
                context.Count("tooShort", grower.TooShort);
                context.Count("blocked", grower.Blocked);
                context.Count("deadEnds", grower.DeadEnds);
                context.Count("junctions", grower.Junctions);
            }
        };
    }

    private static WorldGenBenchOp Stitch()
    {
        WorldGenBenchFrozen frozen = null;
        StreetStitcher stitcher = null;
        CityLayout layout = null;

        return new WorldGenBenchOp("set.stitch", context => stitcher.Connect(layout, frozen.Roads.Roads))
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                context.Require(frozen.Cities.Count > 0, "Stitching needs a planned settlement.");
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                var index = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
                stitcher = new StreetStitcher(config, frozen.CarvedHeights, index);

                CityLayout source = frozen.Cities[0];
                layout = Layout(context, source.Hub);

                foreach (Road street in source.Streets)
                {
                    layout.Streets.Add(street);
                    index.Add(street);
                }
            },
            Verify = context =>
            {
                context.Count("islands", stitcher.Islands);
                context.Count("linked", stitcher.Linked);
                context.Count("dropped", stitcher.Dropped);
                context.Count("streets", layout.Streets.Count);
            }
        };
    }

    private static WorldGenBenchOp Lots()
    {
        WorldGenBenchFrozen frozen = null;
        LotSubdivider subdivider = null;
        FillStep fill = null;
        CityLayout layout = null;
        RoadProximity index = null;

        return new WorldGenBenchOp("set.lots", context =>
        {
            var random = new Random(303u);
            fill(layout, index, ref random);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                context.Require(frozen.Cities.Count > 0, "Lot subdivision needs a settlement.");

                if (context.Args.Has("lotGap"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "LotGap", context.Args.Float("lotGap", 6f));
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                subdivider = new LotSubdivider(config, context.Profile.Pois);
                fill = WorldGenBenchReflect.Bind<FillStep>(subdivider, "Fill");

                index = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);

                CityLayout source = frozen.Cities[0];
                layout = Layout(context, source.Hub);

                foreach (Road street in source.Streets)
                {
                    layout.Streets.Add(street);
                    index.Add(street);
                }

                foreach (Road frontage in source.Frontage)
                    layout.Frontage.Add(frontage);
            },
            Verify = context =>
            {
                context.Count("lots", layout.Lots.Count);
                context.Count("skippedOverlap", subdivider.SkippedOverlap);
                context.Count("skippedOnRoad", subdivider.SkippedOnRoad);
                context.Count("skippedOutside", subdivider.SkippedOutside);

                int before = layout.Lots.Count;
                subdivider.Rollback(layout);
                context.Count("rolledBack", before - layout.Lots.Count);
            }
        };
    }

    private static WorldGenBenchOp Retry()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap heights = null;

        return new WorldGenBenchOp("set.retry", context =>
        {
            List<CityLayout> cities = new CityPlanner(context.Profile.Config, context.Profile.Pois, heights)
                .Plan(frozen.Roads.Hubs, frozen.Roads.Roads);

            context.Put("cities", cities);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                WorldGenerationConfig config = context.Profile.Config;

                WorldGenBenchProfile.Assign(config, "SettlementPlanAttempts", context.Args.Int("attempts", 3));

                if (context.Args.Bool("impossible", false))
                {
                    WorldGenBenchProfile.Assign(config.CityProfile, "DowntownFraction", 0.001f);
                    WorldGenBenchProfile.Assign(config.TownProfile, "DowntownFraction", 0.001f);
                }

                context.Count("attempts", config.SettlementPlanAttempts);
            },
            Prepare = context => heights = frozen.CopyCarved(),
            Verify = context =>
            {
                var cities = context.Get<List<CityLayout>>("cities");
                int met = 0;
                int planned = 0;

                foreach (CityLayout city in cities)
                {
                    met += city.Composition.Met;
                    planned += city.Composition.Planned;
                }

                context.Count("compositionMet", met);
                context.Count("compositionPlanned", planned);
                context.Count("cities", cities.Count);
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
                    roads.Add(new Road(new[] { start, end }, config.RoadHalfWidth));
                }

                lots = new List<Lot>();

                for (int index = 0; index < segments; index++)
                {
                    var center = new Vector2(random.NextFloat(0f, config.WorldSize), random.NextFloat(0f, config.WorldSize));
                    lots.Add(new Lot(center, 12f, 14f, new Vector2(1f, 0f), DistrictType.Residential, null));
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

    private static CityLayout Layout(WorldGenBenchContext context, Hub hub)
    {
        WorldGenerationConfig config = context.Profile.Config;
        SettlementProfile profile = config.ProfileFor(hub.Tier);
        var composition = new SettlementComposition(profile, new PoiCensus());

        return new CityLayout(hub, 0f, hub.Radius * config.CityRadiusScale, profile, composition,
            config.CityShapeJitter, 0.3f, 1.7f, 0.9f);
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
            text.Append(hub.Position.x).Append(',').Append(hub.Position.y).Append(',').Append(hub.Radius).Append(';');

        return WorldGenBenchArgs.Hash(text.ToString());
    }

    public static string Fingerprint(List<Road> roads)
    {
        var text = new System.Text.StringBuilder();

        foreach (Road road in roads)
        {
            foreach (Vector2 point in road.Points)
                text.Append(point.x).Append(',').Append(point.y).Append(';');

            text.Append('|');
        }

        return WorldGenBenchArgs.Hash(text.ToString());
    }
}
