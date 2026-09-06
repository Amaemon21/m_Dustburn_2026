using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NaughtyAttributes;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class WorldGenerator : MonoBehaviour
{
    [BoxGroup("Assets"), SerializeField]
    private WorldGenerationConfig _config;

    [BoxGroup("Assets"), SerializeField]
    private BiomeDatabase _biomes;

    [BoxGroup("Assets"), SerializeField]
    private PoiDatabase _pois;

    [Foldout("Preview"), ShowAssetPreview(320, 320), SerializeField] private Texture2D _biomeMapPreview;
    [Foldout("Preview"), ShowAssetPreview(320, 320), SerializeField] private Texture2D _heightMapPreview;
    [Foldout("Preview"), ShowAssetPreview(320, 320), SerializeField] private Texture2D _roadMaskPreview;

    private BiomeMap _biomeMap;
    private HeightMap _rawHeightMap;
    private HeightMap _roadsHeightMap;
    private HeightMap _cityHeightMap;
    private RoadNetwork _roadNetwork;
    private List<CityLayout> _cityLayouts;
    private List<PoiPlacement> _placements;
    private PoiPlacer _placer;
    private float[] _roadMask;
    private float[] _cityMask;

    public BiomeMap BiomeMap => _biomeMap;
    public HeightMap HeightMap => _cityHeightMap ?? _roadsHeightMap ?? _rawHeightMap;
    public RoadNetwork RoadNetwork => _roadNetwork;

    [ShowNativeProperty]
    public string BiomeMapStatus => _biomeMap == null
        ? "not generated"
        : $"{_biomeMap.Resolution}x{_biomeMap.Resolution} cells of {_biomeMap.CellSize:F1} m";

    [ShowNativeProperty]
    public string HeightMapStatus => _rawHeightMap == null
        ? "not generated"
        : $"{_rawHeightMap.Resolution}x{_rawHeightMap.Resolution}, {_rawHeightMap.MaxHeight:F0} m max";

    [ShowNativeProperty]
    public string RoadNetworkStatus => _roadNetwork == null
        ? "not generated"
        : $"{_roadNetwork.Hubs.Count} hubs, {_roadNetwork.Roads.Count} roads";

    [ShowNativeProperty]
    public string CityStatus => _cityLayouts == null
        ? "not planned"
        : $"{_cityLayouts.Count} settlements, {_roadNetwork.Streets.Count} streets, {_placements.Count} POI";

    [Button("1. Generate Biome Map")]
    private void GenerateBiomeMap()
    {
        if (!Validate())
            return;

        var stopwatch = Stopwatch.StartNew();

        _biomeMap = new BiomeMapGenerator(_config, _biomes).Generate();
        Replace(ref _biomeMapPreview, BiomeMapTexture.Create(_biomeMap, _biomes));

        Debug.Log($"Biome map: {_biomeMap.Resolution}x{_biomeMap.Resolution} cells, {_biomeMap.CellSize:F1} m per cell, {stopwatch.ElapsedMilliseconds} ms", this);
    }

    [Button("2. Generate Height Map")]
    private void GenerateHeightMap()
    {
        if (!Validate())
            return;

        if (_biomeMap == null)
            GenerateBiomeMap();

        var stopwatch = Stopwatch.StartNew();

        _rawHeightMap = new HeightMapGenerator(_config, _biomes).Generate(_biomeMap);

        ResetRoads();

        Replace(ref _heightMapPreview, HeightMapTexture.CreateHillshade(_rawHeightMap));

        Debug.Log($"Height map: {_rawHeightMap.Resolution}x{_rawHeightMap.Resolution}, {_config.HeightCellSize} m per sample, {stopwatch.ElapsedMilliseconds} ms", this);
    }

    [Button("3. Generate Hubs And Roads")]
    private void GenerateRoads()
    {
        if (!Validate())
            return;

        if (_rawHeightMap == null)
            GenerateHeightMap();

        var stopwatch = Stopwatch.StartNew();

        ResetRoads();

        _roadNetwork = new RoadNetwork();
        _roadNetwork.Hubs.AddRange(new HubPlacer(_config, _rawHeightMap).Place());
        _roadNetwork.Roads.AddRange(new RoadPlanner(_config, _rawHeightMap).Plan(_roadNetwork.Hubs));

        _roadsHeightMap = new TerrainCarver(_config).Carve(_rawHeightMap, _roadNetwork, out _roadMask);

        Replace(ref _heightMapPreview, HeightMapTexture.CreateHillshade(_roadsHeightMap));
        Replace(ref _roadMaskPreview, MaskTexture.Create(_roadMask, _roadsHeightMap.Resolution, "RoadMask"));

        Debug.Log($"Hubs: {_roadNetwork.Hubs.Count}, roads: {_roadNetwork.Roads.Count}, {stopwatch.ElapsedMilliseconds} ms", this);
    }

    [Button("4. Generate Cities And POI")]
    private void GenerateCities()
    {
        if (!Validate())
            return;

        if (_roadsHeightMap == null)
            GenerateRoads();

        if (_pois == null)
        {
            Debug.LogError("No settlements built: PoiDatabase is not assigned. Create one through Create/World/POI Database and drop it into the Pois field", this);
            return;
        }

        if (!_pois.IsValid())
            return;

        var stopwatch = Stopwatch.StartNew();

        _cityLayouts = new CityPlanner(_config, _pois, _roadsHeightMap).Plan(_roadNetwork.Hubs, _roadNetwork.Roads);

        _roadNetwork.Streets.Clear();

        foreach (CityLayout layout in _cityLayouts)
            _roadNetwork.Streets.AddRange(layout.Streets);

        _cityMask = (float[])_roadMask.Clone();

        var carver = new TerrainCarver(_config);

        HeightMap withStreets = carver.CarveStreets(_roadsHeightMap, _cityLayouts, _cityMask);

        var roadProximity = new RoadProximity(_roadNetwork.Roads, _config.WorldSize, _config.RoadCellSize);

        roadProximity.AddRange(_roadNetwork.Streets);

        _placer = new PoiPlacer(_config, _pois, withStreets, roadProximity);
        _placements = _placer.Place(_cityLayouts, _roadNetwork);

        _cityHeightMap = carver.CarvePads(withStreets, _placements);
        _placer.ApplyHeights(_cityHeightMap);

        Replace(ref _heightMapPreview, HeightMapTexture.CreateHillshade(_cityHeightMap));
        Replace(ref _roadMaskPreview, MaskTexture.Create(_cityMask, _cityHeightMap.Resolution, "RoadMask"));

        Debug.Log($"Cities: {_cityLayouts.Count}, streets: {_roadNetwork.Streets.Count}, POI: {_placements.Count}, {stopwatch.ElapsedMilliseconds} ms", this);
    }

    [Button("5. Save Generated Maps")]
    private void SaveGeneratedMaps()
    {
        if (_biomeMap != null)
            SaveTexture(_biomeMapPreview, _config.BiomeMapAssetPath);

        if (HeightMap == null)
            return;

        SaveTexture(_heightMapPreview, _config.HeightMapPreviewPath);
        SaveBytes(HeightMap.ToRaw16(), _config.HeightMapAssetPath);

        if (_roadMask == null)
            return;

        SaveTexture(_roadMaskPreview, _config.RoadMaskAssetPath);
        SaveRoadNetwork();

        if (_placements == null)
        {
            Debug.LogWarning("POI not saved: step 4 has not run yet, the world will hold no buildings", this);
            return;
        }

        SavePoiPlacement();
    }

    [Button("Generate And Save All")]
    private void GenerateAndSaveAll()
    {
        GenerateBiomeMap();
        GenerateHeightMap();
        GenerateRoads();
        GenerateCities();
        SaveGeneratedMaps();
    }

    private void ResetRoads()
    {
        _roadsHeightMap = null;
        _cityHeightMap = null;
        _roadNetwork = null;
        _cityLayouts = null;
        _placements = null;
        _placer = null;
        _roadMask = null;
        _cityMask = null;
        Replace(ref _roadMaskPreview, null);
    }

    private static void Replace(ref Texture2D field, Texture2D texture)
    {
        Texture2D previous = field;

        field = texture;

        if (previous == null || previous == texture)
            return;

#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.Contains(previous))
            return;
#endif

        if (Application.isPlaying)
            Destroy(previous);
        else
            DestroyImmediate(previous);
    }

    private bool Validate()
    {
        if (_config == null)
        {
            Debug.LogError("WorldGenerationConfig is not assigned", this);
            return false;
        }

        if (_biomes == null || !_biomes.IsValid())
        {
            Debug.LogError("BiomeDatabase is not assigned or contains empty slots", this);
            return false;
        }

        return true;
    }

    private void SaveRoadNetwork()
    {
#if UNITY_EDITOR
        var asset = LoadOrCreate<RoadNetworkAsset>(_config.RoadNetworkAssetPath, out bool created);

        asset.EditorSetup(_roadNetwork);

        Commit(asset, _config.RoadNetworkAssetPath, created);
#endif
    }

    private void SavePoiPlacement()
    {
#if UNITY_EDITOR
        var asset = LoadOrCreate<PoiPlacementAsset>(_config.PoiPlacementAssetPath, out bool created);

        asset.EditorSetup(_placements);

        Commit(asset, _config.PoiPlacementAssetPath, created);
#endif
    }

#if UNITY_EDITOR
    private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);

        created = asset == null;

        return created ? ScriptableObject.CreateInstance<T>() : asset;
    }

    private void Commit(ScriptableObject asset, string path, bool created)
    {
        if (created)
            UnityEditor.AssetDatabase.CreateAsset(asset, path);

        UnityEditor.EditorUtility.SetDirty(asset);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"Saved {path}", this);
    }
#endif

    private void SaveTexture(Texture2D texture, string assetPath)
    {
        if (texture == null)
            return;

        SaveBytes(texture.EncodeToPNG(), assetPath);

#if UNITY_EDITOR
        ConfigureTextureImporter(assetPath);
#endif
    }

    private void SaveBytes(byte[] bytes, string assetPath)
    {
#if UNITY_EDITOR
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
        File.WriteAllBytes(assetPath, bytes);

        UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);

        Debug.Log($"Saved {assetPath} ({bytes.Length / 1024} KB)", this);
#else
        string path = Path.Combine(Application.persistentDataPath, Path.GetFileName(assetPath));

        File.WriteAllBytes(path, bytes);

        Debug.Log($"Saved {path} ({bytes.Length / 1024} KB)", this);
#endif
    }

#if UNITY_EDITOR
    private static void ConfigureTextureImporter(string assetPath)
    {
        var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;

        if (importer == null)
            return;

        importer.textureType = UnityEditor.TextureImporterType.Default;
        importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = false;
        importer.isReadable = true;
        importer.maxTextureSize = 8192;
        importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;

        importer.SaveAndReimport();
    }
#endif
}
