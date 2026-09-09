using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsDec
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["dec.place"] = Place;
        registry["dec.filter"] = Filter;
        registry["dec.step"] = Step;
        registry["dec.plan"] = Plan;
        registry["dec.combine"] = Combine;
        registry["dec.instance"] = Instance;
        registry["dec.instantiate"] = Instantiate;
        registry["dec.cards"] = Cards;
        registry["dec.renderer"] = Renderer;
        registry["dec.reach"] = Reach;
        registry["dec.seams"] = Seams;
    }

    private static WorldGenBenchOp Place()
    {
        WorldGenBenchDecorRig rig = null;
        List<DecorInstance> output = new();
        DecorKind kind = DecorKind.Grass;
        Vector2 origin = Vector2.zero;
        float span = 0f;

        return new WorldGenBenchOp("dec.place", context =>
        {
            int cells = 0;
            int placed = 0;

            for (int layer = 0; layer < rig.Placer.Layers.Count; layer++)
            {
                if (rig.Placer.Layers[layer].Kind != kind)
                    continue;

                output.Clear();
                cells += rig.Placer.Place(layer, origin, span, output);
                placed += output.Count;
            }

            context.Count("testedCells", cells);
            context.Count("placed", placed);
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, context.Args.Float("grassDensity", 1f));
                kind = (DecorKind)Enum.Parse(typeof(DecorKind), context.Args.Text("kind", "Grass"), true);
                span = context.Args.Float("span", 128f);

                float centre = rig.Heights.WorldSize * 0.5f;
                origin = new Vector2(centre - span * 0.5f, centre - span * 0.5f);

                context.Count("layers", rig.Placer.Layers.Count);
                context.Count("span", span);
            },
            Verify = context =>
            {
                context.Count("rejected", rig.Placer.Rejected);
                context.Count("hectares", span * span / 10000f);
                context.Require(rig.Placer.Layers.Count > 0, "The placer must know at least one decor layer.");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp Filter()
    {
        WorldGenBenchDecorRig rig = null;
        Vector2[] points = null;

        return new WorldGenBenchOp("dec.filter", context =>
        {
            int clear = 0;
            int inBiome = 0;
            int inPatch = 0;

            foreach (Vector2 point in points)
            {
                if (rig.Filter.IsClear(point, 1f, 6f))
                    clear++;

                if (rig.Filter.InBiome(point, 0, 0.5f))
                    inBiome++;

                if (rig.Filter.InPatch(point, 38f, 0.4f))
                    inPatch++;
            }

            context.Count("clear", clear);
            context.Count("inBiome", inBiome);
            context.Count("inPatch", inPatch);
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, 1f);
                int count = context.Args.Int("points", 100000);
                points = new Vector2[count];

                var random = new Random(8484u);

                for (int index = 0; index < count; index++)
                {
                    points[index] = new Vector2(random.NextFloat(0f, rig.Heights.WorldSize),
                        random.NextFloat(0f, rig.Heights.WorldSize));
                }

                context.Count("points", count);
                context.Count("pads", rig.Placements?.Count ?? 0);
            },
            Verify = context =>
            {
                if (rig.Placements == null || rig.Placements.Count == 0)
                    return;

                int blocked = 0;

                foreach (PoiPlacement placement in rig.Placements)
                {
                    if (!rig.Filter.IsClear(placement.Ground, 0.5f, 0f))
                        blocked++;
                }

                context.Count("padCentresBlocked", blocked);
                context.Require(blocked == rig.Placements.Count, "Decor must never be allowed on a building pad.");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp Step()
    {
        WorldGenBenchDecorRig rig = null;
        VoxelDecorSpawner spawner = null;
        GameObject parent = null;
        int budget = 48;
        Vector2 origin = Vector2.zero;
        float span = 0f;

        return new WorldGenBenchOp("dec.step", context =>
        {
            var build = new VoxelDecorBuild(origin, span, parent.transform, 0, default, DecorScope.All);
            int steps = 0;
            double worst = 0d;

            while (!build.Done)
            {
                long start = WorldGenProbe.Now;
                spawner.Step(build, budget);
                worst = Math.Max(worst, WorldGenProbe.ToMs(WorldGenProbe.Now - start));
                steps++;
            }

            context.Count("steps", steps);
            context.Count("worstStepMs", worst);
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, context.Args.Float("grassDensity", 1f));
                budget = context.Args.Int("budget", 48);
                span = context.Args.Float("span", 32f);

                float centre = rig.Heights.WorldSize * 0.5f;
                origin = new Vector2(centre, centre);

                spawner = new VoxelDecorSpawner(rig.Placer, context.Profile.Voxels,
                    context.Profile.Settings.BatchVertexBudget, false);

                context.Count("budget", budget);
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Decor") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context =>
            {
                context.Count("placed", spawner.Placed);
                context.Count("combined", spawner.Combined);
                context.Count("instanced", spawner.Instanced);
                context.Count("instantiated", spawner.Instantiated);
                context.Count("batches", spawner.Batches);
            },
            Teardown = context =>
            {
                spawner?.Dispose();
                Destroy(parent);
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Plan()
    {
        var cases = new List<(string Name, PrefabProfile Profile, int Instances, bool Colliders, bool Culled)>();
        int budget = 48000;

        return new WorldGenBenchOp("dec.plan", context =>
        {
            var counts = new Dictionary<CombineVerdict, int>();

            foreach ((string name, PrefabProfile profile, int instances, bool colliders, bool culled) in cases)
            {
                CombineVerdict verdict = MeshCombinePlan.Decide(profile, instances, budget, colliders, culled, out _);
                counts.TryGetValue(verdict, out int seen);
                counts[verdict] = seen + 1;
            }

            foreach (KeyValuePair<CombineVerdict, int> pair in counts)
                context.Count(pair.Key.ToString(), pair.Value);
        })
        {
            Setup = context =>
            {
                budget = context.Args.Int("vertexBudget", 48000);

                PrefabProfile plain = new() { HasMesh = true, Readable = true, Instanceable = true, Vertices = 500 };
                PrefabProfile tiny = new() { HasMesh = true, Readable = true, Instanceable = true, Vertices = MeshCombinePlan.TINY_MESH };
                PrefabProfile big = new() { HasMesh = true, Readable = true, Instanceable = true, Vertices = MeshCombinePlan.TINY_MESH + 1 };
                PrefabProfile heavy = new() { HasMesh = true, Readable = true, Instanceable = true, Vertices = budget + 1 };
                PrefabProfile lods = new() { HasMesh = true, Readable = true, HasLodGroup = true, Vertices = 500 };
                PrefabProfile collider = new() { HasMesh = true, Readable = true, HasCollider = true, Vertices = 500 };
                PrefabProfile unreadable = new() { HasMesh = true, Readable = false, Instanceable = true, Vertices = 500 };
                PrefabProfile opaque = new() { HasMesh = true, Readable = false, Instanceable = false, Vertices = 500 };

                cases.Add(("threeCopies", plain, MeshCombinePlan.MIN_INSTANCES - 1, false, false));
                cases.Add(("fourCopies", plain, MeshCombinePlan.MIN_INSTANCES, false, false));
                cases.Add(("tinyMesh", tiny, 2400, false, true));
                cases.Add(("overTiny", big, 2400, false, true));
                cases.Add(("heavyMesh", heavy, 500, false, false));
                cases.Add(("lodGroup", lods, 500, false, false));
                cases.Add(("collider", collider, 500, true, false));
                cases.Add(("unreadable", unreadable, 500, false, false));
                cases.Add(("opaque", opaque, 500, false, false));
                cases.Add(("duplicateCeiling", plain, MeshCombinePlan.DUPLICATE_CEILING / 500 + 1, false, true));
                cases.Add(("noMesh", PrefabProfile.Empty, 500, false, false));

                context.Count("cases", cases.Count);
            },
            Verify = context =>
            {
                PrefabProfile plain = new() { HasMesh = true, Readable = true, Instanceable = true, Vertices = 500 };

                context.Require(MeshCombinePlan.Decide(plain, 3, budget, false, false, out _) == CombineVerdict.Instantiate,
                    "Three copies must stay separate objects.");
                context.Require(MeshCombinePlan.Decide(plain, 4, budget, false, false, out _) == CombineVerdict.Combine,
                    "Four copies must be combined.");
                context.Require(MeshCombinePlan.Decide(PrefabProfile.Empty, 500, budget, false, false, out _) == CombineVerdict.Skip,
                    "A prefab without a mesh must be skipped.");

                context.Count("batchSize", MeshCombinePlan.BatchSize(plain, budget));
                context.Count("batchCount", MeshCombinePlan.BatchCount(plain, 5000, budget));
            }
        };
    }

    private static WorldGenBenchOp Combine()
    {
        return Batching("dec.combine", false);
    }

    private static WorldGenBenchOp Instance()
    {
        return Batching("dec.instance", true);
    }

    private static WorldGenBenchOp Batching(string id, bool instancing)
    {
        WorldGenBenchDecorRig rig = null;
        MeshCombiner combiner = null;
        GrassCardFactory cards = null;
        List<DecorInstance> instances = new();
        GameObject parent = null;
        int budget = 48000;

        return new WorldGenBenchOp(id, context =>
        {
            int batches = instancing
                ? combiner.Instance(instances, parent.transform, "bench", UnityEngine.Rendering.ShadowCastingMode.Off, 96f)
                : combiner.Combine(instances, budget, parent.transform, "bench", UnityEngine.Rendering.ShadowCastingMode.Off);

            context.Count("batches", batches);
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, 1f);
                budget = context.Args.Int("vertexBudget", context.Profile.Settings.BatchVertexBudget);

                cards = new GrassCardFactory { Distance = context.Profile.Voxels.GrassDistance };
                combiner = new MeshCombiner(cards.Mesh, cards.Material(Texture2D.whiteTexture, Color.white));

                int count = context.Args.Int("instances", instancing ? 1023 : 400);
                var random = new Random(1717u);
                float centre = rig.Heights.WorldSize * 0.5f;

                for (int index = 0; index < count; index++)
                {
                    instances.Add(new DecorInstance
                    {
                        Position = new Vector3(centre + random.NextFloat(-32f, 32f), 0f, centre + random.NextFloat(-32f, 32f)),
                        Rotation = random.NextFloat(0f, 360f),
                        Scale = 1f,
                        Height = 1f,
                        Tint = Color.white
                    });
                }

                context.Count("instances", instances.Count);
                context.Count("vertexBudget", budget);
                context.Count("prefabVertices", combiner.Profile.Vertices);
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Batches") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context => context.Count("children", parent.transform.childCount),
            Teardown = context =>
            {
                Destroy(parent);
                cards?.Dispose();
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Instantiate()
    {
        WorldGenBenchDecorRig rig = null;
        GameObject prefab = null;
        List<DecorInstance> instances = new();
        GameObject parent = null;

        return new WorldGenBenchOp("dec.instantiate", context =>
        {
            foreach (DecorInstance instance in instances)
            {
                GameObject copy = Object.Instantiate(prefab, instance.Position,
                    Quaternion.Euler(0f, instance.Rotation, 0f), parent.transform);

                copy.transform.localScale = new Vector3(instance.Scale, instance.Height, instance.Scale);
            }
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, 1f);
                prefab = FindPrefab(context.Profile.Biomes);

                if (prefab == null)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "No scatter prefab is assigned in the biomes.");

                int count = context.Args.Int("instances", 200);
                var random = new Random(2020u);
                float centre = rig.Heights.WorldSize * 0.5f;

                for (int index = 0; index < count; index++)
                {
                    instances.Add(new DecorInstance
                    {
                        Position = new Vector3(centre + random.NextFloat(-64f, 64f), 0f, centre + random.NextFloat(-64f, 64f)),
                        Rotation = random.NextFloat(0f, 360f),
                        Scale = 1f,
                        Height = 1f,
                        Tint = Color.white
                    });
                }

                context.Count("instances", instances.Count);
                context.Count("hasLodGroup", prefab.GetComponentInChildren<LODGroup>() == null ? 0 : 1);
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Instantiate") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context =>
            {
                context.Count("created", parent.transform.childCount);
                context.Require(parent.transform.childCount == instances.Count, "Every instance must be created.");
            },
            Teardown = context =>
            {
                Destroy(parent);
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Cards()
    {
        GrassCardFactory cards = null;
        Texture2D[] textures = null;

        return new WorldGenBenchOp("dec.cards", context =>
        {
            Mesh mesh = cards.Mesh;
            context.Count("vertices", mesh.vertexCount);

            foreach (Texture2D texture in textures)
            {
                cards.Material(texture, Color.white);
                cards.Material(texture, Color.green);
            }
        })
        {
            Setup = context =>
            {
                textures = new[] { Texture2D.whiteTexture, Texture2D.blackTexture, Texture2D.grayTexture };
                context.Count("textures", textures.Length);
            },
            Prepare = context =>
            {
                cards?.Dispose();
                cards = new GrassCardFactory { Distance = context.Profile.Voxels.GrassDistance };
            },
            Verify = context =>
            {
                Material first = cards.Material(textures[0], Color.white);
                Material again = cards.Material(textures[0], Color.white);
                context.Require(ReferenceEquals(first, again), "One texture and tint must share one material.");
                context.Count("shaderSupported", first.shader != null && first.shader.isSupported ? 1 : 0);
                context.Note("shader: " + (first.shader == null ? "none" : first.shader.name));
            },
            Teardown = context => cards?.Dispose()
        };
    }

    private static WorldGenBenchOp Renderer()
    {
        GameObject host = null;
        VoxelDecorRenderer renderer = null;
        GrassCardFactory cards = null;
        List<DecorInstance> instances = new();

        return new WorldGenBenchOp("dec.renderer", context =>
        {
            renderer.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        })
        {
            Setup = context =>
            {
                cards = new GrassCardFactory { Distance = context.Profile.Voxels.GrassDistance };
                var combiner = new MeshCombiner(cards.Mesh, cards.Material(Texture2D.whiteTexture, Color.white));

                int count = context.Args.Int("instances", 4096);
                var random = new Random(9090u);

                for (int index = 0; index < count; index++)
                {
                    instances.Add(new DecorInstance
                    {
                        Position = new Vector3(random.NextFloat(-96f, 96f), 0f, random.NextFloat(-96f, 96f)),
                        Rotation = random.NextFloat(0f, 360f),
                        Scale = 1f,
                        Height = 1f,
                        Tint = Color.white
                    });
                }

                host = new GameObject("WorldGenBench Renderer") { hideFlags = HideFlags.HideAndDontSave };
                int batches = combiner.Instance(instances, host.transform, "bench",
                    UnityEngine.Rendering.ShadowCastingMode.Off, context.Profile.Voxels.GrassDistance);

                context.Count("instances", instances.Count);
                context.Count("batches", batches);

                renderer = host.GetComponentInChildren<VoxelDecorRenderer>();

                if (renderer == null)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "Instancing did not create a decor renderer.");
            },
            Teardown = context =>
            {
                Destroy(host);
                cards?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Reach()
    {
        WorldGenBenchDecorRig rig = null;
        VoxelStreamPlan plan = null;

        return new WorldGenBenchOp("dec.reach", context =>
        {
            VoxelConfig voxels = context.Profile.Voxels;
            float patch = voxels.ChunkMetres;
            var output = new List<DecorInstance>();

            foreach (DecorKind kind in new[] { DecorKind.Grass, DecorKind.Rock, DecorKind.Tree })
            {
                float reach = kind == DecorKind.Grass ? voxels.GrassDistance : plan.Distance(voxels.MaxLod(kind));
                int patches = Mathf.Max(1, Mathf.CeilToInt(reach * 2f / patch));
                patches *= patches;

                int placed = 0;
                float centre = rig.Heights.WorldSize * 0.5f;

                for (int layer = 0; layer < rig.Placer.Layers.Count; layer++)
                {
                    if (rig.Placer.Layers[layer].Kind != kind)
                        continue;

                    output.Clear();
                    rig.Placer.Place(layer, new Vector2(centre, centre), patch, output);
                    placed += output.Count;
                }

                context.Count(kind + "Reach", reach);
                context.Count(kind + "Patches", patches);
                context.Count(kind + "PerPatch", placed);
                context.Count(kind + "Estimate", (double)placed * patches);
            }
        })
        {
            Setup = context =>
            {
                VoxelConfig voxels = context.Profile.Voxels;

                if (context.Args.Has("grassDistance"))
                    WorldGenBenchProfile.Assign(voxels, "GrassDistance", context.Args.Float("grassDistance", 96f));

                if (context.Args.Has("rockMaxLod"))
                    WorldGenBenchProfile.Assign(voxels, "RockMaxLod", context.Args.Int("rockMaxLod", 1));

                if (context.Args.Has("treeMaxLod"))
                    WorldGenBenchProfile.Assign(voxels, "TreeMaxLod", context.Args.Int("treeMaxLod", 2));

                rig = new WorldGenBenchDecorRig(context, context.Args.Float("grassDensity", 1f));
                plan = new VoxelStreamPlan(voxels, context.Profile.Settings.LodCount,
                    context.Profile.Settings.NearDistance, context.Profile.Config.WorldSize);
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp Seams()
    {
        WorldGenBenchDecorRig rig = null;
        VoxelStreamPlan plan = null;

        return new WorldGenBenchOp("dec.seams", context =>
        {
            VoxelConfig voxels = context.Profile.Voxels;
            var output = new List<DecorInstance>();
            double worst = 0d;
            int checkedPoints = 0;

            for (int lod = 0; lod <= 2; lod++)
            {
                float voxelSize = plan.VoxelSize(lod);
                float size = plan.ChunkMetres(lod);
                float centre = rig.Heights.WorldSize * 0.5f;
                var origin = new Vector2(Mathf.Floor(centre / size) * size, Mathf.Floor(centre / size) * size);

                var frame = new DecorSurface(origin,
                    VoxelDensitySampler.MorphSpan(voxels.ChunkSize, voxelSize), voxelSize, VoxelColumnKey.MORPH_MAX_X);

                for (int layer = 0; layer < rig.Placer.Layers.Count; layer++)
                {
                    output.Clear();
                    rig.Placer.Place(layer, origin, size, output, frame);

                    foreach (DecorInstance instance in output)
                    {
                        float surface = rig.Field.Surface(instance.Position.x, instance.Position.z, frame);
                        worst = Math.Max(worst, Math.Abs(surface - instance.Position.y));
                        checkedPoints++;
                    }
                }
            }

            context.Count("worstSurfaceError", worst);
            context.Count("checkedPoints", checkedPoints);
        })
        {
            Setup = context =>
            {
                rig = new WorldGenBenchDecorRig(context, context.Args.Float("grassDensity", 0.25f));
                plan = new VoxelStreamPlan(context.Profile.Voxels, context.Profile.Settings.LodCount,
                    context.Profile.Settings.NearDistance, context.Profile.Config.WorldSize);
            },
            Verify = context =>
            {
                context.Require(context.Work["checkedPoints"] == 0d || context.Work["worstSurfaceError"] < 0.05,
                    "Decor must sit on the surface the column actually draws.");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static GameObject FindPrefab(BiomeDatabase biomes)
    {
        foreach (BiomeDefinition definition in biomes.Biomes)
        {
            if (definition == null)
                continue;

            foreach (ScatterLayer layer in definition.Trees)
            {
                if (layer != null && layer.Prefab != null)
                    return layer.Prefab;
            }

            foreach (ScatterLayer layer in definition.Rocks)
            {
                if (layer != null && layer.Prefab != null)
                    return layer.Prefab;
            }
        }

        return null;
    }

    private static void Destroy(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(target);
        else
            Object.DestroyImmediate(target);
    }
}
