using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

public static class WorldGenBenchOpsVox
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["vox.field"] = Field;
        registry["vox.sampler"] = Sampler;
        registry["vox.plan"] = Plan;
        registry["vox.range"] = Range;
        registry["vox.mesh"] = Mesh;
        registry["vox.meshKinds"] = MeshKinds;
        registry["vox.seams"] = Seams;
        registry["vox.grid"] = Grid;
        registry["vox.spawn"] = Spawn;
        registry["vox.builder"] = Builder;
    }

    public static HeightMap Heights(WorldGenBenchContext context)
    {
        WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
        return frozen.CarvedHeights ?? frozen.RawHeights;
    }

    public static float Lowest(HeightMap map)
    {
        float lowest = float.MaxValue;

        foreach (float value in map.Heights)
            lowest = Mathf.Min(lowest, value);

        return lowest * map.MaxHeight;
    }

    private static WorldGenBenchOp Field()
    {
        HeightMap map = null;
        VoxelDensityField field = null;

        return new WorldGenBenchOp("vox.field", context =>
        {
            field?.Dispose();
            field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                context.Count("resolution", map.Resolution);
                context.Count("heightBytes", (long)map.Heights.Length * sizeof(float));
            },
            Verify = context =>
            {
                float surface = field.Surface(map.WorldSize * 0.5f, map.WorldSize * 0.5f);
                context.Require(!float.IsNaN(surface), "The density field must sample a finite surface.");
                context.Count("centreSurface", surface);
                context.Count("floor", field.Floor);
            },
            Teardown = context => field?.Dispose()
        };
    }

    private static WorldGenBenchOp Sampler()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        Vector3[] points = null;
        int morph = 0;

        return new WorldGenBenchOp("vox.sampler", context =>
        {
            double total = 0d;
            VoxelDensitySampler sampler = field.Sampler;

            foreach (Vector3 point in points)
            {
                total += sampler.Surface(point.x, point.z);
                total += sampler.Sample(point.x, point.y, point.z);
                total += sampler.Normal(point.x, point.y, point.z).y;
            }

            context.Count("checksum", total);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
                morph = context.Args.Int("morph", 0);

                int count = context.Args.Int("points", 100000);
                points = new Vector3[count];

                var random = new Random(3141u);

                for (int index = 0; index < count; index++)
                {
                    points[index] = new Vector3(random.NextFloat(0f, map.WorldSize),
                        random.NextFloat(0f, map.MaxHeight), random.NextFloat(0f, map.WorldSize));
                }

                context.Count("points", count);
                context.Count("morph", morph);
            },
            Verify = context =>
            {
                VoxelDensitySampler sampler = field.Sampler;
                float centre = map.WorldSize * 0.5f;
                float surface = sampler.Surface(centre, centre);

                context.Require(Mathf.Abs(sampler.Sample(centre, surface, centre)) < 0.01f,
                    "Density must vanish on the surface.");

                float coarse = sampler.Coarse(centre, centre, context.Profile.Voxels.VoxelSize * 4f);
                context.Count("coarseDelta", Mathf.Abs(coarse - surface));
            },
            Teardown = context => field?.Dispose()
        };
    }

    private static WorldGenBenchOp Plan()
    {
        VoxelStreamPlan plan = null;
        List<VoxelColumnKey> columns = new();
        Vector2 viewer = Vector2.zero;

        return new WorldGenBenchOp("vox.plan", context => plan.Around(viewer, columns))
        {
            Setup = context =>
            {
                WorldGenerationConfig config = context.Profile.Config;
                int lods = context.Args.Int("lods", context.Profile.Settings.LodCount);
                float near = context.Args.Float("near", context.Profile.Settings.NearDistance);

                plan = new VoxelStreamPlan(context.Profile.Voxels, lods, near, config.WorldSize);

                string where = context.Args.Text("where", "centre");

                viewer = where switch
                {
                    "corner" => new Vector2(4f, 4f),
                    "edge" => new Vector2(config.WorldSize - 4f, config.WorldSize * 0.5f),
                    _ => new Vector2(config.WorldSize * 0.5f, config.WorldSize * 0.5f)
                };

                context.Count("lods", lods);
                context.Count("nearDistance", near);
            },
            Verify = context =>
            {
                context.Count("columns", columns.Count);
                context.Require(columns.Count > 0, "The plan must cover the world.");

                var perLod = new int[16];
                double area = 0d;

                foreach (VoxelColumnKey key in columns)
                {
                    perLod[key.Lod]++;
                    float metres = plan.ChunkMetres(key.Lod);
                    area += metres * metres;
                }

                for (int lod = 0; lod < perLod.Length; lod++)
                {
                    if (perLod[lod] > 0)
                        context.Count("lod" + lod, perLod[lod]);
                }

                context.Count("coveredSquareMetres", area);
                context.Count("worldSquareMetres", (double)context.Profile.Config.WorldSize * context.Profile.Config.WorldSize);

                var seen = new HashSet<VoxelColumnKey>();

                foreach (VoxelColumnKey key in columns)
                    context.Require(seen.Add(key), "The plan must not repeat a column.");
            }
        };
    }

    private static WorldGenBenchOp Range()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelStreamPlan plan = null;
        List<VoxelColumnKey> columns = new();

        return new WorldGenBenchOp("vox.range", context =>
        {
            int chunks = 0;
            int samples = 0;

            foreach (VoxelColumnKey key in columns)
            {
                float size = plan.ChunkMetres(key.Lod);
                float step = plan.VoxelSize(key.Lod);

                float lowest = float.MaxValue;
                float highest = float.MinValue;

                for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
                {
                    for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
                    {
                        float surface = field.Surface(x, z);
                        lowest = Mathf.Min(lowest, surface);
                        highest = Mathf.Max(highest, surface);
                        samples++;
                    }
                }

                int low = Mathf.FloorToInt(lowest / size) - 1;
                int high = Mathf.FloorToInt(highest / size) + 1;
                chunks += high - low + 1;
            }

            context.Count("chunksRequested", chunks);
            context.Count("surfaceSamples", samples);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
                plan = new VoxelStreamPlan(context.Profile.Voxels, context.Profile.Settings.LodCount,
                    context.Profile.Settings.NearDistance, map.WorldSize);

                var all = new List<VoxelColumnKey>();
                plan.Around(new Vector2(map.WorldSize * 0.5f, map.WorldSize * 0.5f), all);

                int limit = context.Args.Int("columns", 32);

                for (int index = 0; index < all.Count && columns.Count < limit; index++)
                    columns.Add(all[index]);

                context.Count("columns", columns.Count);
            },
            Teardown = context => field?.Dispose()
        };
    }

    private static WorldGenBenchOp Mesh()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelChunkMesher mesher = null;
        VoxelMesh mesh = null;
        int lod = 0;
        int chunkX = 0;
        int chunkY = 0;
        int chunkZ = 0;
        int seams = 0;
        int morph = 0;

        return new WorldGenBenchOp("vox.mesh", context =>
        {
            mesher.Mesh(lod, chunkX, chunkY, chunkZ, mesh, seams, morph);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
                mesher = new VoxelChunkMesher(context.Profile.Voxels, field);
                mesh = new VoxelMesh();

                lod = context.Args.Int("lod", 0);
                seams = context.Args.Int("seams", 0);
                morph = context.Args.Int("morph", 0);

                float metres = context.Profile.Voxels.ChunkMetres * (1 << lod);
                float centre = map.WorldSize * 0.5f;

                chunkX = Mathf.FloorToInt(centre / metres);
                chunkZ = Mathf.FloorToInt(centre / metres);
                chunkY = Mathf.FloorToInt(field.Surface(centre, centre) / metres);

                context.Count("lod", lod);
                context.Count("chunkMetres", metres);
            },
            Verify = context =>
            {
                context.Count("vertices", mesh.VertexCount);
                context.Count("triangles", mesh.TriangleCount);

                float metres = context.Profile.Voxels.ChunkMetres * (1 << lod);
                context.Count("trianglesPerSquareMetre", mesh.TriangleCount / (metres * metres));
            },
            Teardown = context =>
            {
                mesher?.Dispose();
                mesh?.Dispose();
                field?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp MeshKinds()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelChunkMesher mesher = null;
        VoxelMesh mesh = null;
        int chunkX = 0;
        int chunkZ = 0;
        int air = 0;
        int solid = 0;
        int surface = 0;

        return new WorldGenBenchOp("vox.meshKinds", context =>
        {
            mesher.Mesh(0, chunkX, air, chunkZ, mesh, 0, 0);
            context.Count("airTriangles", mesh.TriangleCount);

            mesher.Mesh(0, chunkX, solid, chunkZ, mesh, 0, 0);
            context.Count("solidTriangles", mesh.TriangleCount);

            mesher.Mesh(0, chunkX, surface, chunkZ, mesh, 0, 0);
            context.Count("surfaceTriangles", mesh.TriangleCount);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
                mesher = new VoxelChunkMesher(context.Profile.Voxels, field);
                mesh = new VoxelMesh();

                float metres = context.Profile.Voxels.ChunkMetres;
                float centre = map.WorldSize * 0.5f;

                chunkX = Mathf.FloorToInt(centre / metres);
                chunkZ = Mathf.FloorToInt(centre / metres);
                surface = Mathf.FloorToInt(field.Surface(centre, centre) / metres);
                air = surface + 8;
                solid = surface - 8;
            },
            Verify = context =>
            {
                context.Require(context.Work["airTriangles"] == 0d, "A chunk entirely in air must be empty.");
                context.Require(context.Work["solidTriangles"] == 0d, "A chunk entirely in rock must be empty.");
                context.Require(context.Work["surfaceTriangles"] > 0d, "A chunk on the surface must carry geometry.");
            },
            Teardown = context =>
            {
                mesher?.Dispose();
                mesh?.Dispose();
                field?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Seams()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelChunkMesher mesher = null;
        VoxelMesh mesh = null;
        int chunkX = 0;
        int chunkY = 0;
        int chunkZ = 0;
        int seams = 0;
        int morph = 0;

        return new WorldGenBenchOp("vox.seams", context =>
        {
            mesher.Mesh(1, chunkX, chunkY, chunkZ, mesh, seams, morph);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                seams = context.Args.Int("seams", VoxelColumnKey.FACE_MIN_X);
                morph = context.Args.Int("morph", VoxelColumnKey.MORPH_MIN_X);

                VoxelConfig voxels = context.Profile.Voxels;

                if (context.Args.Has("skirt"))
                    WorldGenBenchProfile.Assign(voxels, "SkirtDepth", context.Args.Float("skirt", 2f));

                field = new VoxelDensityField(map, voxels, Lowest(map));
                mesher = new VoxelChunkMesher(voxels, field);
                mesh = new VoxelMesh();

                float metres = voxels.ChunkMetres * 2f;
                float centre = map.WorldSize * 0.5f;

                chunkX = Mathf.FloorToInt(centre / metres);
                chunkZ = Mathf.FloorToInt(centre / metres);
                chunkY = Mathf.FloorToInt(field.Surface(centre, centre) / metres);

                context.Count("seams", seams);
                context.Count("morph", morph);
                context.Count("skirtDepth", voxels.SkirtDepth);
            },
            Verify = context =>
            {
                context.Count("vertices", mesh.VertexCount);
                context.Count("triangles", mesh.TriangleCount);

                var bare = new VoxelMesh();
                mesher.Mesh(1, chunkX, chunkY, chunkZ, bare, 0, morph);
                context.Count("trianglesWithoutSeam", bare.TriangleCount);
                bare.Dispose();
            },
            Teardown = context =>
            {
                mesher?.Dispose();
                mesh?.Dispose();
                field?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Grid()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelChunkMesher mesher = null;
        VoxelMesh mesh = null;
        int chunkX = 0;
        int chunkY = 0;
        int chunkZ = 0;

        return new WorldGenBenchOp("vox.grid", context => mesher.Mesh(0, chunkX, chunkY, chunkZ, mesh, 0, 0))
        {
            Setup = context =>
            {
                VoxelConfig voxels = context.Profile.Voxels;
                WorldGenBenchProfile.Assign(voxels, "ChunkSize", context.Args.Int("chunkSize", voxels.ChunkSize));
                WorldGenBenchProfile.Assign(voxels, "VoxelSize", context.Args.Float("voxelSize", voxels.VoxelSize));

                map = Heights(context);
                field = new VoxelDensityField(map, voxels, Lowest(map));
                mesher = new VoxelChunkMesher(voxels, field);
                mesh = new VoxelMesh();

                float metres = voxels.ChunkMetres;
                float centre = map.WorldSize * 0.5f;

                chunkX = Mathf.FloorToInt(centre / metres);
                chunkZ = Mathf.FloorToInt(centre / metres);
                chunkY = Mathf.FloorToInt(field.Surface(centre, centre) / metres);

                int samples = voxels.ChunkSize + 2;

                context.Count("chunkSize", voxels.ChunkSize);
                context.Count("voxelSize", voxels.VoxelSize);
                context.Count("chunkMetres", metres);
                context.Count("scratchBytes", (long)samples * samples * samples * sizeof(float));
                context.Count("totalChunks", (long)voxels.ChunksPerSide(map.WorldSize) * voxels.ChunksPerSide(map.WorldSize));
            },
            Verify = context =>
            {
                context.Count("vertices", mesh.VertexCount);
                context.Count("triangles", mesh.TriangleCount);
            },
            Teardown = context =>
            {
                mesher?.Dispose();
                mesh?.Dispose();
                field?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Spawn()
    {
        HeightMap map = null;
        VoxelDensityField field = null;
        VoxelChunkMesher mesher = null;
        VoxelMesh source = null;
        GameObject parent = null;

        return new WorldGenBenchOp("vox.spawn", context =>
        {
            var holder = new GameObject("Chunk");
            holder.transform.SetParent(parent.transform, false);

            var mesh = new UnityEngine.Mesh { name = "Chunk" };

            if (source.VertexCount > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(source.Vertices.AsArray());
            mesh.SetNormals(source.Normals.AsArray());
            mesh.SetUVs(0, source.Uv.AsArray());
            mesh.SetIndices(source.Triangles.AsArray(), MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();

            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>();
            GeneratedMesh.Own(holder);
            holder.SetActive(true);
        })
        {
            Setup = context =>
            {
                map = Heights(context);
                field = new VoxelDensityField(map, context.Profile.Voxels, Lowest(map));
                mesher = new VoxelChunkMesher(context.Profile.Voxels, field);
                source = new VoxelMesh();

                float metres = context.Profile.Voxels.ChunkMetres;
                float centre = map.WorldSize * 0.5f;

                mesher.Mesh(0, Mathf.FloorToInt(centre / metres), Mathf.FloorToInt(field.Surface(centre, centre) / metres),
                    Mathf.FloorToInt(centre / metres), source, 0, 0);

                context.Count("vertices", source.VertexCount);
                context.Count("triangles", source.TriangleCount);
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Chunks") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context => context.Count("spawned", parent.transform.childCount),
            Teardown = context =>
            {
                Destroy(parent);
                mesher?.Dispose();
                source?.Dispose();
                field?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Builder()
    {
        GameObject host = null;
        VoxelTerrainBuilder builder = null;

        return new WorldGenBenchOp("vox.builder", context =>
        {
            bool built = builder.Build(null);
            context.Count("built", built ? 1 : 0);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

                if (fixture == null || !fixture.HasBake)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "The preview builder needs a valid bake.");

                WorldGenBenchBudget.RequireFullPreview(context, fixture);

                host = new GameObject("WorldGenBench Preview") { hideFlags = HideFlags.HideAndDontSave };
                builder = host.AddComponent<VoxelTerrainBuilder>();
                builder.Configure(fixture.Settings, fixture.World);
            },
            Verify = context =>
            {
                context.Count("columns", host.transform.childCount);
                builder.Clear();
                context.Count("afterClear", host.transform.childCount);
            },
            Teardown = context =>
            {
                builder?.Clear();
                Destroy(host);
            }
        };
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
