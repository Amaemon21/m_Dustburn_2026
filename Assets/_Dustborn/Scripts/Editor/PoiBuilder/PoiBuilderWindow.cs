using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static PoiBuilderText;

public sealed class PoiBuilderWindow : EditorWindow
{
    private const string MENU = "World/POI Builder";
    private static readonly Color _amber = new(0.94f, 0.68f, 0.34f);
    private static readonly Color _cyan = new(0.43f, 0.79f, 0.84f);
    private static readonly Color _paper = new(0.88f, 0.91f, 0.91f);
    private static readonly Color _ink = new(0.10f, 0.14f, 0.16f);

    [SerializeField] private PoiBuildPreset _selectedPreset;
    [SerializeField] private PoiPresetSettings _draft;
    [SerializeField] private Transform _anchor;
    [SerializeField] private string _assetName = PoiBuilderAssets.EXAMPLE_NAME;
    [SerializeField] private bool _showModels;
    [SerializeField] private bool _studyExample;
    private PoiBuildPreset[] _presets;
    private PoiBuildPreparation _preparation;
    private Vector2 _scroll;
    private int _previewFloor;
    private string _status = string.Empty;
    private MessageType _statusType;

    [MenuItem(MENU)]
    private static void Open() => GetWindow<PoiBuilderWindow>().Show();

