using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class PropContentSetup
{
    private const string MENU = "Мир/Реквизит/Собрать машины, хлам и тайники";
    private const string PROGRESS_TITLE = "Машины, хлам и тайники";
    private const string PROPS_ROOT = "Assets/_Dustborn/Content/Prefabs/Props";
    private const string SYNTY_PROPS = SYNTY_PREFABS + "/Props";
    private const string PROP_DATABASE = "Assets/_Dustborn/Content/Configs/World/PropDatabase.asset";
    private const float FLAT_SLOPE = 8f;
    private const float LOOSE_WEIGHT = 0.5f;
    private const float LOOSE_VEHICLE_WEIGHT = 2f;

    private enum PropRole
    {
        Decor,
        Breakable,
        Searchable
    }

    private readonly struct PropRow
    {
        public readonly string Prefab;
        public readonly string Folder;
        public readonly PropContext Contexts;
        public readonly float Weight;
        public readonly PropRole Role;
        public readonly string Config;
        public readonly bool OnRoad;
        public readonly bool AlongRoad;
        public readonly float YawJitter;
        public readonly float MaxSlope;
        public readonly int ClusterMin;
        public readonly int ClusterMax;
        public readonly float ClusterRadius;

        public PropRow(string prefab, PropContext contexts, float weight, PropRole role, string config, bool onRoad,
            bool alongRoad, float yawJitter, float maxSlope, int clusterMin, int clusterMax, float clusterRadius, string folder = null)
        {
            Prefab = prefab;
            Folder = folder;
            Contexts = contexts;
            Weight = weight;
            Role = role;
            Config = config;
            OnRoad = onRoad;
            AlongRoad = alongRoad;
            YawJitter = yawJitter;
            MaxSlope = maxSlope;
            ClusterMin = clusterMin;
            ClusterMax = clusterMax;
            ClusterRadius = clusterRadius;
        }

        public PropRow With(string folder = null, bool? onRoad = null, bool? alongRoad = null, float? yawJitter = null, float? maxSlope = null)
            => new(Prefab, Contexts, Weight, Role, Config, onRoad ?? OnRoad, alongRoad ?? AlongRoad, yawJitter ?? YawJitter,
                maxSlope ?? MaxSlope, ClusterMin, ClusterMax, ClusterRadius, folder ?? Folder);
    }

    [MenuItem(MENU)]
    private static void Run()
    {
        var report = new StringBuilder();

        try
        {
            Dictionary<string, InventoryItem> items = EnsureItems(report);
            Dictionary<string, BlockConfig> blocks = EnsureBlocks(items, report);
            Dictionary<string, ContainerConfig> loot = EnsureLoot(items, report);
            PropDatabase database = EnsureAsset<PropDatabase>(PROP_DATABASE, report);
            int added = 0;
            int roles = 0;

            for (int i = 0; i < ROWS.Length; i++)
            {
                PropRow row = ROWS[i];
                EditorUtility.DisplayProgressBar(PROGRESS_TITLE, row.Prefab, i / (float)ROWS.Length);

                GameObject prefab = EnsurePrefab(row, report);
                if (prefab == null)
                    continue;

                if (ApplyRole(prefab, row, blocks, loot, report))
                    roles++;

                if (database.EditorContains(prefab))
                    continue;

                database.EditorAdd(new PropDefinition(prefab, row.Contexts, row.Weight, row.OnRoad, row.AlongRoad, row.YawJitter,
                    row.MaxSlope, row.ClusterMin, row.ClusterMax, row.ClusterRadius, null));
                added++;
            }

            int loose = AddLoosePrefabs(database, report);

            EditorUtility.SetDirty(database);
            database.MeasurePrefabs();
            AssignToSettings(database, report);
            AssetDatabase.SaveAssets();

            report.Insert(0, $"Props: {added} new definitions from the catalog and {loose} from loose prefabs under {PROPS_ROOT} in {PROP_DATABASE}, "
                + $"roles set on {roles} prefabs. Generate the world to place them.\n");
            Debug.Log(report.ToString(), database);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static GameObject EnsurePrefab(PropRow row, StringBuilder report)
    {
        string folder = $"{PROPS_ROOT}/{row.Folder}";
        string target = $"{folder}/{row.Prefab}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target) ?? FindUnder(PROPS_ROOT, row.Prefab);

        if (prefab != null)
            return prefab;

        string source = $"{SYNTY_PROPS}/{row.Prefab}.prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
        {
            report.AppendLine($"Missing prefab {row.Prefab}: there is no {source}, put it into {folder} or remove the row");
            return null;
        }

        EnsureFolder(folder);

        if (!AssetDatabase.CopyAsset(source, target))
        {
            report.AppendLine($"Could not copy {source} into {folder}");
            return null;
        }

        report.AppendLine($"Copied {row.Prefab} from Synty Props into {row.Folder}");
        return AssetDatabase.LoadAssetAtPath<GameObject>(target);
    }

    private static int AddLoosePrefabs(PropDatabase database, StringBuilder report)
    {
        int added = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PROPS_ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null || database.EditorContains(prefab))
                continue;

            PropDefinition definition = LooseDefinition(prefab, path);
            database.EditorAdd(definition);
            report.AppendLine($"{prefab.name}: not in the catalog, added as {definition.Context} by its folder");
            added++;
        }

        return added;
    }

    private static PropDefinition LooseDefinition(GameObject prefab, string path)
    {
        bool vehicle = path.StartsWith($"{PROPS_ROOT}/{VEHICLES}/", StringComparison.OrdinalIgnoreCase);

        return vehicle
            ? new PropDefinition(prefab, ROAD | STREET, LOOSE_VEHICLE_WEIGHT, true, true, 20f, 14f, 1, 1, 0f, null)
            : new PropDefinition(prefab, EVERYWHERE, LOOSE_WEIGHT, false, false, 180f, 20f, 1, 1, 0f, null);
    }

    private static GameObject FindUnder(string root, string name)
    {
        if (!AssetDatabase.IsValidFolder(root))
            return null;

        foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }

    private static bool ApplyRole(GameObject prefab, PropRow row, Dictionary<string, BlockConfig> blocks,
        Dictionary<string, ContainerConfig> loot, StringBuilder report)
    {
        if (row.Role == PropRole.Decor)
            return false;

        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            bool changed = row.Role == PropRole.Searchable
                ? MakeSearchable(root, Lookup(loot, row.Config, row.Prefab, report))
                : MakeBreakable(root, Lookup(blocks, row.Config, row.Prefab, report));

            if (!changed)
                return false;

            EnsureCollider(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool MakeSearchable(GameObject root, ContainerConfig config)
    {
        if (config == null || root.GetComponent<BlockView>() != null)
            return false;

        if (!root.TryGetComponent(out ContainerInteractable container))
            container = root.AddComponent<ContainerInteractable>();
        else if (container.Config != null)
            return false;

        container.EditorSetup(config);
        return true;
    }

    private static bool MakeBreakable(GameObject root, BlockConfig config)
    {
        if (config == null || root.GetComponent<ContainerInteractable>() != null)
            return false;

        if (!root.TryGetComponent(out BlockView block))
            block = root.AddComponent<BlockView>();
        else if (block.Config != null)
            return false;

        block.EditorSetup(config);
        return true;
    }

    private static void EnsureCollider(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>(true) != null || !PrefabBounds.TryMeasure(root, out Bounds bounds))
            return;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
    }

    private static T Lookup<T>(Dictionary<string, T> configs, string name, string prefab, StringBuilder report) where T : Object
    {
        if (name != null && configs.TryGetValue(name, out T config) && config != null)
            return config;

        report.AppendLine($"{prefab}: no config '{name}', its role was skipped");
        return null;
    }

    private static void AssignToSettings(PropDatabase database, StringBuilder report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(WorldBuildSettings)))
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(AssetDatabase.GUIDToAssetPath(guid));
            if (settings == null || settings.Props != null)
                continue;

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("<Props>k__BackingField").objectReferenceValue = database;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"Assigned the prop database to {settings.name}");
        }
    }

    private static T EnsureAsset<T>(string path, StringBuilder report) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        report.AppendLine($"Created {path}");
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
