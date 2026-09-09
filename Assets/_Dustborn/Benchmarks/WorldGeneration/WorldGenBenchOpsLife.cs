using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldGenBenchOpsLife
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["life.previewCancel"] = PreviewCancel;
        registry["life.preview"] = Preview;
        registry["life.resources"] = Resources;
    }

    private static WorldGenBenchOp PreviewCancel()
    {
#if UNITY_EDITOR
        return new WorldGenBenchOp("life.previewCancel", context =>
        {
            var pipeline = new WorldMapPipeline(context.Profile.Config, context.Profile.Biomes, context.Profile.Pois);
            using var cancellation = new System.Threading.CancellationTokenSource();

            int visited = 0;
            long requested = 0L;
            long observed = 0L;

            try
            {
                pipeline.Generate((stage, fraction) =>
                {
                    if (++visited != context.Args.Int("stopAt", 4))
                        return;

                    requested = WorldGenProbe.Now;
                    cancellation.Cancel();
                }, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                observed = WorldGenProbe.Now;
            }

            context.Count("cleanupMs", observed == 0L ? double.NaN : WorldGenProbe.ToMs(observed - requested));
            context.Count("visitedStages", visited);
        })
        {
            Verify = context =>
            {
                context.Count("liveMeshes", UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().Length);
                context.Note("editor preview cancellation is measured through the same cancellation token the editor run uses");
            }
        };
#else
        return new WorldGenBenchOp("life.previewCancel", context =>
            throw new WorldGenBenchBlockedException("blocked_dependency", "Preview cancellation is editor only."));
#endif
    }

    private static WorldGenBenchOp Preview()
    {
        GameObject host = null;
        VoxelTerrainBuilder builder = null;

        return new WorldGenBenchOp("life.preview", context =>
        {
            builder.Build(null);
            builder.Clear();
        })
        {
            Setup = context =>
            {
                WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

                if (fixture == null || !fixture.HasBake)
                    throw new WorldGenBenchBlockedException("blocked_dependency", "Preview cycles need a valid bake.");

                WorldGenBenchBudget.RequireFullPreview(context, fixture);

                host = new GameObject("WorldGenBench PreviewCycle") { hideFlags = HideFlags.HideAndDontSave };
                builder = host.AddComponent<VoxelTerrainBuilder>();
                builder.Configure(fixture.Settings, fixture.World);
            },
            Verify = context =>
            {
                context.Count("childrenAfterClear", host.transform.childCount);
                context.Count("liveMeshes", UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().Length);
                context.Require(host.transform.childCount == 0, "Clear must leave no preview object behind.");
            },
            Teardown = context =>
            {
                builder?.Clear();

                if (host == null)
                    return;

                if (Application.isPlaying)
                    Object.Destroy(host);
                else
                    Object.DestroyImmediate(host);
            }
        };
    }

    private static WorldGenBenchOp Resources()
    {
        long before = 0L;

        return new WorldGenBenchOp("life.resources", context =>
        {
            HeightMap map = WorldGenBenchOpsVox.Heights(context);

            using var field = new VoxelDensityField(map, context.Profile.Voxels, WorldGenBenchOpsVox.Lowest(map));
            using var mesher = new VoxelChunkMesher(context.Profile.Voxels, field);
            using var mesh = new VoxelMesh();

            float metres = context.Profile.Voxels.ChunkMetres;
            float centre = map.WorldSize * 0.5f;

            mesher.Mesh(0, Mathf.FloorToInt(centre / metres), Mathf.FloorToInt(field.Surface(centre, centre) / metres),
                Mathf.FloorToInt(centre / metres), mesh, 0, 0);

            context.Count("triangles", mesh.TriangleCount);
        })
        {
            Setup = context =>
            {
                before = GC.GetTotalMemory(true);
                context.Count("managedBeforeBytes", before);
                context.Count("liveMeshesBefore", UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().Length);
            },
            Verify = context =>
            {
                long after = GC.GetTotalMemory(true);
                context.Count("managedAfterBytes", after);
                context.Count("managedDeltaBytes", after - before);
                context.Count("liveMeshesAfter", UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().Length);
                context.Note("managed totals only: native and graphics memory need a separate diagnostic capture");
            }
        };
    }
}
