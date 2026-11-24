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

    private static readonly (WorldGenStage Stage, string LabelKey)[] DETAIL_STAGES =
    {
        (WorldGenStage.MapWeights, "editor.worldRun.timing.mapWeights"),
        (WorldGenStage.MapHeightNoise, "editor.worldRun.timing.mapHeightNoise"),
        (WorldGenStage.MapHeightHydraulic, "editor.worldRun.timing.mapHeightHydraulic"),
        (WorldGenStage.MapHeightThermal, "editor.worldRun.timing.mapHeightThermal"),
        (WorldGenStage.MapRoadPlan, "editor.worldRun.timing.mapRoadPlan"),
        (WorldGenStage.MapCarveTrunk, "editor.worldRun.timing.mapCarveTrunk"),
        (WorldGenStage.MapCities, "editor.worldRun.timing.mapCities"),
        (WorldGenStage.MapSettlementPads, "editor.worldRun.timing.mapSettlementPads"),
        (WorldGenStage.MapCarveStreets, "editor.worldRun.timing.mapCarveStreets"),
        (WorldGenStage.MapLots, "editor.worldRun.timing.mapLots"),
        (WorldGenStage.MapPoiPlace, "editor.worldRun.timing.mapPoiPlace"),
        (WorldGenStage.MapCarvePads, "editor.worldRun.timing.mapCarvePads"),
        (WorldGenStage.BakeTextures, "editor.worldRun.timing.bakeTextures"),
        (WorldGenStage.BakeHeightRaw, "editor.worldRun.timing.bakeHeightRaw"),
        (WorldGenStage.BakeImport, "editor.worldRun.timing.bakeImport"),
        (WorldGenStage.BakeLayers, "editor.worldRun.timing.bakeLayers"),
        (WorldGenStage.BakeSplat, "editor.worldRun.timing.bakeSplat"),
        (WorldGenStage.BakePersist, "editor.worldRun.timing.bakePersist"),
        (WorldGenStage.BakeSaveAssets, "editor.worldRun.timing.bakeSaveAssets"),
        (WorldGenStage.PreviewLoad, "editor.worldRun.timing.previewLoad"),
        (WorldGenStage.ShorePlan, "editor.worldRun.timing.shorePlan"),
        (WorldGenStage.PreviewGeometry, "editor.worldRun.timing.previewGeometry"),
        (WorldGenStage.RoadSurface, "editor.worldRun.timing.roadSurface"),
        (WorldGenStage.PreviewWater, "editor.worldRun.timing.previewWater"),
        (WorldGenStage.PreviewPoi, "editor.worldRun.poi"),
        (WorldGenStage.PreviewTransient, "editor.worldRun.timing.previewTransient"),
        (WorldGenStage.PreviewSwap, "editor.worldRun.timing.previewSwap"),
        (WorldGenStage.PreviewDecor, "editor.worldRun.timing.previewDecor")
    };

    private static readonly List<StageTime> TIMINGS = new();
    private static readonly List<StageTime> DETAILS = new();
    private static readonly Stopwatch CLOCK = new();

    private const double BAR_INTERVAL = 0.1d;

    private static string _status;
    private static string _stage;
    private static string _barStage;
    private static double _barShown;
    private static double _stageStart;

    public static bool Busy { get; private set; }
    public static float Progress { get; private set; }
    public static string Status
    {
        get => _status ?? LocalizationService.Text("editor.worldRun.idle");
        private set => _status = value;
    }
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
        Status = LocalizationService.Text("editor.worldRun.prepare");
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
        _barStage = null;
        CLOCK.Restart();
        Mark(LocalizationService.Text("editor.worldRun.prepare"));

        try
        {
            if (owner == null || settings == null || !settings.IsValid || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Assign the world controller and all source assets before generating.");

            if (!settings.Biomes.IsValid() || !settings.Pois.IsValid())
                throw new InvalidOperationException("The biome or POI database is invalid. Check the console.");

            BakedWorld world = generate ? null : SavedWorld(owner, settings);

            if (!generate && (world == null || !world.IsValid))
                throw new InvalidOperationException("Generate and save the world before rebuilding its preview.");

            WorldBuildBudget.Validate(settings, generate ? settings.Config : world.Config, settings.PreviewCenter, settings.PreviewColliders, generate ? null : world.LoadWater());
            EditorApplication.LockReloadAssemblies();
            locked = true;

            if (generate)
            {
                world = new WorldGenerationBake(settings, Report).Generate();
                saved = true;
            }

            if (world == null || !world.IsValid)
                throw new InvalidOperationException("Generate and save the world before rebuilding its preview.");

            Report(LocalizationService.Text("editor.worldRun.fullPreview"), 0.65f);
            preview = new GameObject(LocalizationService.Text("editor.worldRun.previewObject")) { tag = "EditorOnly" };
            preview.transform.SetParent(owner.transform, false);

            var terrainRoot = new GameObject(LocalizationService.Text("editor.worldRun.terrainObject"));
            terrainRoot.transform.SetParent(preview.transform, false);
            var terrain = terrainRoot.AddComponent<VoxelTerrainBuilder>();
            terrain.hideFlags = HideFlags.HideInInspector;
            terrain.Configure(settings, world);

            if (!terrain.Build((stage, fraction) => Report(stage, 0.65f + fraction * 0.28f)))
                throw new InvalidOperationException("The terrain preview was refused. Check its memory budget and source maps.");

            using (WorldGenProbe.Measure(WorldGenStage.PreviewTransient))
                MakeTransient(terrainRoot);

            var buildings = new GameObject(LocalizationService.Text("editor.worldRun.poi"));
            buildings.transform.SetParent(preview.transform, false);
            var pois = new PoiInstanceBuilder(world.Placement.Placements, buildings.transform);

            using (WorldGenProbe.Measure(WorldGenStage.PreviewPoi))
            {
                while (!pois.Ready)
                {
                    Report(LocalizationService.Text("editor.worldRun.placePoi"), 0.93f + pois.Progress * 0.04f);
                    pois.BuildNext(16);
                }
            }

            var propsRoot = new GameObject(LocalizationService.Text("loading.props"));
            propsRoot.transform.SetParent(preview.transform, false);
            float cull = settings.Props == null ? 0f : settings.Props.CullScreenHeight;
            var props = new PropInstanceBuilder(world.Placement.Props, propsRoot.transform, terrainRoot.transform, cull, settings.Voxels.ChunkMetres);

            using (WorldGenProbe.Measure(WorldGenStage.PreviewProps))
            {
                while (!props.Ready)
                {
                    Report(LocalizationService.Text("loading.props"), 0.97f + props.Progress * 0.03f);
                    props.BuildNext(256);
                }

                MakeTransient(propsRoot);
            }

            Mark(LocalizationService.Text("editor.worldRun.swapPreview"));
            using WorldGenProbe.Span swap = WorldGenProbe.Measure(WorldGenStage.PreviewSwap);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(LocalizationService.Text("editor.worldRun.updateUndo"));
            WorldGenerationMigration.Apply(owner, settings);
            ClearPreview(owner);
            Assign(owner, "_settings", settings);
            Assign(owner, "_world", world);
            Assign(owner, "_previewRoot", preview);
            Assign(owner, "_config", null);
            Assign(owner, "_biomes", null);
            Assign(owner, "_pois", null);
            terrainRoot.transform.SetParent(null, false);
            propsRoot.transform.SetParent(null, false);
            Undo.RegisterCreatedObjectUndo(preview, LocalizationService.Text("editor.worldRun.createUndo"));
            terrainRoot.transform.SetParent(preview.transform, false);
            propsRoot.transform.SetParent(preview.transform, false);
            Undo.CollapseUndoOperations(undoGroup);
            preview = null;
            EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
            SceneView.RepaintAll();
            Progress = 1f;
            string elapsed = FormatTime(CLOCK.Elapsed.TotalSeconds);
            Status = generate ? LocalizationService.Format("editor.worldRun.saved", elapsed, world.Placement.Placements.Count, world.Placement.Props.Count) : LocalizationService.Format("editor.worldRun.previewUpdated", elapsed);
        }
        catch (OperationCanceledException)
        {
            Status = saved ? LocalizationService.Text("editor.worldRun.cancelledAfterSave") : LocalizationService.Text("editor.worldRun.cancelled");
        }
        catch (Exception exception)
        {
            Status = LocalizationService.Text("editor.worldRun.failed");
            Debug.LogException(exception, owner);
        }
        finally
        {
            CloseStage();
            TotalSeconds = CLOCK.Elapsed.TotalSeconds;
            CLOCK.Stop();
            CollectDetails();
            LogTimings();

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
            return LocalizationService.Format("editor.worldRun.seconds", seconds);

        int minutes = (int)(seconds / 60d);

        return LocalizationService.Format("editor.worldRun.minutesAndSeconds", minutes, seconds - minutes * 60d);
    }

    private static void CollectDetails()
    {
        foreach ((WorldGenStage stage, string labelKey) in DETAIL_STAGES)
        {
            WorldGenStageStat stat = WorldGenProbe.Stat(stage);

            if (stat.Count > 0)
                DETAILS.Add(new StageTime(LocalizationService.Text(labelKey), WorldGenProbe.ToMs(stat.Ticks) / 1000d));
        }
    }

    private static void LogTimings()
    {
        var rows = new List<(WorldGenStage Stage, double Seconds, int Count)>();

        for (var stage = (WorldGenStage)0; stage < WorldGenStage.Count; stage++)
        {
            WorldGenStageStat stat = WorldGenProbe.Stat(stage);

            if (stat.Count > 0)
                rows.Add((stage, WorldGenProbe.ToMs(stat.Ticks) / 1000d, stat.Count));
        }

        rows.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));

        var text = new System.Text.StringBuilder($"World generation took {TotalSeconds:0.0} s. Stages:");

        foreach ((WorldGenStage stage, double seconds, int count) in rows)
        {
            if (seconds >= 0.05d)
                text.Append($"\n  {stage}: {seconds:0.0} s ({count})");
        }

        Debug.Log(text.ToString());
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

        double now = CLOCK.Elapsed.TotalSeconds;

        if (stage == _barStage && now - _barShown < BAR_INTERVAL)
            return;

        _barStage = stage;
        _barShown = now;

        if (EditorUtility.DisplayCancelableProgressBar(LocalizationService.Text("editor.worldWindow.title"), $"{stage} — {Progress:P0}", Progress))
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
