#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WorldGenerationBake
{
    private readonly WorldBuildSettings _settings;
    private readonly WorldGenerationConfig _config;
    private readonly Action<string, float> _progress;

    public WorldGenerationBake(WorldBuildSettings settings, Action<string, float> progress)
    {
        _settings = settings;
        _config = settings.Config;
        _progress = progress;
    }

    public BakedWorld Generate()
    {
        long total = WorldGenProbe.Now;
        ValidatePaths();
        WorldMapResult maps = new WorldMapPipeline(_config, _settings.Biomes, _settings.Pois).Generate(_progress);
        BiomeMap biomes = maps.Biomes;
        HeightMap heights = maps.Heights;
        RoadNetwork roads = maps.Roads;
        float[] mask = maps.RoadMask;
        List<PoiPlacement> placements = maps.Placements;

        foreach (PoiPlacement placement in placements)
        {
            if (placement.Prefab == null || !PrefabUtility.IsPartOfPrefabAsset(placement.Prefab))
                throw new InvalidOperationException("Every POI must refer to a prefab asset before saving the world.");
        }

        _progress("Сохранение карт", 0.54f);
        using WorldGenProbe.Span save = WorldGenProbe.Measure(WorldGenStage.BakeMapsSave);

        BakedWorld world = LoadOrCreate<BakedWorld>(_settings.BakedWorldPath);
        world.EditorInvalidate();
        EditorUtility.SetDirty(world);
        AssetDatabase.SaveAssets();

        using (WorldGenProbe.Measure(WorldGenStage.BakeTextures))
        {
            SaveTexture(BiomeMapTexture.Create(biomes, _settings.Biomes), _config.BiomeMapAssetPath);
            SaveTexture(HeightMapTexture.CreateHillshade(heights), _config.HeightMapPreviewPath);
        }

        using (WorldGenProbe.Measure(WorldGenStage.BakeHeightRaw))
            GeneratedAssetFile.WriteAllBytes(_config.HeightMapAssetPath, heights.ToRaw16());

        using (WorldGenProbe.Measure(WorldGenStage.BakeImport))
            AssetDatabase.ImportAsset(_config.HeightMapAssetPath, ImportAssetOptions.ForceUpdate);

        using (WorldGenProbe.Measure(WorldGenStage.BakeTextures))
            SaveTexture(MaskTexture.Create(mask, heights.Resolution, "RoadMask"), _config.RoadMaskAssetPath);

        using (WorldGenProbe.Measure(WorldGenStage.BakeRoadAsset))
        {
            RoadNetworkAsset network = LoadOrCreate<RoadNetworkAsset>(_config.RoadNetworkAssetPath);
            network.EditorSetup(roads);
            EditorUtility.SetDirty(network);
        }

        PoiPlacementAsset placementAsset;

        using (WorldGenProbe.Measure(WorldGenStage.BakePoiAsset))
        {
            placementAsset = LoadOrCreate<PoiPlacementAsset>(_config.PoiPlacementAssetPath);
            placementAsset.EditorSetup(placements);
            EditorUtility.SetDirty(placementAsset);
        }

        var material = new VoxelGroundMaterial.Request
        {
            Config = _config,
            Biomes = _settings.Biomes,
            BiomeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(_config.BiomeMapAssetPath),
            RoadMask = AssetDatabase.LoadAssetAtPath<Texture2D>(_config.RoadMaskAssetPath),
            Map = heights,
            ControlResolution = _settings.ControlResolution,
            MaterialPath = _settings.MaterialPath,
            UseRepetitionless = true
        };

        _progress("Текстуры поверхности", 0.6f);
        string result;

        using (WorldGenProbe.Measure(WorldGenStage.BakeMaterial))
            result = VoxelGroundMaterial.Bake(material, world);

        if (material.Material == null || !result.StartsWith("splatmap:", StringComparison.Ordinal))
            throw new InvalidOperationException($"The ground material was not baked: {result}");

        WorldGenerationConfig snapshot = world.Config;

        if (snapshot == null)
        {
            snapshot = Object.Instantiate(_config);
            snapshot.name = "Baked generation settings";
            snapshot.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(snapshot, world);
        }
        else
        {
            EditorUtility.CopySerialized(_config, snapshot);
        }

        world.EditorSetup(snapshot, _settings.Biomes,
            AssetDatabase.LoadAssetAtPath<TextAsset>(_config.HeightMapAssetPath),
            material.BiomeMap, material.RoadMask, placementAsset, material.Material);
        EditorUtility.SetDirty(snapshot);
        EditorUtility.SetDirty(world);

        using (WorldGenProbe.Measure(WorldGenStage.BakeSaveAssets))
            AssetDatabase.SaveAssets();

        if (!world.IsValid)
            throw new InvalidOperationException("The saved world has inconsistent maps. Check the output paths and resolutions.");

        WorldGenProbe.Record(WorldGenStage.BakeTotal, total, WorldGenProbe.Now, placements.Count);

        return world;
    }

    private void ValidatePaths()
    {
        string root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in new[] { _config.HeightMapAssetPath, _config.BiomeMapAssetPath,
            _config.HeightMapPreviewPath, _config.RoadMaskAssetPath, _config.RoadNetworkAssetPath,
            _config.PoiPlacementAssetPath, _settings.MaterialPath, _settings.BakedWorldPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(Path.GetFullPath(path)))
                throw new InvalidOperationException("Generated asset paths must be distinct files inside Assets.");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
        }
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);

        if (asset != null)
            return asset;

        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            throw new InvalidOperationException($"An incompatible asset already exists at {path}.");

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void SaveTexture(Texture2D texture, string path)
    {
        try
        {
            GeneratedAssetFile.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            throw new InvalidOperationException($"A generated texture could not be imported: {path}");

        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = false;
        importer.isReadable = true;
        importer.maxTextureSize = 16384;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
#endif
