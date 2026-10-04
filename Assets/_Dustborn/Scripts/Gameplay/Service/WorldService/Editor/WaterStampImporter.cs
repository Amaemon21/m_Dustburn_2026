#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WaterStampImporter
{
    private const string PACK = "Assets/_Dustborn/Content/WaterStamps";
    private const string MANIFEST_PATH = PACK + "/manifest.json";
    private const string DATA_FOLDER = PACK + "/WaterStampData";
    private const string DATABASE_PATH = "Assets/_Dustborn/Content/World/WaterStampDatabase.asset";
    private const string SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";
    private const string DATABASE_PROPERTY = "<Water>k__BackingField.<Stamps>k__BackingField.<Database>k__BackingField";

    [MenuItem("Мир/Импорт штампов воды")]
    public static void Import()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Штампы воды", "Манифест", 0f);
            WaterStampDatabase database = Run(out string summary);
            EditorGUIUtility.PingObject(database);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Штампы воды", summary, "OK");
        }
        catch (Exception exception)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Штампы воды", "Импорт не выполнен: " + exception.Message, "OK");
        }
    }

    public static WaterStampDatabase Run(out string summary)
    {
        if (!File.Exists(MANIFEST_PATH))
            throw new FileNotFoundException($"No water stamp manifest at {MANIFEST_PATH}");

        var manifest = JsonUtility.FromJson<WaterStampManifest>(File.ReadAllText(MANIFEST_PATH));

        if (manifest == null)
            throw new InvalidDataException($"{MANIFEST_PATH} is not a water stamp manifest");

        List<string> errors = manifest.Validate();

        if (errors.Count > 0)
            throw new InvalidDataException($"{MANIFEST_PATH}: " + string.Join("; ", errors));

        Directory.CreateDirectory(DATA_FOLDER);

        var warnings = new List<string>();
        var definitions = new List<WaterStampDefinition>();
        int rivers = 0, lakes = 0, ponds = 0, branches = 0;

        for (int i = 0; i < manifest.stamps.Length; i++)
        {
            WaterStampManifestEntry entry = manifest.stamps[i];
            EditorUtility.DisplayProgressBar("Штампы воды", entry.name, (i + 0.5f) / manifest.stamps.Length);

            byte[] bytes = Mask(entry, manifest.ExpectedBytes, out TextAsset asset, manifest.Resolution);
            WaterStampDefinition definition = entry.ToDefinition(asset, manifest.Resolution, warnings);

            WaterStampTracer.Trace(definition, WaterStampShape.Decode(bytes, manifest.Resolution));

            if (!definition.IsValid)
                throw new InvalidDataException($"Water stamp {entry.name} did not trace into a usable definition");

            definitions.Add(definition);
            branches += definition.Branches.Count;

            switch (definition.Kind)
            {
                case WaterStampKind.River:
                    rivers++;
                    break;
                case WaterStampKind.Lake:
                    lakes++;
                    break;
                default:
                    ponds++;
                    break;
            }
        }

        foreach (string warning in warnings)
            Debug.LogWarning(warning);

        WaterStampDatabase database = AssetDatabase.LoadAssetAtPath<WaterStampDatabase>(DATABASE_PATH);

        if (database == null)
        {
            database = ScriptableObject.CreateInstance<WaterStampDatabase>();
            AssetDatabase.CreateAsset(database, DATABASE_PATH);
        }

        database.Replace(definitions);
        EditorUtility.SetDirty(database);

        bool attached = Attach(database);
        AssetDatabase.SaveAssets();

        Debug.Log($"Water stamps: {definitions.Count} imported from {MANIFEST_PATH} into {DATABASE_PATH} ({rivers} rivers, {lakes} lakes, {ponds} ponds, "
            + $"{branches} branch sockets traced), SHA-256 verified; {(attached ? "attached to the world generation config" : "config not found, attach the database by hand")}");

        summary = $"Импортировано штампов: {definitions.Count} (рек {rivers}, озёр {lakes}, прудов {ponds}), контрольные суммы совпали, оси русел и {branches} веток прослежены. "
            + (attached ? "База подключена к конфигу генерации." : "Конфиг генерации не найден — подключите базу вручную.");

        return database;
    }

    private static byte[] Mask(WaterStampManifestEntry entry, long expected, out TextAsset asset, int resolution)
    {
        string source = Path.Combine(PACK, entry.mask_raw16).Replace('\\', '/');

        if (!File.Exists(source))
            throw new FileNotFoundException($"Water stamp {entry.name}: {source} does not exist");

        byte[] bytes = File.ReadAllBytes(source);

        if (bytes.Length != expected)
            throw new InvalidDataException($"Water stamp {entry.name}: {source} is {bytes.Length} bytes, a {resolution}x{resolution} RAW16 is {expected}");

        string hash = WaterStampManifest.Sha256(bytes);

        if (!string.Equals(hash, entry.sha256_raw16, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Water stamp {entry.name}: SHA-256 of {source} is {hash}, the manifest says {entry.sha256_raw16}");

        string target = $"{DATA_FOLDER}/{entry.name}.bytes";

        if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length || WaterStampManifest.Sha256(File.ReadAllBytes(target)) != hash)
        {
            File.WriteAllBytes(target, bytes);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
        }

        asset = AssetDatabase.LoadAssetAtPath<TextAsset>(target);

        if (asset == null)
            throw new InvalidDataException($"Water stamp {entry.name}: {target} did not import as a TextAsset");

        return bytes;
    }

    private static bool Attach(WaterStampDatabase database)
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(SETTINGS_PATH);

        if (settings == null || settings.Config == null)
            return false;

        var config = new SerializedObject(settings.Config);
        SerializedProperty field = config.FindProperty(DATABASE_PROPERTY);

        if (field == null)
            return false;

        if (field.objectReferenceValue == database)
            return true;

        field.objectReferenceValue = database;
        config.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings.Config);

        return true;
    }
}
#endif
