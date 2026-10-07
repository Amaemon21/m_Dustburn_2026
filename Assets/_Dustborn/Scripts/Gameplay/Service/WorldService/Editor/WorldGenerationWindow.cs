#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow : EditorWindow
{
    public const string DEFAULT_SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";

    private const float TIMINGS_HEIGHT = 220f;
    private const double MIN_SHOWN_SECONDS = 0.05d;
    private const float PRIMARY_HEIGHT = 36f;
    private const float SECTION_GAP = 10f;

    private static readonly string[] TABS = { "Мир", "Вода", "Поселения", "Детализация", "Игра" };

    private static readonly (string Label, string Menu)[] TOOLS =
    {
        ("Перепечь землю", "Мир/Перепечь землю мира"),
        ("Карты воды и штампов", "Мир/Предпросмотр воды и штампов"),
        ("Вода и штампы в сцене", "Мир/Вода и штампы в сцене"),
        ("Отладка раскраски земли", "Мир/Отладка раскраски земли"),
        ("Отладка стыков LOD", "Мир/Отладка стыков LOD"),
        ("Импорт штампов рельефа", "Мир/Импорт штампов рельефа"),
        ("Импорт штампов воды", "Мир/Импорт штампов воды")
    };

    [SerializeField] private WorldGenerator _owner;
    [SerializeField] private WorldBuildSettings _settings;
    [SerializeField] private int _tab;
    [SerializeField] private int _biome;
    [SerializeField] private bool _relief;
    [SerializeField] private bool _sources;
    [SerializeField] private bool _tools;
    [SerializeField] private bool _timings;
    [SerializeField] private bool _details;
    private Vector2 _scroll;
    private Vector2 _timingsScroll;
    private GUIStyle _title;
    private GUIStyle _hint;

    [MenuItem("Мир/Генерация мира")]
    public static void Open()
    {
        GetWindow<WorldGenerationWindow>("Генерация мира").Show();
    }

    public static void Open(WorldGenerator owner)
    {
        var window = GetWindow<WorldGenerationWindow>("Генерация мира");
        window.Select(owner);
        window.Show();
    }

    public static void OpenSettings(WorldBuildSettings settings)
    {
        var window = GetWindow<WorldGenerationWindow>("Генерация мира");
        window._settings = settings;
        window.Show();
    }

    public static string Describe(BakedWorld world)
    {
        if (world == null)
            return "Мир ещё не сгенерирован";

        if (world.Stale)
            return "Сохранённый мир сделан старой версией генератора — сгенерируйте заново";

        if (!world.IsValid)
            return "Сохранённый мир неполный — сгенерируйте заново";

        string path = AssetDatabase.GetAssetPath(world);
        string saved = string.IsNullOrEmpty(path) ? "" : $", сохранён {File.GetLastWriteTime(path):dd.MM.yyyy HH:mm}";

        return $"Мир готов: сид {world.Config.Seed}, {world.Config.WorldSize / 1024f:0.#} км{saved}";
    }

    private void OnEnable()
    {
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
        EditorGUILayout.LabelField("Генерация мира", _title);

        using (new EditorGUI.DisabledScope(Locked))
            DrawScene();

        if (_settings == null)
        {
            _settings = EditorGUILayout.ObjectField("Настройки мира", _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;
            EditorGUILayout.HelpBox("Назначьте набор настроек мира.", MessageType.Warning);
            return;
        }

        DrawStatus();
        DrawActions();
        DrawProgress();

        EditorGUILayout.Space(SECTION_GAP);

        using (new EditorGUI.DisabledScope(Locked))
        {
            _tab = GUILayout.Toolbar(_tab, TABS, GUILayout.Height(24f));
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var settings = new SerializedObject(_settings);

            if (!_settings.IsValid)
            {
                EditorGUILayout.HelpBox("Не назначены источники настроек — откройте «Источники настроек» внизу.", MessageType.Error);
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
            _sources = EditorGUILayout.Foldout(_sources, "Источники настроек", true);

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
