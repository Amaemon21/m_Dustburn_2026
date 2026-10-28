using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MainMenuTerrainGrass
{
    private sealed class GrassEntry
    {
        public GrassLayer Layer;
        public DetailPrototype Prototype;
    }

    public static int Plant(TerrainData terrain, MainMenuTerrainLayout layout, MainMenuTerrainMaps maps, string folder, BiomeDefinition forest, int seed, float density, float size)
    {
        var entries = new List<GrassEntry>();
        var readbacks = new Dictionary<(Texture, int, int), Color[]>();
        AddBiome(entries, forest, folder, seed, size, readbacks);
        readbacks.Clear();
        terrain.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        terrain.SetDetailResolution(512, 16);
        var prototypes = new DetailPrototype[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            prototypes[i] = entries[i].Prototype;
        terrain.detailPrototypes = prototypes;
        terrain.wavingGrassAmount = 0.15f;
        terrain.wavingGrassSpeed = 0.25f;
        terrain.wavingGrassStrength = 0.15f;
        terrain.wavingGrassTint = Color.white;

        int resolution = terrain.detailResolution;
        if (entries.Count == 0)
            return 0;
        int cells = resolution * resolution;
        using var surface = new NativeArray<float3>(cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var surfaceJob = new MainMenuGrassSurfaceJob
        {
            Layout = layout,
            Resolution = resolution,
            HeightResolution = maps.Resolution,
            Heights = maps.Heights,
            Surface = surface
        };
        var layerSettings = new MainMenuGrassSettings[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            GrassLayer grass = entries[i].Layer;
            layerSettings[i] = new MainMenuGrassSettings
            {
                MaxSlope = grass.MaxSlope,
                PatchFrequency = grass.PatchFrequency,
                PatchThreshold = grass.PatchThreshold,
                Density = grass.Density,
                Burnt = 0
            };
        }
        using var settings = new NativeArray<MainMenuGrassSettings>(layerSettings, Allocator.Persistent);
        using var instances = new NativeArray<int>(cells * entries.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        using var rowCounts = new NativeArray<int>(resolution * entries.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var scatterJob = new MainMenuGrassScatterJob
        {
            Layout = layout,
            Resolution = resolution,
            Seed = unchecked((uint)seed),
            Density = density,
            Layers = settings,
            Surface = surface,
            Instances = instances,
            RowCounts = rowCounts
        };
        MainMenuTerrainMaps.Complete(surfaceJob.Schedule(resolution, 4), "Caching grass surface masks with Burst", 0.62f);
        MainMenuTerrainMaps.Complete(scatterJob.Schedule(rowCounts.Length, 4), "Seeding all grass layers with Burst", 0.78f);

        var flat = new int[cells];
        var map = new int[resolution, resolution];
        for (int i = 0; i < entries.Count; i++)
        {
            MainMenuTerrainGenerator.Progress($"Applying grass {i + 1}/{entries.Count}", 0.85f + 0.1f * i / entries.Count);
            instances.GetSubArray(i * cells, cells).CopyTo(flat);
            Buffer.BlockCopy(flat, 0, map, 0, cells * sizeof(int));
            terrain.SetDetailLayer(0, 0, i, map);
        }
        int count = 0;
        for (int i = 0; i < rowCounts.Length; i++)
            count += rowCounts[i];
        return count;
    }

    private static void AddBiome(List<GrassEntry> entries, BiomeDefinition biome, string folder, int seed, float size, Dictionary<(Texture, int, int), Color[]> readbacks)
    {
        for (int i = 0; i < biome.Grass.Count; i++)
        {
            GrassLayer grass = biome.Grass[i];
            if (grass == null || grass.IsMute || grass.Card == null || grass.ShoreDistance > 0f)
                continue;

            MainMenuTerrainGenerator.Progress($"Preparing {biome.name} grass {i + 1}", 0.57f);
            string name = $"Forest_Grass_{i:00}";
            Texture2D texture = BakeTexture(grass, folder + "/" + name + ".png", readbacks);
            var prototype = new DetailPrototype
            {
                prototypeTexture = texture,
                renderMode = DetailRenderMode.GrassBillboard,
                usePrototypeMesh = false,
                useInstancing = false,
                useDensityScaling = true,
                healthyColor = Color.white,
                dryColor = Color.Lerp(Color.white, new Color(0.82f, 0.78f, 0.7f), grass.ColorVariation),
                minWidth = grass.MinWidth * size,
                maxWidth = grass.MaxWidth * size,
                minHeight = grass.MinHeight * size,
                maxHeight = grass.MaxHeight * size,
                noiseSpread = grass.PatchFrequency,
                noiseSeed = unchecked(seed + entries.Count * 193),
                positionJitter = 0.9f,
                holeEdgePadding = 0.2f
            };
            if (!prototype.Validate(out string error))
                throw new InvalidOperationException($"Invalid grass prototype {name}: {error}");
            entries.Add(new GrassEntry { Layer = grass, Prototype = prototype });
        }
    }

    public static int RemoveBurnt(TerrainData terrain)
    {
        DetailPrototype[] original = terrain.detailPrototypes;
        var keep = new List<DetailPrototype>();
        var maps = new List<int[,]>();
        int removed = 0;
        for (int i = 0; i < original.Length; i++)
        {
            Texture2D texture = original[i].prototypeTexture;
            if (texture != null && texture.name.StartsWith("Burnt_Grass_", StringComparison.Ordinal))
            {
                removed++;
                continue;
            }
            keep.Add(original[i]);
            maps.Add(terrain.GetDetailLayer(0, 0, terrain.detailWidth, terrain.detailHeight, i));
        }
        if (removed == 0)
            return 0;
        terrain.detailPrototypes = keep.ToArray();
        for (int i = 0; i < maps.Count; i++)
            terrain.SetDetailLayer(0, 0, i, maps[i]);
        EditorUtility.SetDirty(terrain);
        return removed;
    }

    private static Texture2D BakeTexture(GrassLayer grass, string path, Dictionary<(Texture, int, int), Color[]> readbacks)
    {
        int width = Mathf.Min(512, grass.Card.width);
        int height = Mathf.Max(1, Mathf.RoundToInt((float)grass.Card.height / grass.Card.width * width));
        Color[] source = ReadPixels(grass.Card, width, height, readbacks);
        Color[] mask = grass.Mask != null ? ReadPixels(grass.Mask, width, height, readbacks) : null;
        Color leaves = grass.LeavesColor.linear;
        Color stems = grass.StemsColor.linear;
        Color flowers = grass.FlowersColor.linear;
        Color tip = grass.TipTint.linear;
        using var nativeSource = new NativeArray<Color>(source, Allocator.Persistent);
        using var nativeMask = mask != null ? new NativeArray<Color>(mask, Allocator.Persistent) : new NativeArray<Color>(0, Allocator.Persistent);
        using var pixels = new NativeArray<Color32>(source.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var recolor = new MainMenuGrassRecolorJob
        {
            Width = width,
            Height = height,
            HasMask = mask != null,
            Leaves = new float3(leaves.r, leaves.g, leaves.b),
            Stems = new float3(stems.r, stems.g, stems.b),
            Flowers = new float3(flowers.r, flowers.g, flowers.b),
            Tip = new float3(tip.r, tip.g, tip.b),
            RootDarkening = grass.RootDarkening,
            TextureShading = grass.TextureShading,
            Source = nativeSource,
            Mask = nativeMask,
            Pixels = pixels
        };
        MainMenuTerrainMaps.Complete(recolor.Schedule(pixels.Length, 128), "Recoloring grass textures with Burst", 0.57f);

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixelData(pixels, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 512;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color[] ReadPixels(Texture source, int width, int height, Dictionary<(Texture, int, int), Color[]> readbacks)
    {
        var key = (source, width, height);
        if (readbacks.TryGetValue(key, out Color[] cached))
            return cached;
        RenderTexture previous = RenderTexture.active;
        bool previousWrite = GL.sRGBWrite;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Texture2D readable = null;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply();
            Color[] pixels = readable.GetPixels();
            readbacks.Add(key, pixels);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousWrite;
            RenderTexture.ReleaseTemporary(target);
            if (readable != null)
                Object.DestroyImmediate(readable);
        }
    }
}
