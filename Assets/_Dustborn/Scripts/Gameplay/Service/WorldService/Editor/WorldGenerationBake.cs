#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        WorldMapResult maps = new WorldMapPipeline(_config, _settings.Biomes, _settings.Pois, _settings.Props).Generate(_progress);
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

        foreach (PropPlacement prop in maps.Props)
        {
            if (prop.Prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prop.Prefab))
                throw new InvalidOperationException("Every prop in the PropDatabase must refer to a prefab asset before saving the world.");
        }

        _progress("Сохранение карт", 0.54f);
        using WorldGenProbe.Span save = WorldGenProbe.Measure(WorldGenStage.BakeMapsSave);

        int resolution = heights.Resolution;
        Task<byte[]> hillshade = Task.Run(() => EncodeGray(HeightMapTexture.Hillshade(heights), resolution));
        Task<byte[]> roadMask = Task.Run(() => EncodeGray(MaskTexture.ToGray(mask), resolution));
        Task<byte[]> raw = Task.Run(heights.ToRaw16);
        Task<byte[]> water = Task.Run(() => WaterMapFormat.Write(maps.Water));

        BakedWorld world = LoadOrCreate<BakedWorld>(_settings.BakedWorldPath);
        world.EditorInvalidate();
        EditorUtility.SetDirty(world);
        AssetDatabase.SaveAssets();

        using (WorldGenProbe.Measure(WorldGenStage.BakeTextures))
        {
            GeneratedAssetFile.WriteAllBytes(_config.BiomeMapAssetPath, EncodeTexture(BiomeMapTexture.Create(biomes, _settings.Biomes)));
            GeneratedAssetFile.WriteAllBytes(_config.HeightMapPreviewPath, hillshade.GetAwaiter().GetResult());
            GeneratedAssetFile.WriteAllBytes(_config.RoadMaskAssetPath, roadMask.GetAwaiter().GetResult());
        }

        using (WorldGenProbe.Measure(WorldGenStage.BakeHeightRaw))
            GeneratedAssetFile.WriteAllBytes(_config.HeightMapAssetPath, raw.GetAwaiter().GetResult());

        using (WorldGenProbe.Measure(WorldGenStage.BakeWaterAsset))
            GeneratedAssetFile.WriteAllBytes(_config.WaterMapAssetPath, water.GetAwaiter().GetResult());

        using (WorldGenProbe.Measure(WorldGenStage.BakeImport))
        {
            ImportGenerated(_config.BiomeMapAssetPath, _config.HeightMapPreviewPath, _config.RoadMaskAssetPath,
                _config.HeightMapAssetPath, _config.WaterMapAssetPath);
            ConfigureTextures(_config.BiomeMapAssetPath, _config.HeightMapPreviewPath, _config.RoadMaskAssetPath);
        }

        RoadNetworkAsset network;

        using (WorldGenProbe.Measure(WorldGenStage.BakeRoadAsset))
        {
            network = LoadOrCreate<RoadNetworkAsset>(_config.RoadNetworkAssetPath);
            network.EditorSetup(roads);
            EditorUtility.SetDirty(network);
        }

        PoiPlacementAsset placementAsset;

        using (WorldGenProbe.Measure(WorldGenStage.BakePoiAsset))
        {
            placementAsset = LoadOrCreate<PoiPlacementAsset>(_config.PoiPlacementAssetPath);
            placementAsset.EditorSetup(placements, maps.Props);
            EditorUtility.SetDirty(placementAsset);
        }

        var material = new VoxelGroundMaterial.Request
        {
            Config = _config,
            Biomes = _settings.Biomes,
            BiomeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(_config.BiomeMapAssetPath),
            Roads = roads.Paved(),
            Map = heights,
            Water = maps.Water,
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

        GroundLayerSetAsset.Save(_settings, material);

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
            material.BiomeMap, AssetDatabase.LoadAssetAtPath<Texture2D>(_config.RoadMaskAssetPath), network, placementAsset, material.Material,
            AssetDatabase.LoadAssetAtPath<TextAsset>(_config.WaterMapAssetPath), maps.Stamps);
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
            _config.PoiPlacementAssetPath, _config.WaterMapAssetPath, _settings.MaterialPath, _settings.BakedWorldPath })
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

    private static byte[] EncodeGray(byte[] values, int resolution)
    {
        return ImageConversion.EncodeArrayToPNG(values, GraphicsFormat.R8_UNorm, (uint)resolution, (uint)resolution);
    }

    private static byte[] EncodeTexture(Texture2D texture)
    {
        try
        {
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    private static void ImportGenerated(params string[] paths)
    {
        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private static void ConfigureTextures(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new InvalidOperationException($"A generated texture could not be imported: {path}");

            if (Configured(importer))
                continue;

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

    private static bool Configured(TextureImporter importer)
    {
        return importer.textureType == TextureImporterType.Default
            && importer.npotScale == TextureImporterNPOTScale.None
            && importer.filterMode == FilterMode.Point
            && importer.wrapMode == TextureWrapMode.Clamp
            && !importer.mipmapEnabled
            && !importer.sRGBTexture
            && importer.isReadable
            && importer.maxTextureSize == 16384
            && importer.textureCompression == TextureImporterCompression.Uncompressed;
    }
}
#endif
