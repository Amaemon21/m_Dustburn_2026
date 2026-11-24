#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow : EditorWindow, IHasCustomMenu
{
    public const string DEFAULT_SETTINGS_PATH = "Assets/_Dustborn/Content/Configs/World/WorldBuildSettings.asset";

    private const float TIMINGS_HEIGHT = 220f;
    private const double MIN_SHOWN_SECONDS = 0.05d;
    private const float PRIMARY_HEIGHT = 36f;
    private const float SECTION_GAP = 10f;

    private static readonly string[] TAB_KEYS =
    {
        "editor.worldWindow.tabWorld",
        "editor.worldWindow.tabWater",
        "editor.worldWindow.tabSettlements",
        "editor.worldWindow.tabDetail",
        "editor.worldWindow.tabGame"
    };

    [SerializeField] private WorldGenerator _owner;
    [SerializeField] private WorldBuildSettings _settings;
    [SerializeField] private int _tab;
    [SerializeField] private int _biome;
    [SerializeField] private bool _relief;
    [SerializeField] private bool _sources;
    [SerializeField] private bool _timings;
    [SerializeField] private bool _details;
    private Vector2 _scroll;
    private Vector2 _timingsScroll;
    private GUIStyle _title;
    private GUIStyle _hint;

    [MenuItem("World/World Generation")]
    public static void Open()
    {
        GetWindow<WorldGenerationWindow>(LocalizationService.Text("editor.worldWindow.title")).Show();
    }

    public static void Open(WorldGenerator owner)
    {
        var window = GetWindow<WorldGenerationWindow>(LocalizationService.Text("editor.worldWindow.title"));
        window.Select(owner);
        window.Show();
    }

    public static void OpenSettings(WorldBuildSettings settings)
    {
        var window = GetWindow<WorldGenerationWindow>(LocalizationService.Text("editor.worldWindow.title"));
        window._settings = settings;
        window.Show();
    }

    public static string Describe(BakedWorld world)
    {
        if (world == null)
            return LocalizationService.Text("editor.worldWindow.notGenerated");

        if (world.Stale)
            return LocalizationService.Text("editor.worldWindow.outdated");

        if (!world.IsValid)
            return LocalizationService.Text("editor.worldWindow.incomplete");

        string path = AssetDatabase.GetAssetPath(world);
        string saved = string.IsNullOrEmpty(path) ? "" : LocalizationService.Format("editor.worldWindow.savedAt", File.GetLastWriteTime(path));

        return LocalizationService.Format("editor.worldWindow.ready", world.Config.Seed, world.Config.WorldSize / 1024f, saved);
    }

    private void OnEnable()
    {
        titleContent = new GUIContent(LocalizationService.Text("editor.worldWindow.title"));
        minSize = new Vector2(420f, 520f);

        if (_owner == null)
        {
            WorldGenerator[] owners = FindObjectsByType<WorldGenerator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (owners.Length == 1)
                Select(owners[0]);
        }

        if (_settings == null)
            _settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(DEFAULT_SETTINGS_PATH);
    }

    private void Select(WorldGenerator owner)
    {
        _owner = owner;
        _settings = owner != null && owner.Settings != null
            ? owner.Settings : AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(DEFAULT_SETTINGS_PATH);
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
        _hint ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

        float previousWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.5f, 180f, 280f);

        try
        {
            DrawWindow();
        }
        finally
        {
            EditorGUIUtility.labelWidth = previousWidth;
        }
    }

    private void DrawWindow()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.title"), _title);

        using (new EditorGUI.DisabledScope(Locked))
            DrawScene();

        if (_settings == null)
        {
            _settings = EditorGUILayout.ObjectField(LocalizationService.Text("editor.worldWindow.settings"), _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;
            EditorGUILayout.HelpBox(LocalizationService.Text("editor.worldWindow.assignSettings"), MessageType.Warning);
            return;
        }

        DrawStatus();
        DrawActions();
        DrawProgress();

        EditorGUILayout.Space(SECTION_GAP);

        using (new EditorGUI.DisabledScope(Locked))
        {
            _tab = GUILayout.Toolbar(_tab, Array.ConvertAll(TAB_KEYS, LocalizationService.Text), GUILayout.Height(24f));
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var settings = new SerializedObject(_settings);

            if (!_settings.IsValid)
            {
                EditorGUILayout.HelpBox(LocalizationService.Text("editor.worldWindow.missingSources"), MessageType.Error);
                _sources = true;
            }
            else if (_tab == 0)
            {
                WorldTab(settings);
            }
            else if (_tab == 1)
            {
                WaterTab(settings);
            }
            else if (_tab == 2)
            {
                SettlementTab();
            }
            else if (_tab == 3)
            {
                DetailTab(settings);
            }
            else
            {
                GameTab(settings);
            }

            EditorGUILayout.Space(SECTION_GAP);
            _sources = EditorGUILayout.Foldout(_sources, LocalizationService.Text("editor.worldWindow.sources"), true);

            if (_sources)
                Sources(settings);

            settings.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();
        }

        DrawTools();
        DrawTimings();
        EditorGUILayout.Space(6f);
    }

    private static bool Locked => WorldGenerationEditorRun.Busy || EditorApplication.isPlayingOrWillChangePlaymode;
}

#endif
