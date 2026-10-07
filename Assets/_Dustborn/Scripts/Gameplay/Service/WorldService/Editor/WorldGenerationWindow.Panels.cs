using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow
{
    private void DrawScene()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            WorldGenerator selected = EditorGUILayout.ObjectField("Мир в сцене", _owner, typeof(WorldGenerator), true) as WorldGenerator;

            if (selected != _owner)
                Select(selected);

            if (_owner != null && GUILayout.Button("Выделить", GUILayout.Width(80f)))
            {
                Selection.activeObject = _owner;
                EditorGUIUtility.PingObject(_owner);
            }
        }

        if (_owner != null || !GUILayout.Button("Добавить управление миром в сцену"))
            return;

        var holder = new GameObject("WorldGenerator");
        Undo.RegisterCreatedObjectUndo(holder, "Добавить управление миром");
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
        string blocked = Locked ? "Идёт генерация или запуск игры" : _owner == null ? "Добавьте управление миром в сцену" : !_settings.IsValid ? "Назначьте источники настроек" : null;

        using (new EditorGUI.DisabledScope(blocked != null))
        {
            Color background = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);

            if (GUILayout.Button(new GUIContent("Сгенерировать мир", "Все карты, дороги, здания, вода и предпросмотр в сцене за один запуск. Отмена после сохранения карт сохраняет их."), GUILayout.Height(PRIMARY_HEIGHT)))
                WorldGenerationEditorRun.Start(_owner, _settings, true);

            GUI.backgroundColor = background;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(blocked != null || saved == null || !saved.IsValid))
            {
                if (GUILayout.Button(new GUIContent("Обновить предпросмотр", "Пересобрать ландшафт, воду и декор в сцене из сохранённого мира, не генерируя карты заново.")))
                    WorldGenerationEditorRun.Start(_owner, _settings, false);
            }

            using (new EditorGUI.DisabledScope(Locked || _owner == null || _owner.PreviewRoot == null))
            {
                if (GUILayout.Button(new GUIContent("Убрать предпросмотр", "Удалить геометрию предпросмотра из сцены. Сохранённый мир не трогается.")))
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
        EditorGUILayout.Space(4f);
        _tools = EditorGUILayout.Foldout(_tools, "Инструменты", true, EditorStyles.foldoutHeader);

        if (!_tools)
            return;

        using (new EditorGUI.DisabledScope(Locked))
        {
            for (int i = 0; i < TOOLS.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Tool(TOOLS[i]);

                    if (i + 1 < TOOLS.Length)
                        Tool(TOOLS[i + 1]);
                }
            }
        }
    }

    private static void Tool((string Label, string Menu) tool)
    {
        if (GUILayout.Button(new GUIContent(tool.Label, tool.Menu)))
            EditorApplication.ExecuteMenuItem(tool.Menu);
    }

    private void DrawTimings()
    {
        if (WorldGenerationEditorRun.Busy || WorldGenerationEditorRun.Timings.Count == 0)
            return;

        double total = WorldGenerationEditorRun.TotalSeconds;
        string title = WorldGenerationEditorRun.LastRunGenerated ? "Время генерации" : "Время обновления предпросмотра";

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
            _details = EditorGUILayout.Foldout(_details, "Подробно", true);

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
