#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WorldGenBenchOpsEditor
{
    static WorldGenBenchOpsEditor()
    {
        WorldGenBench.Extend(Register);
    }

    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["io.write"] = Write;
        registry["io.lock"] = Locked;
        registry["io.import"] = Import;
        registry["io.bake"] = Bake;
        registry["io.bakeFail"] = BakeFail;
    }

    private static WorldGenBenchOp Write()
    {
        byte[] payload = null;
        string path = null;

        return new WorldGenBenchOp("io.write", context => GeneratedAssetFile.WriteAllBytes(path, payload))
        {
            Setup = context =>
            {
                int megabytes = context.Args.Int("megabytes", 8);
                payload = new byte[megabytes * 1024 * 1024];

                var random = new System.Random(11);
                random.NextBytes(payload);

                path = WorldGenBenchPaths.Temporary("write.bytes");
                WorldGenBenchPaths.Ensure();

                if (context.Args.Bool("existing", false))
                    File.WriteAllBytes(Path.GetFullPath(path), new byte[1024]);

                context.Count("bytes", payload.Length);
            },
            Verify = context =>
            {
                byte[] written = File.ReadAllBytes(Path.GetFullPath(path));
                context.Require(written.Length == payload.Length, "The written file must hold every byte.");
                context.Print("written", WorldGenBenchArgs.Hash(written));
            },
            Teardown = context => WorldGenBenchPaths.Clean()
        };
    }

    private static WorldGenBenchOp Locked()
    {
        string path = null;
        FileStream holder = null;
        byte[] payload = null;

        return new WorldGenBenchOp("io.lock", context =>
        {
            try
            {
                GeneratedAssetFile.WriteAllBytes(path, payload);
                context.Put("outcome", "written");
            }
            catch (IOException exception)
            {
                context.Put("outcome", "refused: " + exception.Message);
            }
        })
        {
            Setup = context =>
            {
                payload = new byte[64 * 1024];
                path = WorldGenBenchPaths.Temporary("locked.bytes");
                WorldGenBenchPaths.Ensure();
                File.WriteAllBytes(Path.GetFullPath(path), new byte[1024]);
                holder = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.None);
                context.Count("bytes", payload.Length);
            },
            Verify = context =>
            {
                var outcome = (string)context.State["outcome"];
                context.Note(outcome);
                context.Require(outcome.StartsWith("refused", StringComparison.Ordinal),
                    "A permanently locked destination must be refused rather than truncated.");

                context.Require(new FileInfo(Path.GetFullPath(path)).Length == 1024,
                    "A refused write must leave the previous file intact.");
            },
            Teardown = context =>
            {
                holder?.Dispose();
                WorldGenBenchPaths.Clean();
            }
        };
    }

    private static WorldGenBenchOp Import()
    {
        string path = null;
        Texture2D texture = null;

        return new WorldGenBenchOp("io.import", context =>
        {
            UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
        })
        {
            Setup = context =>
            {
                WorldGenBenchFrozen frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                texture = MaskTexture.Create(frozen.RoadMask, frozen.CarvedHeights.Resolution, "RoadMask");
                path = WorldGenBenchPaths.Temporary("mask.png");
                WorldGenBenchPaths.Ensure();
                GeneratedAssetFile.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEditor.AssetDatabase.Refresh();
                context.Count("width", texture.width);
                context.Count("bytes", new FileInfo(Path.GetFullPath(path)).Length);
            },
            Verify = context =>
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                context.Require(importer != null, "A generated texture must import.");

                long start = WorldGenProbe.Now;
                importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
                importer.maxTextureSize = 16384;
                importer.SaveAndReimport();
                context.Count("saveAndReimportMs", WorldGenProbe.ToMs(WorldGenProbe.Now - start));

                var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                context.Require(loaded != null && loaded.width == texture.width,
                    "The imported texture must keep its width.");
            },
            Teardown = context =>
            {
                Release(texture);
                WorldGenBenchPaths.Clean();
            }
        };
    }

    private static WorldGenBenchOp Bake()
    {
        return new WorldGenBenchOp("io.bake", context =>
        {
            WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();
            var bake = new WorldGenerationBake(fixture.Settings, (stage, fraction) => { });
            BakedWorld world = bake.Generate();
            context.Put("world", world);
        })
        {
            Setup = context =>
            {
                if (!context.Args.Bool("allowSourceWrite", false))
                {
                    throw new WorldGenBenchBlockedException("blocked_dependency",
                        "A full bake writes into the project Generated folder. Pass allowSourceWrite=true to opt in.");
                }
            },
            Verify = context =>
            {
                var world = context.Get<BakedWorld>("world");
                context.Require(world != null && world.IsValid, "The bake must produce a valid world.");
                context.Count("placements", world.Placement.Placements.Count);
            }
        };
    }

    private static WorldGenBenchOp BakeFail()
    {
        return new WorldGenBenchOp("io.bakeFail", context =>
        {
            WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();
            WorldBuildSettings settings = Object.Instantiate(fixture.Settings);
            settings.hideFlags = HideFlags.HideAndDontSave;

            WorldGenerationConfig broken = Object.Instantiate(fixture.Config);
            broken.hideFlags = HideFlags.HideAndDontSave;
            WorldGenBenchProfile.Assign(broken, "HeightMapAssetPath", "NotAssets/HeightMap.bytes");
            WorldGenBenchProfile.Assign(settings, "Config", broken);

            try
            {
                new WorldGenerationBake(settings, (stage, fraction) => { }).Generate();
                context.Put("outcome", "accepted");
            }
            catch (InvalidOperationException exception)
            {
                context.Put("outcome", "refused: " + exception.Message);
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(broken);
            }
        })
        {
            Verify = context =>
            {
                var outcome = (string)context.State["outcome"];
                context.Note(outcome);
                context.Require(outcome.StartsWith("refused", StringComparison.Ordinal),
                    "A bake with an output path outside Assets must be refused before anything is written.");
            }
        };
    }

    private static void Release(Texture2D texture)
    {
        if (texture == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(texture);
        else
            Object.DestroyImmediate(texture);
    }
}
#endif
