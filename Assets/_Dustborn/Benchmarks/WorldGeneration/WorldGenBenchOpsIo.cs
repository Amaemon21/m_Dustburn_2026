using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldGenBenchOpsIo
{
    public static void Register(Dictionary<string, Func<WorldGenBenchOp>> registry)
    {
        registry["io.raw16"] = Raw16;
        registry["io.textures"] = Textures;
        registry["io.png"] = Png;
        registry["io.assets"] = Assets;
    }

    private static WorldGenBenchOp Raw16()
    {
        HeightMap map = null;
        byte[] bytes = null;
        bool decode = false;

        return new WorldGenBenchOp("io.raw16", context =>
        {
            if (decode)
                context.Put("decoded", HeightMap.FromRaw16(bytes, map.Resolution, map.WorldSize, map.MaxHeight));
            else
                bytes = map.ToRaw16();
        })
        {
            Setup = context =>
            {
                decode = context.Args.Text("direction", "encode") == "decode";
                map = WorldGenBenchFixtures.Frozen(context.Profile, "heights").RawHeights;
                bytes = map.ToRaw16();

                context.Count("bytes", bytes.Length);
                context.Count("resolution", map.Resolution);
            },
            Verify = context =>
            {
                HeightMap round = HeightMap.FromRaw16(map.ToRaw16(), map.Resolution, map.WorldSize, map.MaxHeight);
                double worst = 0d;

                for (int index = 0; index < map.Heights.Length; index++)
                    worst = Math.Max(worst, Math.Abs(map.Heights[index] - round.Heights[index]));

                context.Count("worstRoundTripError", worst);
                context.Require(worst <= 1.0 / 65535.0 + 1e-6, "A raw16 round trip must stay within one quantisation step.");
                context.Print("raw16", WorldGenBenchArgs.Hash(bytes));
            }
        };
    }

    private static WorldGenBenchOp Textures()
    {
        WorldGenBenchFrozen frozen = null;
        Texture2D biomes = null;
        Texture2D hillshade = null;
        Texture2D mask = null;

        return new WorldGenBenchOp("io.textures", context =>
        {
            Release(biomes);
            Release(hillshade);
            Release(mask);

            biomes = BiomeMapTexture.Create(frozen.Biomes, context.Profile.Biomes);
            hillshade = HeightMapTexture.CreateHillshade(frozen.CarvedHeights);
            mask = MaskTexture.Create(frozen.RoadMask, frozen.CarvedHeights.Resolution, "RoadMask");
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                context.Count("biomeResolution", frozen.Biomes.Resolution);
                context.Count("heightResolution", frozen.CarvedHeights.Resolution);
            },
            Verify = context =>
            {
                BiomeMap read = BiomeMapTexture.Read(biomes, context.Profile.Biomes, frozen.Biomes.WorldSize);
                context.Require(read.Resolution == frozen.Biomes.Resolution, "A biome texture must round trip its resolution.");
                context.Count("biomeMismatch", Mismatch(read.Cells, frozen.Biomes.Cells));

                float[] maskBack = MaskTexture.Read(mask);
                context.Require(maskBack.Length == frozen.RoadMask.Length, "The road mask must round trip its size.");

                double worst = 0d;

                for (int index = 0; index < maskBack.Length; index++)
                    worst = Math.Max(worst, Math.Abs(maskBack[index] - frozen.RoadMask[index]));

                context.Count("worstMaskError", worst);
            },
            Teardown = context =>
            {
                Release(biomes);
                Release(hillshade);
                Release(mask);
            }
        };
    }

    private static WorldGenBenchOp Png()
    {
        WorldGenBenchFrozen frozen = null;
        Texture2D texture = null;
        string kind = "biomes";

        return new WorldGenBenchOp("io.png", context =>
        {
            byte[] encoded = texture.EncodeToPNG();
            context.Put("encoded", encoded);
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "carved");
                kind = context.Args.Text("kind", "biomes");

                texture = kind switch
                {
                    "hillshade" => HeightMapTexture.CreateHillshade(frozen.CarvedHeights),
                    "mask" => MaskTexture.Create(frozen.RoadMask, frozen.CarvedHeights.Resolution, "RoadMask"),
                    _ => BiomeMapTexture.Create(frozen.Biomes, context.Profile.Biomes)
                };

                context.Count("width", texture.width);
                context.Count("rawBytes", (long)texture.width * texture.height * 4);
            },
            Verify = context =>
            {
                var encoded = (byte[])context.State["encoded"];
                context.Require(encoded != null && encoded.Length > 8, "PNG encoding must produce bytes.");
                context.Count("pngBytes", encoded.Length);
                context.Count("compressionRatio", (double)encoded.Length / ((long)texture.width * texture.height * 4));
                context.Print("png" + kind, WorldGenBenchArgs.Hash(encoded));
            },
            Teardown = context => Release(texture)
        };
    }

    private static WorldGenBenchOp Assets()
    {
#if UNITY_EDITOR
        string roadPath = null;
        string poiPath = null;
        WorldGenBenchFrozen frozen = null;

        return new WorldGenBenchOp("io.assets", context =>
        {
            var network = ScriptableObject.CreateInstance<RoadNetworkAsset>();
            network.EditorSetup(frozen.Roads);
            UnityEditor.AssetDatabase.CreateAsset(network, roadPath);

            var placement = ScriptableObject.CreateInstance<PoiPlacementAsset>();
            placement.EditorSetup(frozen.Placements ?? new List<PoiPlacement>());
            UnityEditor.AssetDatabase.CreateAsset(placement, poiPath);

            UnityEditor.AssetDatabase.SaveAssets();
        })
        {
            Setup = context =>
            {
                frozen = WorldGenBenchFixtures.Frozen(context.Profile, "placements");
                context.Count("roads", frozen.Roads.Roads.Count);
                context.Count("streets", frozen.Roads.Streets.Count);
                context.Count("placements", frozen.Placements?.Count ?? 0);
            },
            Prepare = context =>
            {
                WorldGenBenchPaths.Ensure();
                roadPath = WorldGenBenchPaths.Temporary($"RoadNetwork_{context.Iteration}.asset");
                poiPath = WorldGenBenchPaths.Temporary($"PoiPlacement_{context.Iteration}.asset");
            },
            Verify = context =>
            {
                var reloaded = UnityEditor.AssetDatabase.LoadAssetAtPath<PoiPlacementAsset>(poiPath);
                context.Require(reloaded != null, "The placement asset must reload.");
                context.Count("reloadedPlacements", reloaded.Placements.Count);
            },
            Teardown = context => WorldGenBenchPaths.Clean()
        };
#else
        return Blocked("io.assets", "AssetDatabase is editor only.");
#endif
    }

    private static WorldGenBenchOp Blocked(string id, string reason)
    {
        return new WorldGenBenchOp(id, context => throw new WorldGenBenchBlockedException("blocked_dependency", reason));
    }

    private static int Mismatch(byte[] first, byte[] second)
    {
        int count = 0;

        for (int index = 0; index < first.Length && index < second.Length; index++)
        {
            if (first[index] != second[index])
                count++;
        }

        return count;
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
