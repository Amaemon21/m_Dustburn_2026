using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldGenBenchOpsQue
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["que.workers"] = Workers;
        registry["que.drain"] = Drain;
        registry["que.batch"] = Batch;
        registry["que.colliderDispatch"] = ColliderDispatch;
        registry["que.colliderAttach"] = ColliderAttach;
        registry["que.colliderLag"] = ColliderLag;
        registry["que.dispose"] = Dispose;
        registry["que.contention"] = Contention;
    }

    private sealed class Rig : IDisposable
    {
        public HeightMap Map;
        public VoxelDensityField Field;
        public VoxelMeshQueue Queue;
        public VoxelStreamPlan Plan;
        public List<VoxelColumnKey> Columns = new();

        public void Dispose()
        {
            Queue?.Dispose();
            Field?.Dispose();
        }
    }

    private static Rig Build(WorldGenBenchContext context, int workers)
    {
        var rig = new Rig { Map = WorldGenBenchOpsVox.Heights(context) };

        rig.Field = new VoxelDensityField(rig.Map, context.Profile.Voxels, WorldGenBenchOpsVox.Lowest(rig.Map));
        rig.Queue = new VoxelMeshQueue(context.Profile.Voxels, rig.Field, workers);
        rig.Plan = new VoxelStreamPlan(context.Profile.Voxels, context.Profile.Settings.LodCount,
            context.Profile.Settings.NearDistance, rig.Map.WorldSize);

        var all = new List<VoxelColumnKey>();
        rig.Plan.Around(new Vector2(rig.Map.WorldSize * 0.5f, rig.Map.WorldSize * 0.5f), all);

        int limit = context.Args.Int("columns", 24);

        for (int index = 0; index < all.Count && rig.Columns.Count < limit; index++)
            rig.Columns.Add(all[index]);

        return rig;
    }

    private static int Chase(Rig rig, WorldGenBenchContext context)
    {
        int chunks = 0;
        int empty = 0;

        foreach (VoxelColumnKey key in rig.Columns)
        {
            float size = rig.Plan.ChunkMetres(key.Lod);
            float step = rig.Plan.VoxelSize(key.Lod);

            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
            {
                for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
                {
                    float surface = rig.Field.Surface(x, z);
                    lowest = Mathf.Min(lowest, surface);
                    highest = Mathf.Max(highest, surface);
                }
            }

            int low = Mathf.FloorToInt(lowest / size) - 1;
            int high = Mathf.FloorToInt(highest / size) + 1;

            for (int y = low; y <= high; y++)
            {
                while (rig.Queue.Free == 0)
                    Collect(rig, ref empty);

                rig.Queue.TrySchedule(key.Lod, key.X, y, key.Z, key.Seams, key.Morph);
                chunks++;
            }

            rig.Queue.Kick();
        }

        while (rig.Queue.InFlight > 0)
            Collect(rig, ref empty);

        context.Count("chunks", chunks);
        context.Count("emptyChunks", empty);
        return chunks;
    }

    private static void Collect(Rig rig, ref int empty)
    {
        int inFlight = rig.Queue.Drain();

        for (int slot = 0; slot < inFlight; slot++)
        {
            VoxelMesh mesh = rig.Queue.Result(slot, out _);

            if (mesh.IsEmpty)
                empty++;
        }

        rig.Queue.Reset();
    }

    private static WorldGenBenchOp Workers()
    {
        Rig rig = null;
        int workers = 8;

        return new WorldGenBenchOp("que.workers", context => Chase(rig, context))
        {
            Setup = context =>
            {
                workers = context.Args.Int("workers", 8);
                rig = Build(context, workers);
                context.Count("workers", rig.Queue.Workers);
                context.Count("columns", rig.Columns.Count);
            },
            Verify = context =>
            {
                context.Count("scheduleMs", WorldGenProbe.ToMs(WorldGenProbe.Stat(WorldGenStage.VoxelMeshSchedule).Ticks));
                context.Count("blockingMs", WorldGenProbe.ToMs(WorldGenProbe.Stat(WorldGenStage.VoxelMeshBlocking).Ticks));
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp Drain()
    {
        Rig rig = null;

        return new WorldGenBenchOp("que.drain", context =>
        {
            VoxelColumnKey key = rig.Columns[0];
            float size = rig.Plan.ChunkMetres(key.Lod);
            int y = Mathf.FloorToInt(rig.Field.Surface((key.X + 0.5f) * size, (key.Z + 0.5f) * size) / size);

            int scheduled = 0;

            while (rig.Queue.Free > 0)
            {
                rig.Queue.TrySchedule(key.Lod, key.X, y, key.Z, key.Seams, key.Morph);
                scheduled++;
            }

            long kick = WorldGenProbe.Now;
            rig.Queue.Kick();
            context.Count("kickMs", WorldGenProbe.ToMs(WorldGenProbe.Now - kick));

            long drain = WorldGenProbe.Now;
            int inFlight = rig.Queue.Drain();
            context.Count("drainMs", WorldGenProbe.ToMs(WorldGenProbe.Now - drain));

            long collect = WorldGenProbe.Now;

            for (int slot = 0; slot < inFlight; slot++)
                rig.Queue.Result(slot, out _);

            context.Count("collectMs", WorldGenProbe.ToMs(WorldGenProbe.Now - collect));
            context.Count("scheduled", scheduled);
            rig.Queue.Reset();
        })
        {
            Setup = context =>
            {
                rig = Build(context, context.Args.Int("workers", 8));
                context.Require(rig.Columns.Count > 0, "The queue needs at least one column.");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp Batch()
    {
        Rig rig = null;

        return new WorldGenBenchOp("que.batch", context =>
        {
            VoxelColumnKey key = rig.Columns[0];
            float size = rig.Plan.ChunkMetres(key.Lod);
            int surface = Mathf.FloorToInt(rig.Field.Surface((key.X + 0.5f) * size, (key.Z + 0.5f) * size) / size);

            int slot = 0;

            while (rig.Queue.Free > 0)
            {
                int y = slot == 0 ? surface : surface + 16 + slot;
                rig.Queue.TrySchedule(key.Lod, key.X, y, key.Z, key.Seams, key.Morph);
                slot++;
            }

            rig.Queue.Kick();

            long stall = WorldGenProbe.Now;
            int inFlight = rig.Queue.Drain();
            context.Count("mainStallMs", WorldGenProbe.ToMs(WorldGenProbe.Now - stall));

            int heaviest = 0;

            for (int index = 0; index < inFlight; index++)
                heaviest = Math.Max(heaviest, rig.Queue.Result(index, out _).TriangleCount);

            context.Count("heaviestTriangles", heaviest);
            rig.Queue.Reset();
        })
        {
            Setup = context =>
            {
                rig = Build(context, context.Args.Int("workers", 8));
                context.Require(rig.Columns.Count > 0, "The batch needs at least one column.");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static WorldGenBenchOp ColliderDispatch()
    {
        return Colliders("que.colliderDispatch", false);
    }

    private static WorldGenBenchOp ColliderAttach()
    {
        return Colliders("que.colliderAttach", true);
    }

    private static WorldGenBenchOp Colliders(string id, bool attach)
    {
        Rig rig = null;
        VoxelColliderQueue queue = null;
        GameObject parent = null;
        List<Mesh> meshes = new();
        int batch = 32;

        return new WorldGenBenchOp(id, context =>
        {
            for (int index = 0; index < meshes.Count; index++)
            {
                var holder = new GameObject("Collider " + index);
                holder.transform.SetParent(parent.transform, false);
                holder.AddComponent<MeshFilter>().sharedMesh = meshes[index];
                queue.Add(holder, meshes[index]);
            }

            while (queue.Waiting > 0)
            {
                queue.Dispatch();

                if (attach)
                    queue.Collect();
                else
                    queue.Collect();
            }
        })
        {
            Setup = context =>
            {
                rig = Build(context, 4);
                batch = context.Args.Int("batch", 32);
                queue = new VoxelColliderQueue(batch);

                int count = context.Args.Int("meshes", 32);
                VoxelColumnKey key = rig.Columns[0];
                float size = rig.Plan.ChunkMetres(key.Lod);
                int surface = Mathf.FloorToInt(rig.Field.Surface((key.X + 0.5f) * size, (key.Z + 0.5f) * size) / size);

                using var mesher = new VoxelChunkMesher(context.Profile.Voxels, rig.Field);
                using var scratch = new VoxelMesh();

                for (int index = 0; index < count; index++)
                {
                    mesher.Mesh(key.Lod, key.X + index, surface, key.Z, scratch, 0, 0);

                    if (scratch.IsEmpty)
                        continue;

                    var mesh = new Mesh { name = "Bench collider " + index };
                    mesh.SetVertices(scratch.Vertices.AsArray());
                    mesh.SetIndices(scratch.Triangles.AsArray(), MeshTopology.Triangles, 0);
                    mesh.RecalculateBounds();
                    meshes.Add(mesh);
                }

                context.Count("batch", batch);
                context.Count("meshes", meshes.Count);
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Colliders") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context =>
            {
                int attached = parent.GetComponentsInChildren<MeshCollider>(true).Length;
                context.Count("attached", attached);
                context.Count("blockingMs", WorldGenProbe.ToMs(WorldGenProbe.Stat(WorldGenStage.ColliderBlocking).Ticks));
                context.Count("attachMs", WorldGenProbe.ToMs(WorldGenProbe.Stat(WorldGenStage.ColliderAttach).Ticks));
                context.Require(meshes.Count == 0 || attached == meshes.Count, "Every queued mesh must receive a collider.");
            },
            Teardown = context =>
            {
                queue?.Dispose();
                Destroy(parent);

                foreach (Mesh mesh in meshes)
                    DestroyAsset(mesh);

                meshes.Clear();
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp ColliderLag()
    {
        Rig rig = null;
        VoxelColliderQueue queue = null;
        GameObject parent = null;

        return new WorldGenBenchOp("que.colliderLag", context =>
        {
            int empty = 0;
            int spawned = 0;

            foreach (VoxelColumnKey key in rig.Columns)
            {
                float size = rig.Plan.ChunkMetres(key.Lod);
                int y = Mathf.FloorToInt(rig.Field.Surface((key.X + 0.5f) * size, (key.Z + 0.5f) * size) / size);

                if (!rig.Queue.TrySchedule(key.Lod, key.X, y, key.Z, key.Seams, key.Morph))
                {
                    rig.Queue.Kick();
                    int inFlight = rig.Queue.Drain();

                    for (int slot = 0; slot < inFlight; slot++)
                    {
                        VoxelMesh mesh = rig.Queue.Result(slot, out _);

                        if (mesh.IsEmpty)
                        {
                            empty++;
                            continue;
                        }

                        Attach(mesh, parent, queue);
                        spawned++;
                    }

                    rig.Queue.Reset();
                    queue.Dispatch();
                    queue.Collect();
                }
            }

            rig.Queue.Kick();
            int last = rig.Queue.Drain();

            for (int slot = 0; slot < last; slot++)
            {
                VoxelMesh mesh = rig.Queue.Result(slot, out _);

                if (mesh.IsEmpty)
                {
                    empty++;
                    continue;
                }

                Attach(mesh, parent, queue);
                spawned++;
            }

            rig.Queue.Reset();

            while (queue.Waiting > 0)
            {
                context.Add("peakWaiting", 0d);
                context.Count("peakWaiting", Math.Max(context.Work["peakWaiting"], queue.Waiting));
                queue.Dispatch();
                queue.Collect();
            }

            context.Count("spawned", spawned);
            context.Count("emptyChunks", empty);
        })
        {
            Setup = context =>
            {
                rig = Build(context, context.Args.Int("workers", 8));
                queue = new VoxelColliderQueue(context.Args.Int("batch", 32));
            },
            Prepare = context =>
            {
                Destroy(parent);
                parent = new GameObject("WorldGenBench Lag") { hideFlags = HideFlags.HideAndDontSave };
            },
            Verify = context =>
            {
                context.Count("attached", parent.GetComponentsInChildren<MeshCollider>(true).Length);
                context.Count("firstTerrainMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.FirstTerrain));
                context.Count("firstColliderMs", WorldGenProbe.MilestoneMs(WorldGenMilestone.FirstCollider));
            },
            Teardown = context =>
            {
                queue?.Dispose();
                Destroy(parent);
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Dispose()
    {
        Rig rig = null;
        VoxelColliderQueue colliders = null;
        GameObject parent = null;

        return new WorldGenBenchOp("que.dispose", context =>
        {
            VoxelColumnKey key = rig.Columns[0];
            float size = rig.Plan.ChunkMetres(key.Lod);
            int y = Mathf.FloorToInt(rig.Field.Surface((key.X + 0.5f) * size, (key.Z + 0.5f) * size) / size);

            while (rig.Queue.Free > 0)
                rig.Queue.TrySchedule(key.Lod, key.X, y, key.Z, key.Seams, key.Morph);

            rig.Queue.Kick();

            long start = WorldGenProbe.Now;
            rig.Queue.Dispose();
            colliders.Dispose();
            context.Count("disposeMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));

            rig.Queue = new VoxelMeshQueue(context.Profile.Voxels, rig.Field, 8);
            colliders = new VoxelColliderQueue(32);
        })
        {
            Setup = context =>
            {
                rig = Build(context, 8);
                colliders = new VoxelColliderQueue(32);
                parent = new GameObject("WorldGenBench Dispose") { hideFlags = HideFlags.HideAndDontSave };
                context.Require(rig.Columns.Count > 0, "Disposal needs at least one column.");
            },
            Verify = context => context.Note("queues disposed with jobs in flight without a leak report"),
            Teardown = context =>
            {
                colliders?.Dispose();
                Destroy(parent);
                rig?.Dispose();
            }
        };
    }

    private static WorldGenBenchOp Contention()
    {
        Rig rig = null;
        int load = 2;

        return new WorldGenBenchOp("que.contention", context =>
        {
            var threads = new System.Threading.Thread[load];
            var stop = new System.Threading.ManualResetEventSlim(false);

            for (int index = 0; index < load; index++)
            {
                threads[index] = new System.Threading.Thread(() =>
                {
                    double spin = 0d;

                    while (!stop.IsSet)
                    {
                        for (int step = 0; step < 100000; step++)
                            spin += Math.Sqrt(step);
                    }
                })
                { IsBackground = true };

                threads[index].Start();
            }

            try
            {
                Chase(rig, context);
            }
            finally
            {
                stop.Set();

                foreach (System.Threading.Thread thread in threads)
                    thread.Join(2000);
            }
        })
        {
            Setup = context =>
            {
                load = context.Args.Int("load", 2);
                rig = Build(context, context.Args.Int("workers", 8));
                context.Count("backgroundThreads", load);
                context.Note("diagnostic only: background load is synthetic contention, not a product scenario");
            },
            Teardown = context => rig?.Dispose()
        };
    }

    private static void Attach(VoxelMesh source, GameObject parent, VoxelColliderQueue queue)
    {
        var holder = new GameObject("Chunk");
        holder.transform.SetParent(parent.transform, false);

        var mesh = new Mesh();
        mesh.SetVertices(source.Vertices.AsArray());
        mesh.SetIndices(source.Triangles.AsArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        GeneratedMesh.Own(holder);
        queue.Add(holder, mesh);
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

    private static void DestroyAsset(Mesh mesh)
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(mesh);
        else
            Object.DestroyImmediate(mesh);
    }
}
