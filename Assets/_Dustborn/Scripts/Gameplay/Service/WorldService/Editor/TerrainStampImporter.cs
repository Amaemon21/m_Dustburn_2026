#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TerrainStampImporter
{
    private const string MANIFEST_PATH = "Assets/_Dustborn/Content/TerrainStamps/terrain_height_stamps/manifest.json";
    private const string DATA_FOLDER = "Assets/_Dustborn/Content/TerrainStamps/TerrainStampData";
    private const string DATABASE_PATH = "Assets/_Dustborn/Content/World/TerrainStampDatabase.asset";
    private const string SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";

    [Serializable]
    private class Manifest
    {
        public int[] resolution;
        public ManifestStamp[] stamps;
    }

    [Serializable]
    private class ManifestStamp
    {
        public string name;
        public string operation;
        public string heightmap_raw16;
        public float[] suggested_footprint_m;
        public float suggested_amplitude_m;
    }

    [MenuItem("Мир/Импорт штампов рельефа")]
    public static void Import()
    {
        try
        {
            TerrainStampDatabase database = Run();
            EditorGUIUtility.PingObject(database);
            EditorUtility.DisplayDialog("Штампы рельефа", $"Импортировано штампов: {database.Count}. База подключена к конфигу генерации.", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Штампы рельефа", exception.Message, "OK");
        }
    }

    public static TerrainStampDatabase Run()
    {
        if (!File.Exists(MANIFEST_PATH))
            throw new FileNotFoundException($"No stamp manifest at {MANIFEST_PATH}");

        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(MANIFEST_PATH));

        if (manifest?.stamps == null || manifest.stamps.Length == 0)
            throw new InvalidDataException("The stamp manifest lists no stamps");

        int resolution = manifest.resolution != null && manifest.resolution.Length > 0 ? manifest.resolution[0] : 1024;
        string pack = Path.GetDirectoryName(MANIFEST_PATH);
        var definitions = new List<TerrainStampDefinition>();

        Directory.CreateDirectory(DATA_FOLDER);

        foreach (ManifestStamp stamp in manifest.stamps)
        {
            TextAsset data = HeightData(stamp, pack, resolution);
            TerrainStampCategory category = TerrainStampCategories.FromName(stamp.name);
            TerrainStampOperation operation = string.Equals(stamp.operation, "subtract", StringComparison.OrdinalIgnoreCase)
                ? TerrainStampOperation.Subtract
                : TerrainStampOperation.Add;

            float[] footprint = stamp.suggested_footprint_m ?? new[] { 1500f, 1500f };

            definitions.Add(new TerrainStampDefinition(stamp.name, data, operation, resolution,
                new Vector2(footprint[0], footprint.Length > 1 ? footprint[1] : footprint[0]), stamp.suggested_amplitude_m,
                category, TerrainStampCategories.DefaultBiomes(category)));
        }

        TerrainStampDatabase database = AssetDatabase.LoadAssetAtPath<TerrainStampDatabase>(DATABASE_PATH);

        if (database == null)
        {
            database = ScriptableObject.CreateInstance<TerrainStampDatabase>();
            AssetDatabase.CreateAsset(database, DATABASE_PATH);
        }

        database.Replace(definitions);
        EditorUtility.SetDirty(database);

        Attach(database);
        AssetDatabase.SaveAssets();

        Debug.Log($"Terrain stamps: {definitions.Count} imported from {MANIFEST_PATH} into {DATABASE_PATH}");

        return database;
    }

    private static TextAsset HeightData(ManifestStamp stamp, string pack, int resolution)
    {
        long expected = (long)resolution * resolution * sizeof(ushort);
        string target = $"{DATA_FOLDER}/{stamp.name}.bytes";

        if (!File.Exists(target))
        {
            string source = Path.Combine(pack, stamp.heightmap_raw16).Replace('\\', '/');

            if (!File.Exists(source))
                throw new FileNotFoundException($"Stamp {stamp.name}: neither {target} nor {source} exists");

            File.Copy(source, target);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
        }

        if (new FileInfo(target).Length != expected)
            throw new InvalidDataException($"Stamp {stamp.name}: {target} is {new FileInfo(target).Length} bytes, a {resolution}x{resolution} RAW16 is {expected}");

        TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(target);

        if (asset == null)
            throw new InvalidDataException($"Stamp {stamp.name}: {target} did not import as a TextAsset");

        return asset;
    }

    private static void Attach(TerrainStampDatabase database)
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(SETTINGS_PATH);

        if (settings == null || settings.Config == null)
            return;

        var config = new SerializedObject(settings.Config);
        SerializedProperty field = config.FindProperty("<Stamps>k__BackingField.<Database>k__BackingField");

        if (field == null || field.objectReferenceValue == database)
            return;

        field.objectReferenceValue = database;
        config.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
