using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DecorEditTool
{
    private const string MENU = "Мир/Правка деревьев и камней";
    private const string EDITS_PATH = "Assets/_Dustborn/Content/Configs/World/DecorEdits.asset";
    private const string EDITS_PROPERTY = "<DecorEdits>k__BackingField";

    private const float PICK_RADIUS = 6f;
    private const float RAY_REACH = 4000f;
    private const float RAY_STEP = 1f;
    private const int RAY_REFINE = 12;
    private const float AREA_PAD = 2f;
    private const float MARKER_REACH = 600f;
    private const float MIN_SCALE = 0.1f;

    private static readonly List<DecorEdit> Snapshot = new();
    private static readonly List<Vector2> Touched = new();

    private static DecorEditSession _session;
    private static VoxelTerrainBuilder _builder;
    private static bool _active;

    private static bool _picked;
    private static DecorPick _pick;
    private static DecorInstance _pose;
    private static Vector2 _origin;
    private static Vector2 _shown;
    private static bool _dragging;
    private static string _status = string.Empty;

    static DecorEditTool()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.playModeStateChanged += _ => Stop();
    }

    [MenuItem(MENU)]
    private static void Toggle()
    {
        if (_active)
            Stop();
        else
            Start();
    }

    [MenuItem(MENU, true)]
    private static bool CanToggle()
    {
        Menu.SetChecked(MENU, _active);

        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static void Start()
    {
        _builder = Object.FindFirstObjectByType<VoxelTerrainBuilder>();

        if (_builder == null)
        {
            Notify("Нет предпросмотра мира: сначала обновите предпросмотр");
            return;
        }

        string problem;

        try
        {
            EditorUtility.DisplayProgressBar("Правка деревьев и камней", "Загрузка карты высот и расстановки декора", 0.5f);
            _session = _builder.BeginDecorEdit(out problem);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (_session == null)
        {
            Debug.LogWarning($"Decor editing did not start: {problem}");
            Notify("Правка декора не запустилась, причина в консоли");
            return;
        }

        _active = true;
        _picked = false;
        _status = "Щёлкните по дереву или камню";

        TakeSnapshot(Edits());

        SceneView.duringSceneGui += OnSceneGui;
        Undo.undoRedoPerformed += OnUndo;
        SceneView.RepaintAll();
    }

    private static void Stop()
    {
        if (!_active)
            return;

        SceneView.duringSceneGui -= OnSceneGui;
        Undo.undoRedoPerformed -= OnUndo;

        _session?.Dispose();
        _session = null;
        _builder = null;
        _active = false;
        _picked = false;

        SceneView.RepaintAll();
    }

    private static void OnSceneGui(SceneView view)
    {
        if (_builder == null)
        {
            Stop();
            Notify("Предпросмотр перестроен: включите правку декора заново");
            return;
        }

        Event current = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);

        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(control);

        DecorEditsAsset edits = Edits();

        DrawMarkers(edits, view);

        if (_picked)
            DrawSelection(edits);

        Rect panel = DrawPanel(edits);

        HandleCommands(current, edits);

        if (current.type != EventType.MouseDown || current.button != 0 || current.alt || panel.Contains(current.mousePosition))
            return;

        if (HandleUtility.nearestControl != control)
            return;

        Pick(HandleUtility.GUIPointToWorldRay(current.mousePosition), edits);
        current.Use();
    }

    private static void Pick(Ray ray, DecorEditsAsset edits)
    {
        _picked = false;

        if (!Raycast(ray, out Vector3 hit) || !_session.TryPick(new Vector2(hit.x, hit.z), PICK_RADIUS, out _pick))
        {
            _status = "Рядом с этой точкой нет деревьев и камней";
            return;
        }

        _picked = true;
        _pose = _pick.Instance;
        _shown = _pick.Point;
        _origin = edits != null && edits.TryGet(_pick.Key, out DecorEdit edit) ? edit.Origin : _pick.Point;
        _status = string.Empty;

        Selection.activeObject = null;
    }

    private static void DrawSelection(DecorEditsAsset edits)
    {
        VoxelDecorLayer layer = _session.Placer.Layers[_pick.Layer];
        Vector3 position = _pose.Position;
        Quaternion turn = Quaternion.Euler(0f, _pose.Rotation, 0f);
        float radius = Mathf.Max(1f, layer.Footprint);

        Handles.color = Color.yellow;
        Handles.DrawWireDisc(position, Vector3.up, radius);
        Handles.Label(position + Vector3.up * (radius + 1f), layer.Name);

        if (!DistanceUtility.WithinRadius(_origin, Flat(position), 0.01f))
        {
            Handles.color = Color.cyan;
            Handles.DrawDottedLine(Ground(_origin), position, 4f);
        }

        if (edits == null || !_session.Accepts(edits))
            return;

        EditorGUI.BeginChangeCheck();

        Vector3 moved = Handles.PositionHandle(position, turn);
        Quaternion turned = Handles.Disc(turn, position, Vector3.up, radius + 0.5f, false, 0f);
        float scale = Handles.ScaleSlider(_pose.Scale, position, Vector3.up, turn, HandleUtility.GetHandleSize(position) * 1.5f, 0f);

        if (EditorGUI.EndChangeCheck())
        {
            float resized = Mathf.Max(MIN_SCALE, scale);

            _pose.Height *= resized / Mathf.Max(MIN_SCALE, _pose.Scale);
            _pose.Scale = resized;
            _pose.Rotation = turned.eulerAngles.y;
            _pose.Position = Ground(Flat(moved));
            _dragging = true;
        }

        if (_dragging && GUIUtility.hotControl == 0)
            Commit(edits);
    }

    private static void Commit(DecorEditsAsset edits)
    {
        _dragging = false;

        Undo.RecordObject(edits, "Правка дерева или камня");
        edits.Set(_session.Seed, DecorEdit.Moved(_pick.Key, PrefabOf(_pick), _origin, _pose));
        EditorUtility.SetDirty(edits);

        Vector2 placed = Flat(_pose.Position);

        Apply(edits, _origin, _shown, placed);

        _shown = placed;
        _status = "Сдвинуто";
    }

    private static void Remove(DecorEditsAsset edits)
    {
        Undo.RecordObject(edits, "Удаление дерева или камня");
        edits.Set(_session.Seed, DecorEdit.Removal(_pick.Key, PrefabOf(_pick), _origin));
        EditorUtility.SetDirty(edits);

        Apply(edits, _origin, _shown);

        _picked = false;
        _status = "Удалено";
    }

    private static void Restore(DecorEditsAsset edits)
    {
        Undo.RecordObject(edits, "Возврат дерева или камня");
        edits.Forget(_pick.Key);
        EditorUtility.SetDirty(edits);

        Apply(edits, _origin, _shown);

        _picked = _session.TryFind(_pick.Key, _origin, PICK_RADIUS, out _pick);
        _pose = _pick.Instance;
        _shown = _origin;
        _status = _picked ? "Возвращено на место генерации" : "Генератор больше не ставит сюда этот объект";
    }

    private static void Apply(DecorEditsAsset edits, params Vector2[] points)
    {
        _session.Reindex(edits);

        var pad = new Vector2(AREA_PAD, AREA_PAD);

        foreach (Vector2 point in points)
        {
            if (_builder.RefreshDecor(_session, point - pad, point + pad) >= 0)
                continue;

            _status = "Набор декора изменился после предпросмотра: обновите предпросмотр";
            break;
        }

        TakeSnapshot(edits);
        SceneView.RepaintAll();
    }

    private static void OnUndo()
    {
        DecorEditsAsset edits = Edits();

        if (edits == null)
            return;

        Touched.Clear();

        foreach (DecorEdit before in Snapshot)
        {
            if (!edits.TryGet(before.Key, out DecorEdit after) || !Same(before, after))
                Touch(before);
        }

        foreach (DecorEdit after in edits.Edits)
        {
            if (!Find(after.Key, out DecorEdit before) || !Same(before, after))
                Touch(after);
        }

        if (Touched.Count == 0)
            return;

        _picked = false;
        _dragging = false;
        _status = "Отмена применена";

        Apply(edits, Touched.ToArray());
    }

    private static void Touch(DecorEdit edit)
    {
        Touched.Add(edit.Origin);
        Touched.Add(edit.Position);
    }

    private static bool Find(DecorKey key, out DecorEdit edit)
    {
        foreach (DecorEdit candidate in Snapshot)
        {
            if (!candidate.Key.Equals(key))
                continue;

            edit = candidate;
            return true;
        }

        edit = default;
        return false;
    }

    private static bool Same(DecorEdit first, DecorEdit second)
    {
        return first.Removed == second.Removed && first.Position == second.Position && first.Rotation == second.Rotation
            && first.Scale == second.Scale && first.Height == second.Height;
    }

    private static void TakeSnapshot(DecorEditsAsset edits)
    {
        Snapshot.Clear();

        if (edits != null)
            Snapshot.AddRange(edits.Edits);
    }

    private static void HandleCommands(Event current, DecorEditsAsset edits)
    {
        bool delete = current.commandName == "SoftDelete" || current.commandName == "Delete";

        if (_picked && delete && current.type == EventType.ValidateCommand)
        {
            current.Use();
            return;
        }

        if (_picked && delete && current.type == EventType.ExecuteCommand && edits != null && _session.Accepts(edits))
        {
            Remove(edits);
            current.Use();
            return;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && _picked)
        {
            _picked = false;
            current.Use();
        }
    }

    private static Rect DrawPanel(DecorEditsAsset edits)
    {
        var panel = new Rect(10f, 10f, 330f, 150f);

        Handles.BeginGUI();
        GUILayout.BeginArea(panel, EditorStyles.helpBox);
        GUILayout.Label("Правка деревьев и камней", EditorStyles.boldLabel);

        if (edits == null)
            Missing();
        else if (!_session.Accepts(edits))
            Foreign(edits);
        else
            Controls(edits);

        GUILayout.EndArea();
        Handles.EndGUI();

        return panel;
    }

    private static void Missing()
    {
        GUILayout.Label("Нет файла правок: правки негде хранить.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Создать файл правок"))
            CreateEdits();

        if (GUILayout.Button("Закрыть"))
            Stop();
    }

    private static void Foreign(DecorEditsAsset edits)
    {
        GUILayout.Label($"Правки сделаны для мира с сидом {edits.Seed}, а этот мир — сид {_session.Seed}.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Очистить правки"))
        {
            Undo.RecordObject(edits, "Очистка правок декора");
            edits.Clear();
            EditorUtility.SetDirty(edits);
            Apply(edits);
        }

        if (GUILayout.Button("Закрыть"))
            Stop();
    }

    private static void Controls(DecorEditsAsset edits)
    {
        GUILayout.Label(_picked ? $"Выбрано: {_session.Placer.Layers[_pick.Layer].Name}" : "Щёлкните по дереву или камню", EditorStyles.wordWrappedLabel);

        using (new EditorGUI.DisabledScope(!_picked))
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Удалить"))
                Remove(edits);

            if (GUILayout.Button("Вернуть как было"))
                Restore(edits);
        }

        GUILayout.Label($"Правок: сдвинуто {edits.Moved}, удалено {edits.Removed}. {_status}", EditorStyles.wordWrappedMiniLabel);

        if (GUILayout.Button("Закрыть"))
            Stop();
    }

    private static void DrawMarkers(DecorEditsAsset edits, SceneView view)
    {
        if (edits == null || view.camera == null)
            return;

        Vector3 eye = view.camera.transform.position;

        foreach (DecorEdit edit in edits.Edits)
        {
            Vector3 origin = Ground(edit.Origin);

            if (!DistanceUtility.WithinRadius(eye, origin, MARKER_REACH))
                continue;

            if (edit.Removed)
            {
                Handles.color = Color.red;
                Handles.DrawLine(origin + new Vector3(-1f, 0.2f, -1f), origin + new Vector3(1f, 0.2f, 1f));
                Handles.DrawLine(origin + new Vector3(-1f, 0.2f, 1f), origin + new Vector3(1f, 0.2f, -1f));
                continue;
            }

            Handles.color = Color.cyan;
            Handles.DrawWireDisc(origin, Vector3.up, 0.5f);
            Handles.DrawDottedLine(origin, Ground(edit.Position), 4f);
        }
    }

    private static void CreateEdits()
    {
        var edits = AssetDatabase.LoadAssetAtPath<DecorEditsAsset>(EDITS_PATH);

        if (edits == null)
        {
            edits = ScriptableObject.CreateInstance<DecorEditsAsset>();
            AssetDatabase.CreateAsset(edits, EDITS_PATH);
        }

        WorldBuildSettings settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(WorldGenerationWindow.DEFAULT_SETTINGS_PATH);

        if (settings != null)
        {
            var serialized = new SerializedObject(settings);

            serialized.FindProperty(EDITS_PROPERTY).objectReferenceValue = edits;
            serialized.ApplyModifiedProperties();
        }

        AssetDatabase.SaveAssets();

        _builder.UseDecorEdits(edits);
        _session.Reindex(edits);

        TakeSnapshot(edits);
    }

    private static DecorEditsAsset Edits()
    {
        if (_builder.DecorEdits != null)
            return _builder.DecorEdits;

        WorldBuildSettings settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(WorldGenerationWindow.DEFAULT_SETTINGS_PATH);

        if (settings == null || settings.DecorEdits == null)
            return null;

        _builder.UseDecorEdits(settings.DecorEdits);
        _session.Reindex(settings.DecorEdits);

        return settings.DecorEdits;
    }

    private static bool Raycast(Ray ray, out Vector3 hit)
    {
        float above = 0f;

        for (float reach = 0f; reach <= RAY_REACH; reach += RAY_STEP)
        {
            if (Above(ray.GetPoint(reach)))
            {
                above = reach;
                continue;
            }

            float below = reach;

            for (int i = 0; i < RAY_REFINE; i++)
            {
                float middle = 0.5f * (above + below);

                if (Above(ray.GetPoint(middle)))
                    above = middle;
                else
                    below = middle;
            }

            hit = ray.GetPoint(below);
            return true;
        }

        hit = default;
        return false;
    }

    private static bool Above(Vector3 point)
    {
        return point.y > _session.Surface(Flat(point));
    }

    private static Vector3 Ground(Vector2 point)
    {
        return new Vector3(point.x, _session.Surface(point), point.y);
    }

    private static Vector2 Flat(Vector3 point)
    {
        return new Vector2(point.x, point.z);
    }

    private static GameObject PrefabOf(DecorPick pick)
    {
        return _session.Placer.Layers[pick.Layer].Prefab;
    }

    private static void Notify(string message)
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view != null)
            view.ShowNotification(new GUIContent(message));
    }
}
