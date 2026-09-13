using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsPoi
{
    public delegate bool LotStep(Lot lot, ref Random random);

    public delegate void RoadStep(RoadNetwork network, ref Random random);

    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["poi.place"] = Place;
        registry["poi.lot"] = OnLot;
        registry["poi.rural"] = Rural;
        registry["poi.pads"] = Pads;
        registry["poi.padIndex"] = PadIndex;
        registry["poi.instances"] = Instances;
        registry["poi.spawner"] = Spawner;
    }

    private static WorldGenBenchOp Place()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap heights = null;
        RoadProximity proximity = null;

        return new WorldGenBenchOp("poi.place", context =>
        {
            var placer = new PoiPlacer(context.Profile.Config, context.Profile.Pois, heights, proximity);
            List<PoiPlacement> placements = placer.Place(frozen.Settlements, frozen.Roads);
            context.Put("placements", placements);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                context.Count("settlements", frozen.Settlements.Count);

                int lots = 0;
                int houses = 0;

                foreach (SettlementLayout settlement in frozen.Settlements)
                {
                    lots += settlement.Lots.Count;
                    houses += settlement.Hub.Houses;
                }

                context.Count("lots", lots);
                context.Count("housesDrawn", houses);
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                heights = frozen.CopyCarved();
                proximity = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
                proximity.AddRange(frozen.Roads.Streets);
            },
            Verify = context =>
            {
                var placements = context.Get<List<PoiPlacement>>("placements");
                context.Count("placements", placements.Count);

                var districts = new Dictionary<DistrictType, int>();

                foreach (PoiPlacement placement in placements)
                {
                    districts.TryGetValue(placement.District, out int count);
                    districts[placement.District] = count + 1;

                    context.Require(placement.Position.x >= 0f && placement.Position.x <= heights.WorldSize
                        && placement.Position.z >= 0f && placement.Position.z <= heights.WorldSize,
                        "A placement must stand inside the world.");
                }

                foreach (KeyValuePair<DistrictType, int> pair in districts)
                    context.Count("district" + pair.Key, pair.Value);

                context.Print("placements", Fingerprint(placements));
            }
        };
    }

    private static WorldGenBenchOp OnLot()
    {
        WorldGenBenchFrozen frozen = null;
        PoiPlacer placer = null;
        LotStep place = null;
        List<Lot> lots = null;

        return new WorldGenBenchOp("poi.lot", context =>
        {
            var random = new Random(191u);

            foreach (Lot lot in lots)
                place(lot, ref random);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                context.Require(frozen.Settlements.Count > 0, "Lot placement needs a settlement.");

                lots = new List<Lot>();

                foreach (SettlementLayout settlement in frozen.Settlements)
                {
                    foreach (Lot lot in settlement.Lots)
                        lots.Add(lot);

                    if (lots.Count >= context.Args.Int("lots", 400))
                        break;
                }

                context.Count("lots", lots.Count);
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                var proximity = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
                proximity.AddRange(frozen.Roads.Streets);
                placer = new PoiPlacer(config, context.Profile.Pois, frozen.CarvedHeights, proximity);
                place = WorldGenBenchReflect.Bind<LotStep>(placer, "PlaceOnLot");
            },
            Verify = context =>
            {
                var placements = (List<PoiPlacement>)WorldGenBenchReflect.Field(placer, "_placements");
                context.Count("accepted", placements.Count);
                context.Count("rejected", lots.Count - placements.Count);
            }
        };
    }

    private static WorldGenBenchOp Rural()
    {
        WorldGenBenchFrozen frozen = null;
        PoiPlacer placer = null;
        RoadStep along = null;

        return new WorldGenBenchOp("poi.rural", context =>
        {
            var random = new Random(808u);
            along(frozen.Roads, ref random);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "cities");
                WorldGenBenchProfile.Assign(context.Profile.Config, "RuralChance",
                    context.Args.Float("ruralChance", context.Profile.Config.RuralChance));

                if (context.Args.Has("ruralSpacing"))
                    WorldGenBenchProfile.Assign(context.Profile.Config, "RuralSpacing", context.Args.Float("ruralSpacing", 180f));

                context.Count("ruralChance", context.Profile.Config.RuralChance);
                context.Count("roads", frozen.Roads.Roads.Count);
            },
            Prepare = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                var proximity = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
                proximity.AddRange(frozen.Roads.Streets);
                placer = new PoiPlacer(config, context.Profile.Pois, frozen.CarvedHeights, proximity);
                along = WorldGenBenchReflect.Bind<RoadStep>(placer, "PlaceAlongRoads");
            },
            Verify = context =>
            {
                var placements = (List<PoiPlacement>)WorldGenBenchReflect.Field(placer, "_placements");
                context.Count("rural", placements.Count);
            }
        };
    }

    private static WorldGenBenchOp Pads()
    {
        WorldGenBenchFrozen frozen = null;
        HeightMap heights = null;

        return new WorldGenBenchOp("poi.pads", context =>
        {
            var carver = new TerrainCarver(context.Profile.Config);
            HeightMap carved = carver.CarvePads(heights, frozen.Placements);
            context.Put("carved", carved);

            WorldGenerationConfig config = context.Profile.Config;
            var proximity = new RoadProximity(frozen.Roads.Roads, config.WorldSize, config.RoadCellSize);
            var placer = new PoiPlacer(config, context.Profile.Pois, carved, proximity);
            placer.ApplyHeights(carved);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements");
                context.Require(frozen.Placements != null, "Pad carving needs placements.");
                context.Count("placements", frozen.Placements.Count);
            },
            Prepare = context => heights = frozen.CopyCarved(),
            Verify = context =>
            {
                var carved = context.Get<HeightMap>("carved");
                WorldGenBenchOpsHgt.Inspect(context, carved, "pads");

                double worst = 0d;

                foreach (PoiPlacement placement in frozen.Placements)
                {
                    Vector2 forward = placement.Forward;
                    var right = new Vector2(forward.y, -forward.x);

                    for (int cornerX = -1; cornerX <= 1; cornerX += 2)
                    {
                        for (int cornerZ = -1; cornerZ <= 1; cornerZ += 2)
                        {
                            Vector2 corner = placement.Ground
                                + right * (cornerX * placement.Footprint.x * 0.5f)
                                + forward * (cornerZ * placement.Footprint.y * 0.5f);

                            float ground = carved.SampleWorldSmooth(corner.x, corner.y) * carved.MaxHeight;
                            worst = Math.Max(worst, Math.Abs(ground - placement.Position.y));
                        }
                    }
                }

                context.Count("worstCornerMetres", worst);
            }
        };
    }

    private static WorldGenBenchOp PadIndex()
    {
        WorldGenBenchFrozen frozen = null;
        Vector2[] queries = null;
        PoiPadIndex index = null;

        return new WorldGenBenchOp("poi.padIndex", context =>
        {
            WorldGenerationConfig config = context.Profile.Config;
            index = new PoiPadIndex(frozen.Placements, config.WorldSize, 64f, config.PoiPadMargin, 8f);

            int hits = 0;

            foreach (Vector2 point in queries)
            {
                if (index.Covers(point, 1f))
                    hits++;
            }

            context.Count("covered", hits);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements");
                context.Require(frozen.Placements != null, "The pad index needs placements.");

                int count = context.Args.Int("queries", 200000);
                queries = new Vector2[count];

                var random = new Random(2626u);

                for (int index2 = 0; index2 < count; index2++)
                {
                    queries[index2] = new Vector2(random.NextFloat(0f, context.Profile.Config.WorldSize),
                        random.NextFloat(0f, context.Profile.Config.WorldSize));
                }

                context.Count("pads", frozen.Placements.Count);
                context.Count("queries", count);
            },
            Verify = context =>
            {
                int inside = 0;

                foreach (PoiPlacement placement in frozen.Placements)
                {
                    if (index.Covers(placement.Ground, 0f))
                        inside++;
                }

                context.Count("padCentresCovered", inside);
                context.Require(frozen.Placements.Count == 0 || inside == frozen.Placements.Count,
                    "Every pad centre must be reported as covered.");
            }
        };
    }

    private static WorldGenBenchOp Instances()
    {
        WorldGenBenchFrozen frozen = null;
        GameObject parent = null;
        PoiInstanceBuilder builder = null;
        int batch = 16;

        return new WorldGenBenchOp("poi.instances", context =>
        {
            while (!builder.Ready)
                builder.BuildNext(batch);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements");
                batch = context.Args.Int("batch", 16);
                context.Require(frozen.Placements != null, "POI instancing needs placements.");

                int limit = context.Args.Int("count", Math.Min(64, frozen.Placements.Count));
                var subset = new List<PoiPlacement>();

                for (int index = 0; index < limit && index < frozen.Placements.Count; index++)
                {
                    if (frozen.Placements[index].Prefab != null)
                        subset.Add(frozen.Placements[index]);
                }

                context.Put("subset", subset);
                context.Count("requested", subset.Count);
                context.Count("batch", batch);
            },
            Prepare = context =>
            {
                Cleanup(parent);
                parent = new GameObject("WorldGenBench POI") { hideFlags = HideFlags.HideAndDontSave };
                builder = new PoiInstanceBuilder(context.Get<List<PoiPlacement>>("subset"), parent.transform);
            },
            Verify = context =>
            {
                var subset = context.Get<List<PoiPlacement>>("subset");
                context.Count("created", parent.transform.childCount);
                context.Require(parent.transform.childCount == subset.Count,
                    "Every requested POI must be created.");
            },
            Teardown = context => Cleanup(parent)
        };
    }

    private static WorldGenBenchOp Spawner()
    {
        GameObject host = null;
        PoiSpawner spawner = null;

        return new WorldGenBenchOp("poi.spawner", context =>
        {
            spawner.Spawn();
        })
        {
            Setup = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

                if (fixture == null || fixture.World == null || fixture.World.Placement == null)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "The legacy spawner needs a baked PoiPlacement asset.");

                host = new GameObject("WorldGenBench Legacy POI") { hideFlags = HideFlags.HideAndDontSave };
                spawner = host.AddComponent<PoiSpawner>();
                WorldGenBenchProfile.Assign(spawner, "Placement", fixture.World.Placement);
                context.Count("placements", fixture.World.Placement.Placements.Count);
            },
            Prepare = context => spawner.Clear(),
            Verify = context =>
            {
                context.Count("afterSpawn", host.transform.childCount);
                spawner.Clear();
                context.Count("afterClear", host.transform.childCount);
            },
            Teardown = context => Cleanup(host)
        };
    }

    private static void Cleanup(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(target);
        else
            UnityEngine.Object.DestroyImmediate(target);
    }

    public static string Fingerprint(List<PoiPlacement> placements)
    {
        var text = new System.Text.StringBuilder();

        foreach (PoiPlacement placement in placements)
        {
            text.Append(placement.Prefab == null ? "none" : placement.Prefab.name).Append(',')
                .Append(placement.Position.x).Append(',').Append(placement.Position.y).Append(',')
                .Append(placement.Position.z).Append(',').Append(placement.Rotation).Append(';');
        }

        return WorldGenBenchArgs.Hash(text.ToString());
    }
}
