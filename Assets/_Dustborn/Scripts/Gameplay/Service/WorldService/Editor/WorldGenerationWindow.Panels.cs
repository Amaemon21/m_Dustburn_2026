using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow
{
    private void DrawScene()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            WorldGenerator selected = EditorGUILayout.ObjectField(LocalizationService.Text("editor.worldWindow.sceneWorld"), _owner, typeof(WorldGenerator), true) as WorldGenerator;

            if (selected != _owner)
                Select(selected);

            if (_owner != null && GUILayout.Button(LocalizationService.Text("editor.worldWindow.select"), GUILayout.Width(80f)))
            {
                Selection.activeObject = _owner;
                EditorGUIUtility.PingObject(_owner);
            }
        }

        if (_owner != null || !GUILayout.Button(LocalizationService.Text("editor.worldWindow.createOwner")))
            return;

        var holder = new GameObject("WorldGenerator");
        Undo.RegisterCreatedObjectUndo(holder, LocalizationService.Text("editor.worldWindow.createOwnerUndo"));
        Select(Undo.AddComponent<WorldGenerator>(holder));
    }

    private void DrawStatus()
    {
        BakedWorld saved = WorldGenerationEditorRun.SavedWorld(_owner, _settings);
        MessageType kind = saved != null && saved.IsValid ? MessageType.Info : MessageType.Warning;

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(Describe(saved), kind);
    }

    private void DrawActions()
    {
        BakedWorld saved = WorldGenerationEditorRun.SavedWorld(_owner, _settings);
        string blocked = Locked ? LocalizationService.Text("editor.worldWindow.busy") : _owner == null ? LocalizationService.Text("editor.worldWindow.missingOwner") : !_settings.IsValid ? LocalizationService.Text("editor.worldWindow.missingSettings") : null;

        using (new EditorGUI.DisabledScope(blocked != null))
        {
            Color background = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);

            if (GUILayout.Button(new GUIContent(LocalizationService.Text("editor.worldWindow.generate"), LocalizationService.Text("editor.worldWindow.generateHelp")), GUILayout.Height(PRIMARY_HEIGHT)))
                WorldGenerationEditorRun.Start(_owner, _settings, true);

            GUI.backgroundColor = background;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(blocked != null || saved == null || !saved.IsValid))
            {
                if (GUILayout.Button(new GUIContent(LocalizationService.Text("editor.worldWindow.refreshPreview"), LocalizationService.Text("editor.worldWindow.refreshPreviewHelp"))))
                    WorldGenerationEditorRun.Start(_owner, _settings, false);
            }

            using (new EditorGUI.DisabledScope(Locked || _owner == null || _owner.PreviewRoot == null))
            {
                if (GUILayout.Button(new GUIContent(LocalizationService.Text("editor.worldWindow.clearPreview"), LocalizationService.Text("editor.worldWindow.clearPreviewHelp"))))
                    WorldGenerationEditorRun.ClearPreview(_owner);
            }
        }

        if (blocked != null && !Locked)
            EditorGUILayout.LabelField(blocked, _hint);
    }

    private void DrawProgress()
    {
        bool runtime = Application.isPlaying && _owner != null;

        if (!runtime && !WorldGenerationEditorRun.Busy && WorldGenerationEditorRun.Timings.Count == 0)
            return;

        EditorGUILayout.Space(4f);
        Rect bar = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.ProgressBar(bar, runtime ? _owner.Progress : WorldGenerationEditorRun.Progress, runtime ? _owner.Status : WorldGenerationEditorRun.Status);
    }

    private void DrawTools()
    {
        using (new EditorGUI.DisabledScope(Locked))
        {
            if (!GUILayout.Button("World tools"))
                return;

            var menu = new GenericMenu();
            AddItemsToMenu(menu);
            menu.ShowAsContext();
        }
    }

    public void AddItemsToMenu(GenericMenu menu)
    {
        AddTool(menu, "Surface/Rebake", WorldSurfaceBake.Rebuild);
        AddTool(menu, "Stamps/Import terrain", TerrainStampImporter.Import);
        AddTool(menu, "Stamps/Import water", WaterStampImporter.Import);
        AddTool(menu, "Diagnostics/Water and stamps preview", WaterDebug.Preview);
        AddTool(menu, "Diagnostics/Ground splat", GroundSplatDebug.Dump);
        AddTool(menu, "Scene/Water and stamps", WaterDebug.ToggleGizmos, WorldFeatureGizmos.Visible);
        AddTool(menu, "Scene/LOD seams", VoxelSeamDebugMenu.Toggle, VoxelColumnDebug.Visible);
        AddTool(menu, "Scene/Edit trees and rocks", DecorEditTool.Toggle, DecorEditTool.Active);
    }

    private static void AddTool(GenericMenu menu, string label, GenericMenu.MenuFunction action, bool selected = false)
    {
        var content = new GUIContent(label);

        if (Locked)
            menu.AddDisabledItem(content, selected);
        else
            menu.AddItem(content, selected, action);
    }

    private void DrawTimings()
    {
        if (WorldGenerationEditorRun.Busy || WorldGenerationEditorRun.Timings.Count == 0)
            return;

        double total = WorldGenerationEditorRun.TotalSeconds;
        string title = WorldGenerationEditorRun.LastRunGenerated ? LocalizationService.Text("editor.worldWindow.generateTime") : LocalizationService.Text("editor.worldWindow.previewTime");

        _timings = EditorGUILayout.Foldout(_timings, $"{title}: {WorldGenerationEditorRun.FormatTime(total)}", true, EditorStyles.foldoutHeader);

        if (!_timings)
            return;

        _timingsScroll = EditorGUILayout.BeginScrollView(_timingsScroll, GUILayout.MaxHeight(TIMINGS_HEIGHT));

        foreach (WorldGenerationEditorRun.StageTime timing in WorldGenerationEditorRun.Timings)
        {
            if (timing.Seconds < MIN_SHOWN_SECONDS)
                continue;

            double share = total > 0d ? timing.Seconds / total : 0d;
            EditorGUILayout.LabelField(timing.Stage, $"{WorldGenerationEditorRun.FormatTime(timing.Seconds)}   {share:P0}");
        }

        if (WorldGenerationEditorRun.Details.Count > 0)
        {
            EditorGUI.indentLevel++;
            _details = EditorGUILayout.Foldout(_details, LocalizationService.Text("editor.worldWindow.detailedTiming"), true);

            if (_details)
            {
                foreach (WorldGenerationEditorRun.StageTime detail in WorldGenerationEditorRun.Details)
                {
                    if (detail.Seconds >= MIN_SHOWN_SECONDS)
                        EditorGUILayout.LabelField(detail.Stage, WorldGenerationEditorRun.FormatTime(detail.Seconds));
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();
    }
}