    private void OnEnable()
    {
        titleContent = new GUIContent(Get("title"));
        minSize = new Vector2(590f, 520f);
        _draft ??= PoiBuilderAssets.CreateExample();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

    private void OnUndoRedo()
    {
        _status = string.Empty;
        _preparation = default;
        Repaint();
    }

    private void OnProjectChange()
    {
        _presets = default;
        _preparation = default;
        Repaint();
    }

    private void OnSelectionChange() => Repaint();
    private void OnHierarchyChange() => Repaint();

    private void OnGUI()
    {
        string title = Get("title");

        if (titleContent.text != title)
            titleContent = new GUIContent(title);

        _presets ??= PoiBuilderAssets.FindPresets();
        DrawHeader();
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 123f;

        try
        {
            DrawContent();
            DrawFooter();
        }
        finally
        {
            EditorGUIUtility.labelWidth = labelWidth;
        }
    }

    private static void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 63f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, _ink);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), _amber);
        var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, normal = { textColor = _paper } };
        var caption = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = _cyan } };
        GUI.Label(new Rect(rect.x + 16f, rect.y + 7f, rect.width - 32f, 30f), Get("title"), title);
        GUI.Label(new Rect(rect.x + 17f, rect.y + 37f, rect.width - 34f, 18f), Get("subtitle"), caption);
    }

    private void DrawContent()
    {
        using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
        _scroll = scroll.scrollPosition;
        GUILayout.Space(10f);

        using var row = new EditorGUILayout.HorizontalScope();
        GUILayout.Space(10f);

        using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(position.width * 0.52f, 296f, 430f))))
        {
            DrawPreset();
            DrawAnchor();
            DrawSettings();
        }

        GUILayout.Space(8f);

        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
        {
            _preparation ??= PoiBuildPreparation.Create(_draft);
            DrawPreview(_preparation);
            DrawValidation(_preparation);
        }

        GUILayout.Space(10f);
    }

    private void DrawPreset()
    {
        using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        DrawSection("preset");
        var labels = new string[_presets.Length + 2];
        labels[0] = Get("examplePreset");
        labels[1] = Get("studyPreset");
        int selected = _studyExample ? 1 : 0;

        for (int i = 0; i < _presets.Length; i++)
        {
            labels[i + 2] = _presets[i].name;

            if (_presets[i] == _selectedPreset)
                selected = i + 2;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            int next = EditorGUILayout.Popup(Get("activePreset"), selected, labels);

            if (next != selected)
            {
                if (next < 2)
                    ResetDraft(next == 1);
                else
                    LoadPreset(_presets[next - 2]);
            }

            if (GUILayout.Button(Get("newPreset"), GUILayout.Width(62f)))
                ResetDraft();
        }

        _assetName = EditorGUILayout.TextField(Get("assetName"), _assetName);
    }

    private void LoadPreset(PoiBuildPreset preset)
    {
        _selectedPreset = preset;
        _studyExample = false;
        _draft = preset.CopySettings();
        _assetName = preset.name;
        _preparation = default;
        _status = string.Empty;
    }

    private void ResetDraft(bool study = false)
    {
        _selectedPreset = default;
        _studyExample = study;
        _draft = study ? PoiBuilderAssets.CreateStudy() : PoiBuilderAssets.CreateExample();
        _assetName = study ? PoiBuilderAssets.STUDY_NAME : PoiBuilderAssets.EXAMPLE_NAME;
        _preparation = default;
        _status = string.Empty;
    }

    private void DrawAnchor()
    {
        using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        DrawSection("placement");
        using (new EditorGUILayout.HorizontalScope())
        {
            _anchor = EditorGUILayout.ObjectField(Get("anchor"), _anchor, typeof(Transform), true) as Transform;
            Transform selection = Selection.activeTransform;

            using var disabled = new EditorGUI.DisabledScope(PoiSceneBuilder.GetAnchorError(selection).Length > 0);

            if (GUILayout.Button(Get("useSelection"), GUILayout.Width(85f)))
                _anchor = selection;
        }

        GUILayout.Label(Get("anchorHint"), EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawSettings()
    {
        using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        DrawSection("building");
        var typeOptions = new[] { Get("house") };
        var sides = new[] { Get("sideFront"), Get("sideRight"), Get("sideBack"), Get("sideLeft") };
        var house = _draft.House;
        EditorGUI.BeginChangeCheck();
        _draft.Kind = (PoiBuildingKind)EditorGUILayout.Popup(Get("type"), (int)_draft.Kind, typeOptions);
        house.Width = EditorGUILayout.IntField(Get("width"), house.Width);
        house.Depth = EditorGUILayout.IntField(Get("depth"), house.Depth);
        house.SecondFloor = EditorGUILayout.Toggle(new GUIContent(Get("secondFloor"), Get("secondFloorHint")), house.SecondFloor);
        house.Entry = (PoiSide)EditorGUILayout.Popup(Get("entry"), (int)house.Entry, sides);
        house.Windows = EditorGUILayout.Toggle(Get("windows"), house.Windows);
        house.Pitched = EditorGUILayout.Toggle(Get("pitched"), house.Pitched);
        house.ModularWalls = EditorGUILayout.Toggle(new GUIContent(Get("modularWalls"), Get("modularWallsHelp")), house.ModularWalls);

        if (house.Pitched && PoiHouseLayout.FitsPitchedRoof(house))
            house.RoofOverhang = EditorGUILayout.Toggle(new GUIContent(Get("roofOverhang"), Get("roofOverhangHint")), house.RoofOverhang);

        if (house.ModularWalls)
            house.Wainscot = EditorGUILayout.Toggle(new GUIContent(Get("wainscot"), Get("wainscotHelp")), house.Wainscot);

        if (house.Windows)
        {
            house.WindowFrames = EditorGUILayout.Toggle(Get("windowFrames"), house.WindowFrames);

            if (house.WindowFrames)
                house.WindowGlass = EditorGUILayout.Toggle(Get("windowGlass"), house.WindowGlass);
        }

        if (!house.SecondFloor)
            house.StudyFurniture = EditorGUILayout.Toggle(Get("studyFurniture"), house.StudyFurniture);

        var foundation = new GUIContent(Get("foundation"), Get("foundationHint"));
        house.Foundation = EditorGUILayout.Toggle(foundation, house.Foundation);
        house.Porch = EditorGUILayout.Toggle(new GUIContent(Get("porch"), Get("porchHint")), house.Porch);

        if (house.Porch)
            house.PorchRails = EditorGUILayout.Toggle(Get("porchRails"), house.PorchRails);

        _draft.House = house;

        DrawSection("materials");
        string wallLabel = house.ModularWalls ? Get("facadeWalls") : Section(PoiSection.Walls);
        _draft.WallMaterial = AssetField(wallLabel, _draft.WallMaterial);
        _draft.FloorMaterial = AssetField(Section(PoiSection.Floor), _draft.FloorMaterial);
        _draft.RoofMaterial = AssetField(Section(PoiSection.Roof), _draft.RoofMaterial);

        _draft.InteriorWallMaterial = FinishField(PoiSection.WallInteriors, _draft.InteriorWallMaterial);
        _draft.TrimMaterial = FinishField(PoiSection.Trim, _draft.TrimMaterial);

        if (house.ModularWalls && house.Wainscot)
            _draft.WainscotMaterial = FinishField(PoiSection.Wainscot, _draft.WainscotMaterial);

        if (house.Pitched && PoiHouseLayout.FitsPitchedRoof(house))
        {
            _draft.GableMaterial = AssetField(Section(PoiSection.Gables), _draft.GableMaterial);
            _draft.CeilingMaterial = FinishField(PoiSection.Ceiling, _draft.CeilingMaterial);
        }

        _draft.FoundationMaterial = FinishField(PoiSection.Foundation, _draft.FoundationMaterial);

        if (house.Porch)
        {
            _draft.PorchMaterial = FinishField(PoiSection.Porch, _draft.PorchMaterial);
            _draft.PorchTimberMaterial = FinishField(PoiSection.PorchTimber, _draft.PorchTimberMaterial);
        }

        if (house.Windows && house.WindowFrames && house.WindowGlass)
            _draft.GlazingMaterial = FinishField(PoiSection.Glazing, _draft.GlazingMaterial);

        _showModels = EditorGUILayout.Foldout(_showModels, Get("models"), true);

        if (_showModels)
            DrawModels();

        if (EditorGUI.EndChangeCheck())
            _preparation = default;
    }

    private void DrawModels()
    {
        if (!_draft.House.ModularWalls)
        {
            _draft.Wall = AssetField(Part(PoiPart.Wall), _draft.Wall);
            _draft.Doorway = AssetField(Part(PoiPart.Doorway), _draft.Doorway);
            _draft.Window = AssetField(Part(PoiPart.Window), _draft.Window);
        }

        _draft.Slab = AssetField(Part(PoiPart.Slab), _draft.Slab);
        _draft.Slope = AssetField(Part(PoiPart.Slope), _draft.Slope);
        _draft.Gable = AssetField(Part(PoiPart.Gable), _draft.Gable);
        _draft.Cube = AssetField(Part(PoiPart.Cube), _draft.Cube);
    }

    private static T AssetField<T>(string label, T value) where T : Object =>
        EditorGUILayout.ObjectField(label, value, typeof(T), false) as T;

    private static Material FinishField(PoiSection section, Material material) =>
        AssetField(Section(section), material != null ? material : PoiBuilderAssets.LoadFinishMaterial(section));

    private static void DrawSection(string key)
    {
        GUILayout.Space(4f);
        GUILayout.Label(Get(key), EditorStyles.boldLabel);
        GUILayout.Space(2f);
    }

    private void DrawPreview(PoiBuildPreparation preparation)
    {
        using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        DrawSection("plan");
        PoiBuildPlan plan = preparation.Plan;
        bool hasUpperFloor = plan != null && plan.Stairwell.HasOpening;

        if (hasUpperFloor)
            _previewFloor = GUILayout.Toolbar(_previewFloor, new[] { Get("firstFloorPlan"), Get("secondFloorPlan") });

        bool upperFloor = hasUpperFloor && _previewFloor == 1;
        Rect rect = GUILayoutUtility.GetRect(0f, 222f, GUILayout.ExpandWidth(true));

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, _ink);

            if (plan != null)
                DrawBlueprint(rect, plan, upperFloor);
        }

        if (plan == null)
        {
            var centered = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = _paper } };
            GUI.Label(rect, Get("noPlan"), centered);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawLegend("entryLegend", _amber);
            DrawLegend("windowLegend", _cyan);

            if (hasUpperFloor)
                DrawLegend("stairLegend", _amber * 0.8f);
        }

        GUILayout.Label(Format("planDetails", plan.Width, plan.Depth, plan.Height, plan.Windows), EditorStyles.wordWrappedMiniLabel);
        GUILayout.Label(Format("roofDetails", Get(plan.Pitched ? "gableRoof" : "flatRoof"), plan.Parts.Count),
            EditorStyles.wordWrappedMiniLabel);
    }

    private static void DrawLegend(string key, Color color)
    {
        using var row = new EditorGUILayout.HorizontalScope(GUILayout.ExpandWidth(false));
        Rect swatch = GUILayoutUtility.GetRect(9f, 9f, GUILayout.ExpandWidth(false));
        swatch.y += 4f;
        EditorGUI.DrawRect(swatch, color);
        GUILayout.Label(Get(key), EditorStyles.miniLabel);
    }

    private static void DrawBlueprint(Rect rect, PoiBuildPlan plan, bool upperFloor)
    {
        var plot = new Rect(-plan.Width * 0.5f, -plan.Depth * 0.5f, plan.Width, plan.Depth);

        foreach (PoiPlacement part in plan.Parts)
        {
            if (TryGetPorchFootprint(part, out Rect porch))
                plot = Rect.MinMaxRect(Mathf.Min(plot.xMin, porch.xMin), Mathf.Min(plot.yMin, porch.yMin),
                    Mathf.Max(plot.xMax, porch.xMax), Mathf.Max(plot.yMax, porch.yMax));
        }

        float unit = Mathf.Min((rect.width - 34f) / plot.width, (rect.height - 34f) / plot.height);
        Vector2 origin = rect.center - plot.center * unit;
        var footprint = new Rect(origin.x - plan.Width * unit / 2f, origin.y - plan.Depth * unit / 2f,
            plan.Width * unit, plan.Depth * unit);
        var grid = new Color(0.26f, 0.37f, 0.39f);
        EditorGUI.DrawRect(footprint, new Color(0.15f, 0.21f, 0.23f));

        if (!upperFloor)
        {
            foreach (PoiPlacement part in plan.Parts)
            {
                if (TryGetPorchFootprint(part, out Rect porch))
                    EditorGUI.DrawRect(new Rect(origin + porch.position * unit, porch.size * unit), _amber * 0.65f);
            }
        }

        for (int x = 0; x <= plan.Width; x++)
            EditorGUI.DrawRect(new Rect(footprint.x + unit * x, footprint.y, 1f, footprint.height), grid);

        for (int z = 0; z <= plan.Depth; z++)
            EditorGUI.DrawRect(new Rect(footprint.x, footprint.y + unit * z, footprint.width, 1f), grid);

        if (plan.Pitched && (!plan.Stairwell.HasOpening || upperFloor))
            EditorGUI.DrawRect(new Rect(origin.x, footprint.y, 1f, footprint.height), _cyan * 0.65f);

        if (plan.Stairwell.HasOpening)
            DrawStairwell(origin, unit, plan.Stairwell, upperFloor);

        var columns = upperFloor ? plan.UpperWallColumns : plan.WallColumns;

        foreach (PoiPlacement part in columns)
            DrawWallColumn(origin, unit, part);

        float floor = columns[0].Y;

        foreach (PoiPlacement part in plan.Parts)
        {
            if (part.Section == PoiSection.Partitions && part.Y == floor)
                DrawWallColumn(origin, unit, part);
        }
    }

    private static void DrawWallColumn(Vector2 origin, float unit, PoiPlacement part)
    {
        var point = origin + new Vector2(part.X, -part.Z) * unit;
        float thickness = Mathf.Max(3f, unit * PoiHouseLayout.WALL_THICKNESS);
        bool horizontal = part.Yaw % 180 == 0;
        float width = horizontal ? unit : thickness;
        float height = horizontal ? thickness : unit;
        Color color = part.Part switch
        {
            PoiPart.Doorway or PoiPart.OpeningJamb => _amber,
            PoiPart.Window => _cyan,
            _ => _paper
        };
        EditorGUI.DrawRect(new Rect(point.x - width / 2f, point.y - height / 2f, width, height), color);
    }

    private static void DrawStairwell(Vector2 origin, float unit, PoiStairwell stairwell, bool upperFloor)
    {
        var center = origin + new Vector2(stairwell.X, -stairwell.Z) * unit;
        var size = new Vector2(PoiStairwellLayout.WIDTH, PoiStairwellLayout.LENGTH) * unit;
        var opening = new Rect(center - size * 0.5f, size);
        EditorGUI.DrawRect(opening, _ink);

        for (int step = 0; step <= PoiStairwellLayout.TREADS; step++)
        {
            float y = opening.y + step * opening.height / PoiStairwellLayout.TREADS;
            bool sectionEdge = step % PoiStairwellLayout.TREADS_PER_SECTION == 0;
            EditorGUI.DrawRect(new Rect(opening.x, y, opening.width, sectionEdge ? 2f : 1f), _amber * 0.8f);
        }

        if (upperFloor)
            EditorGUI.DrawRect(new Rect(opening.x, opening.y, opening.width, 2f), _amber);
    }

    private static bool TryGetPorchFootprint(PoiPlacement part, out Rect footprint)
    {
        float depth = part.Part switch
        {
            PoiPart.PorchDeck => PoiPorchLayout.DEPTH,
            PoiPart.PorchStairSection => PoiPorchLayout.STAIR_SECTION_DEPTH,
            PoiPart.PorchStairEndSection => PoiPorchLayout.STAIR_END_DEPTH,
            PoiPart.PorchLowStairSection => PoiPorchLayout.LOW_STAIRS_DEPTH,
            _ => 0f
        };
        footprint = default;

        if (depth == 0f)
            return false;

        bool horizontal = part.Yaw % 180 == 0;
        float width = part.Part == PoiPart.PorchDeck ? PoiPorchLayout.WIDTH : PoiPorchLayout.STAIR_SECTION_WIDTH;
        var size = horizontal ? new Vector2(width, depth) : new Vector2(depth, width);
        footprint = new Rect(new Vector2(part.X, -part.Z) - size * 0.5f, size);
        return true;
    }

    private void DrawValidation(PoiBuildPreparation preparation)
    {
        string anchorError = PoiSceneBuilder.GetAnchorError(_anchor);

        if (anchorError.Length > 0)
            EditorGUILayout.HelpBox(anchorError, MessageType.Info);
        else if (PoiSceneBuilder.TryFindResult(_anchor, out GameObject result))
        {
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.Label(Get("existingResult"), EditorStyles.miniLabel);

            if (GUILayout.Button(Get("selectResult"), EditorStyles.miniButton))
            {
                Selection.activeGameObject = result;
                EditorGUIUtility.PingObject(result);
            }
        }

        if (preparation.RoofNote.Length > 0)
            EditorGUILayout.HelpBox(preparation.RoofNote, MessageType.Warning);

        foreach (string error in preparation.Errors)
            EditorGUILayout.HelpBox(error, MessageType.Error);

        if (!PoiBuilderAssets.IsValidName(_assetName))
            EditorGUILayout.HelpBox(Get("invalidName"), MessageType.Warning);
    }

    private void DrawFooter()
    {
        using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);

        if (_status.Length > 0)
            EditorGUILayout.HelpBox(_status, _statusType);

        using var row = new EditorGUILayout.HorizontalScope();
        bool validName = PoiBuilderAssets.IsValidName(_assetName);
        bool validAnchor = PoiSceneBuilder.GetAnchorError(_anchor).Length == 0;
        Color color = GUI.backgroundColor;

        using (new EditorGUI.DisabledScope(!validAnchor || !_preparation.CanBuild || !validName))
        {
            GUI.backgroundColor = _amber;

            if (GUILayout.Button(Get("build"), GUILayout.Height(32f), GUILayout.ExpandWidth(true)))
                Run(Build);

            GUI.backgroundColor = color;
        }

        using (new EditorGUI.DisabledScope(!validName))
        {
            if (GUILayout.Button(Get("savePreset"), GUILayout.Height(32f)))
                Run(SavePreset);
        }

        using (new EditorGUI.DisabledScope(!validAnchor || !validName || !PoiSceneBuilder.TryFindResult(_anchor, out _)))
        {
            if (GUILayout.Button(Get("savePrefab"), GUILayout.Height(32f)))
                Run(SavePrefab);
        }
    }

    private void Build()
    {
        _preparation = PoiBuildPreparation.Create(_draft);

        if (!_preparation.CanBuild)
            return;

        GameObject result = PoiSceneBuilder.Build(_anchor, _preparation, _assetName.Trim());
        EditorGUIUtility.PingObject(result);
        SetStatus(Format("built", result.name, Get(_preparation.Plan.Pitched ? "gableRoof" : "flatRoof")));
    }

    private void SavePreset()
    {
        if (!PoiBuilderAssets.TryGetSavePath<PoiBuildPreset>(PoiBuilderAssets.PRESETS, _assetName, "asset", out string path))
            return;

        PoiBuildPreset preset = PoiBuilderAssets.SavePreset(_draft, path);
        LoadPreset(preset);
        _presets = PoiBuilderAssets.FindPresets();
        EditorGUIUtility.PingObject(preset);
        SetStatus(Format("presetSaved", preset.name));
    }

    private void SavePrefab()
    {
        if (!PoiSceneBuilder.TryFindResult(_anchor, out GameObject result) ||
            !PoiBuilderAssets.TryGetSavePath<GameObject>(PoiBuilderAssets.PREFABS, _assetName, "prefab", out string path))
            return;

        GameObject prefab = PoiSceneBuilder.SavePrefab(result, path);
        EditorGUIUtility.PingObject(prefab);
        SetStatus(Format("prefabSaved", prefab.name));
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus(Get("operationFailed"), MessageType.Error);
        }

        GUIUtility.ExitGUI();
    }

    private void SetStatus(string text, MessageType type = MessageType.Info)
    {
        _status = text;
        _statusType = type;
    }
}
