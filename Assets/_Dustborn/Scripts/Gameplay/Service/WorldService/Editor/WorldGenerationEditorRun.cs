#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class WorldGenerationEditorRun
{
    public readonly struct StageTime
    {
        public string Stage { get; }
        public double Seconds { get; }

        public StageTime(string stage, double seconds)
        {
            Stage = stage;
            Seconds = seconds;
        }
    }

    private static readonly (WorldGenStage Stage, string Label)[] DETAIL_STAGES =
    {
        (WorldGenStage.MapWeights, "Веса биомов"),
        (WorldGenStage.MapHeightNoise, "Шум рельефа"),
        (WorldGenStage.MapHeightHydraulic, "Водная эрозия"),
        (WorldGenStage.MapHeightThermal, "Осыпание склонов"),
        (WorldGenStage.MapRoadPlan, "Прокладка трасс"),
        (WorldGenStage.MapCarveTrunk, "Врезка трасс"),
        (WorldGenStage.MapCities, "Планировка кварталов"),
        (WorldGenStage.MapSettlementPads, "Выравнивание поселений"),
        (WorldGenStage.MapCarveStreets, "Врезка улиц"),
        (WorldGenStage.MapLots, "Нарезка участков"),
        (WorldGenStage.MapPoiPlace, "Расстановка зданий"),
        (WorldGenStage.MapCarvePads, "Площадки зданий"),
        (WorldGenStage.BakeTextures, "PNG-карты"),
        (WorldGenStage.BakeHeightRaw, "Запись карты высот"),
        (WorldGenStage.BakeImport, "Импорт карты высот"),
        (WorldGenStage.BakeLayers, "Слои Repetitionless"),
        (WorldGenStage.BakeSplat, "Расчёт текстур поверхности"),
        (WorldGenStage.BakePersist, "Запись текстур поверхности"),
        (WorldGenStage.BakeSaveAssets, "Сохранение ассетов"),
        (WorldGenStage.PreviewGeometry, "Геометрия и коллизия"),
        (WorldGenStage.PreviewDecor, "Растительность")
    };

    private static readonly List<StageTime> TIMINGS = new();
    private static readonly List<StageTime> DETAILS = new();
    private static readonly Stopwatch CLOCK = new();

    private static string _stage;
    private static double _stageStart;

    public static bool Busy { get; private set; }
    public static float Progress { get; private set; }
    public static string Status { get; private set; } = "Готов к генерации";
    public static IReadOnlyList<StageTime> Timings => TIMINGS;
    public static IReadOnlyList<StageTime> Details => DETAILS;
    public static double TotalSeconds { get; private set; }
    public static bool LastRunGenerated { get; private set; }

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

        TIMINGS.Clear();
        DETAILS.Clear();
        WorldGenProbe.BeginRun(false);
        TotalSeconds = 0d;
        LastRunGenerated = generate;
        _stage = null;
        CLOCK.Restart();
        Mark("Подготовка");

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

            Mark("Замена предпросмотра в сцене");
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
            string elapsed = FormatTime(CLOCK.Elapsed.TotalSeconds);
            Status = generate ? $"Мир сохранён за {elapsed}. Зданий POI: {world.Placement.Placements.Count}" : $"Предпросмотр обновлён за {elapsed}";
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
            CloseStage();
            TotalSeconds = CLOCK.Elapsed.TotalSeconds;
            CLOCK.Stop();
            CollectDetails();

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

    public static string FormatTime(double seconds)
    {
        if (seconds < 60d)
            return $"{seconds:0.0} с";

        int minutes = (int)(seconds / 60d);

        return $"{minutes} мин {seconds - minutes * 60d:00.0} с";
    }

    private static void CollectDetails()
    {
        foreach ((WorldGenStage stage, string label) in DETAIL_STAGES)
        {
            WorldGenStageStat stat = WorldGenProbe.Stat(stage);

            if (stat.Count > 0)
                DETAILS.Add(new StageTime(label, WorldGenProbe.ToMs(stat.Ticks) / 1000d));
        }
    }

    private static void Mark(string stage)
    {
        if (stage == _stage)
            return;

        CloseStage();
        _stage = stage;
        _stageStart = CLOCK.Elapsed.TotalSeconds;
    }

    private static void CloseStage()
    {
        if (_stage == null)
            return;

        double seconds = CLOCK.Elapsed.TotalSeconds - _stageStart;
        string stage = _stage;
        int index = TIMINGS.FindIndex(timing => timing.Stage == stage);

        if (index < 0)
            TIMINGS.Add(new StageTime(stage, seconds));
        else
            TIMINGS[index] = new StageTime(stage, TIMINGS[index].Seconds + seconds);

        _stage = null;
    }

    private static void Report(string stage, float progress)
    {
        Mark(stage);
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
