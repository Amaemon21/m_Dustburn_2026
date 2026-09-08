#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldGenerationEditorRun
{
    public static bool Busy { get; private set; }
    public static float Progress { get; private set; }
    public static string Status { get; private set; } = "Готов к генерации";

    public static void Start(WorldGenerator owner, WorldBuildSettings settings, bool generate)
    {
        if (Busy || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Busy = true;
        Progress = 0f;
        Status = "Подготовка";
        EditorApplication.delayCall += () => Run(owner, settings, generate);
    }

    private static void Run(WorldGenerator owner, WorldBuildSettings settings, bool generate)
    {
        GameObject preview = null;
        bool locked = false;
        bool saved = false;

        try
        {
            if (owner == null || settings == null || !settings.IsValid || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Assign the world controller and all source assets before generating.");

            if (!settings.Biomes.IsValid() || !settings.Pois.IsValid())
                throw new InvalidOperationException("The biome or POI database is invalid. Check the console.");

            BakedWorld world = generate ? null : SavedWorld(owner, settings);

            if (!generate && (world == null || !world.IsValid))
                throw new InvalidOperationException("Generate and save the world before rebuilding its preview.");

            WorldBuildBudget.Validate(settings, generate ? settings.Config : world.Config, settings.PreviewCenter, settings.PreviewColliders);
            EditorApplication.LockReloadAssemblies();
            locked = true;

            if (generate)
            {
                world = new WorldGenerationBake(settings, Report).Generate();
                saved = true;
            }

            if (world == null || !world.IsValid)
                throw new InvalidOperationException("Generate and save the world before rebuilding its preview.");

            Report("Предпросмотр всего мира", 0.65f);
            preview = new GameObject("Предпросмотр мира") { tag = "EditorOnly" };
            preview.transform.SetParent(owner.transform, false);

            var terrainRoot = new GameObject("Ландшафт и растительность");
            terrainRoot.transform.SetParent(preview.transform, false);
            var terrain = terrainRoot.AddComponent<VoxelTerrainBuilder>();
            terrain.hideFlags = HideFlags.HideInInspector;
            terrain.Configure(settings, world);

            if (!terrain.Build((stage, fraction) => Report(stage, 0.65f + fraction * 0.28f)))
                throw new InvalidOperationException("The terrain preview was refused. Check its memory budget and source maps.");

            MakeTransient(terrainRoot);

            var buildings = new GameObject("Здания POI");
            buildings.transform.SetParent(preview.transform, false);
            var pois = new PoiInstanceBuilder(world.Placement.Placements, buildings.transform);

            while (!pois.Ready)
            {
                Report("Размещение зданий POI", 0.93f + pois.Progress * 0.07f);
                pois.BuildNext(16);
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Обновить мир в сцене");
            WorldGenerationMigration.Apply(owner, settings);
            ClearPreview(owner);
            Assign(owner, "_settings", settings);
            Assign(owner, "_world", world);
            Assign(owner, "_previewRoot", preview);
            Assign(owner, "_config", null);
            Assign(owner, "_biomes", null);
            Assign(owner, "_pois", null);
            Undo.RegisterCreatedObjectUndo(preview, "Создать предпросмотр мира");
            Undo.CollapseUndoOperations(undoGroup);
            preview = null;
            EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
            SceneView.RepaintAll();
            Progress = 1f;
            Status = generate ? $"Мир сохранён. Зданий POI: {world.Placement.Placements.Count}" : "Предпросмотр обновлён";
        }
        catch (OperationCanceledException)
        {
            Status = saved ? "Карты сохранены. Обновление предпросмотра отменено." : "Операция отменена. Предыдущий предпросмотр сохранён.";
        }
        catch (Exception exception)
        {
            Status = "Генерация не завершена. Подробности в консоли.";
            Debug.LogException(exception, owner);
        }
        finally
        {
            if (preview != null)
                GeneratedMesh.Destroy(preview);

            EditorUtility.ClearProgressBar();

            if (locked)
                EditorApplication.UnlockReloadAssemblies();

            Busy = false;
        }
    }

    public static void ClearPreview(WorldGenerator owner)
    {
        if (owner == null || Application.isPlaying || owner.PreviewRoot == null)
            return;

        foreach (VoxelTerrainBuilder terrain in owner.PreviewRoot.GetComponentsInChildren<VoxelTerrainBuilder>(true))
            terrain.Clear();

        Undo.DestroyObjectImmediate(owner.PreviewRoot);
        Assign(owner, "_previewRoot", null);
        EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
    }

    public static BakedWorld SavedWorld(WorldGenerator owner, WorldBuildSettings settings)
    {
        if (settings == null || settings.Config == null)
            return null;

        if (owner != null && owner.World != null && AssetDatabase.GetAssetPath(owner.World) == settings.BakedWorldPath)
            return owner.World;

        return AssetDatabase.LoadAssetAtPath<BakedWorld>(settings.BakedWorldPath);
    }

    public static void Assign(WorldGenerator owner, string property, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(owner);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(owner);
        EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
    }

    private static void Report(string stage, float progress)
    {
        Status = stage;
        Progress = Mathf.Clamp01(progress);

        if (EditorUtility.DisplayCancelableProgressBar("Генерация мира", $"{stage} — {Progress:P0}", Progress))
            throw new OperationCanceledException();
    }

    private static void MakeTransient(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }
    }
}
#endif
