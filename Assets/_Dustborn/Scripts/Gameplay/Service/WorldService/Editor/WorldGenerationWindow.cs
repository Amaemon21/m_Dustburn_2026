#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class WorldGenerationWindow : EditorWindow
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

    private void WorldTab(SerializedObject settings)
    {
        var config = new SerializedObject(_settings.Config);

        Section("Сид и размер");

        using (new EditorGUILayout.HorizontalScope())
        {
            Property(config, "Seed", "Сид мира", "Один сид определяет биомы, рельеф, воду, дороги, здания и растительность.");

            if (GUILayout.Button("Случайный", GUILayout.Width(84f)))
                Field(config, "Seed").intValue = Guid.NewGuid().GetHashCode();
        }

        Property(config, "WorldSize", "Размер мира, м", "Смена размера или шага карты высот делает сохранённый мир устаревшим.");

        Section("Рельеф");
        Property(config, "MaxHeight", "Наибольшая высота, м");
        Property(config, "ReliefScale", "Общий множитель рельефа");
        Property(config, "BiomeBlendRadius", "Плавность границ биомов, м");
        Nested(config, "Stamps", "Enabled", "Штампы рельефа (горы, кратеры)");
        Nested(config, "Stamps", "Count", "Число штампов");
        Nested(config, "Stamps", "AmplitudeScale", "Высота штампов, множитель");

        _relief = EditorGUILayout.Foldout(_relief, "Рельеф отдельного биома", true);

        if (_relief)
        {
            EditorGUI.indentLevel++;
            DrawBiome(config);
            EditorGUI.indentLevel--;
        }

        Section("Растительность");
        Property(settings, "SpawnDecor", "Трава, деревья и камни");
        Property(settings, "GrassDensity", "Плотность травы", "Множитель плотности из биомов: 0 — без травы, 1 — как задано. Выше 1 заметно растёт нагрузка. Одинакова для редактора и игры.");

        var voxels = new SerializedObject(_settings.Voxels);
        Property(voxels, "GrassDistance", "Дальность травы в игре, м");
        voxels.ApplyModifiedProperties();
        config.ApplyModifiedProperties();
    }

    private void WaterTab(SerializedObject settings)
    {
        var config = new SerializedObject(_settings.Config);

        Section("Море и берег");
        Property(config, "SeaLevel", "Уровень моря, м", "0 — без моря и воды.");
        Nested(config, "Water", "Coast", "Море вокруг мира");
        Nested(config, "Water", "CoastWidth", "Ширина прибрежной полосы, м");

        Section("Реки и озёра");
        Nested(config, "Water", "Enabled", "Реки, озёра и пруды");
        Nested(config, "Water", "DrainageValleys", "Прорезать речные долины");
        Nested(config, "Water", "RiverStartArea", "Водосбор, с которого начинается река, км²");
        Nested(config, "Water", "RiverWidthScale", "Ширина рек, множитель");
        Nested(config, "Water", "RiverDepthScale", "Глубина рек, множитель");
        Nested(config, "Water", "ThroughRiver", "Главная река");
        Nested(config, "Water", "LakeDensity", "Доля котловин с озером");
        Nested(config, "Water", "PondDensity", "Доля ямок с прудом");
        EditorGUILayout.LabelField("В пустыне и выжженном лесу воды нет: реки заканчиваются у их границы.", _hint);

        SerializedProperty stamps = Field(config, "Water")?.FindPropertyRelative("<Stamps>k__BackingField");

        if (stamps != null)
        {
            EditorGUILayout.PropertyField(stamps.FindPropertyRelative("<Enabled>k__BackingField"), new GUIContent("Штампы формы рек и озёр"));
            EditorGUILayout.PropertyField(stamps.FindPropertyRelative("<Database>k__BackingField"), new GUIContent("База штампов воды"));
        }

        Section("Материалы");
        Property(settings, "WaterMaterial", "Вода");
        Property(settings, "IceMaterial", "Лёд");
        Property(settings, "BridgeMaterial", "Мосты");
        Property(settings, "CulvertPrefab", "Водопропускная труба");
        config.ApplyModifiedProperties();
    }

    private void SettlementTab()
    {
        var config = new SerializedObject(_settings.Config);

        Section("Сколько поселений");
        Nested(config, "CityProfile", "Count", "Города");
        Nested(config, "TownProfile", "Count", "Посёлки");
        Nested(config, "CountryTownProfile", "Count", "Сёла");
        Nested(config, "GhostTownProfile", "Count", "Заброшенные посёлки");

        Section("Застройка и дороги");
        Property(config, "TileSize", "Размер квартала, м");
        Property(config, "TargetLinkRatio", "Дорог на поселение");
        EditorGUILayout.LabelField("Размер, форма и доли районов каждого типа — в профилях на ассете параметров генерации.", _hint);
        config.ApplyModifiedProperties();
    }

    private void DetailTab(SerializedObject settings)
    {
        Section("Предпросмотр в редакторе");

        using (new EditorGUILayout.HorizontalScope())
        {
            Property(settings, "PreviewCenter", "Центр детализации, м");

            if (GUILayout.Button("Из сцены", GUILayout.Width(80f)) && SceneView.lastActiveSceneView != null)
            {
                Vector3 pivot = SceneView.lastActiveSceneView.pivot;
                float worldSize = _settings.Config.WorldSize;
                Field(settings, "PreviewCenter").vector2Value = new Vector2(Mathf.Clamp(pivot.x, 0f, worldSize), Mathf.Clamp(pivot.z, 0f, worldSize));
            }
        }

        Property(settings, "PreviewColliders", "Коллизия в предпросмотре");
        Property(settings, "PreviewDecorEverywhere", "Деревья и камни по всей карте");

        var voxels = new SerializedObject(_settings.Voxels);
        Property(voxels, "GrassMaxLod", "Кольцо травы");
        Property(voxels, "RockMaxLod", "Кольцо камней");
        Property(voxels, "TreeMaxLod", "Кольцо деревьев");
        voxels.ApplyModifiedProperties();
        EditorGUILayout.LabelField("Геометрия предпросмотра не сохраняется в сцену. После открытия сцены нажмите «Обновить предпросмотр».", _hint);

        Section("Детализация ландшафта");
        Property(settings, "LodCount", "Уровней детализации");
        Property(settings, "NearDistance", "Радиус лучшей детализации, м");
        Property(settings, "ControlResolution", "Разрешение текстур поверхности");
        Property(settings, "MemoryBudget", "Память геометрии, МБ", "Лимит сеток и коллизии. Проверяется до постройки мира в редакторе и в игре.");

        var config = new SerializedObject(_settings.Config);
        Property(config, "HeightCellSize", "Шаг карты высот, м");
        config.ApplyModifiedProperties();
    }

    private void GameTab(SerializedObject settings)
    {
        Section("Загрузка в игре");
        EditorGUILayout.LabelField("Игра не генерирует мир: она строит ландшафт из сохранённого мира, затем расставляет здания.", _hint);

        if (_owner != null)
        {
            var owner = new SerializedObject(_owner);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(owner.FindProperty("_viewer"), new GUIContent("Игрок / наблюдатель"));
            bool viewerChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.PropertyField(owner.FindProperty("_loadOnStart"), new GUIContent("Загружать при запуске"));
            PausedBehaviours(owner.FindProperty("_pauseWhileLoading"));
            owner.ApplyModifiedProperties();

            if (viewerChanged)
                WorldGenerationMigration.ConfigurePlayerPause(_owner);
        }

        Property(settings, "ShowLoadingScreen", "Свой экран загрузки", "Нужен только для сцены, открытой без общего игрового цикла.");

        Section("Время на кадр");
        Property(settings, "LoadingBudget", "Загрузка ландшафта, мс");
        Property(settings, "DecorBudget", "Растительность, мс");
        Property(settings, "PoisPerFrame", "Зданий за кадр");
        Property(settings, "MeshWorkers", "Параллельных задач геометрии");
    }

    private void Sources(SerializedObject settings)
    {
        EditorGUI.indentLevel++;
        Property(settings, "Config", "Параметры генерации");
        Property(settings, "Biomes", "Набор биомов");
        Property(settings, "Pois", "Набор зданий");
        Property(settings, "Voxels", "Параметры вокселей");
        WorldBuildSettings selected = EditorGUILayout.ObjectField("Набор настроек мира", _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;
        EditorGUI.indentLevel--;

        if (selected == null || selected == _settings)
            return;

        settings.ApplyModifiedProperties();
        _settings = selected;
        GUIUtility.ExitGUI();
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

    private void DrawBiome(SerializedObject config)
    {
        BiomeDatabase biomes = _settings.Biomes;

        if (biomes.Count == 0)
            return;

        var names = new string[biomes.Count];

        for (int index = 0; index < biomes.Count; index++)
            names[index] = biomes.Get(index) == null ? "Не назначен" : BiomeName(biomes.Get(index).Type);

        _biome = EditorGUILayout.Popup("Биом", Mathf.Clamp(_biome, 0, biomes.Count - 1), names);
        BiomeDefinition biome = biomes.Get(_biome);

        if (biome == null)
            return;

        var profile = new SerializedObject(biome);
        float maxHeight = Mathf.Max(1f, _settings.Config.MaxHeight);
        Metres(profile, "BaseHeight", "Базовая высота, м", maxHeight, 1f);
        Metres(profile, "HillAmplitude", "Плавные перепады, м", maxHeight, 1f);
        Wavelength(config, "HillFrequency", "Масштаб перепадов, м");
        Metres(profile, "DetailAmplitude", "Амплитуда неровностей, м", maxHeight, 0.2f);
        Wavelength(config, "DetailFrequency", "Масштаб неровностей, м");
        profile.ApplyModifiedProperties();
    }

    private static void Section(string title)
    {
        EditorGUILayout.Space(SECTION_GAP);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static SerializedProperty Field(SerializedObject target, string property)
    {
        return target.FindProperty($"<{property}>k__BackingField");
    }

    private static void PausedBehaviours(SerializedProperty paused)
    {
        paused.isExpanded = EditorGUILayout.Foldout(paused.isExpanded,
            new GUIContent("Приостановить до загрузки", "Скрипты управления игроком. Включаются после появления коллизии и зданий."), true);

        if (!paused.isExpanded)
            return;

        for (int index = 0; index < paused.arraySize; index++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty element = paused.GetArrayElementAtIndex(index);
                element.objectReferenceValue = EditorGUILayout.ObjectField($"Скрипт {index + 1}", element.objectReferenceValue, typeof(Behaviour), true);

                if (!GUILayout.Button("−", GUILayout.Width(24f)))
                    continue;

                element.objectReferenceValue = null;
                paused.DeleteArrayElementAtIndex(index);
                break;
            }
        }

        if (!GUILayout.Button("Добавить скрипт"))
            return;

        paused.InsertArrayElementAtIndex(paused.arraySize);
        paused.GetArrayElementAtIndex(paused.arraySize - 1).objectReferenceValue = null;
    }

    private static void Property(SerializedObject target, string property, string label, string tooltip = null)
    {
        SerializedProperty field = Field(target, property);

        if (field != null)
            EditorGUILayout.PropertyField(field, new GUIContent(label, tooltip));
    }

    private static void Nested(SerializedObject target, string property, string child, string label)
    {
        SerializedProperty holder = Field(target, property);
        SerializedProperty field = holder?.FindPropertyRelative($"<{child}>k__BackingField");

        if (field != null)
            EditorGUILayout.PropertyField(field, new GUIContent(label));
    }

    private static void Metres(SerializedObject target, string property, string label, float height, float limit)
    {
        SerializedProperty value = Field(target, property);
        EditorGUI.BeginChangeCheck();
        float metres = EditorGUILayout.Slider(new GUIContent(label, "Параметр выбранного биома. Амплитуда дополнительно умножается на общий множитель рельефа."), value.floatValue * height, 0f, height * limit);

        if (EditorGUI.EndChangeCheck())
            value.floatValue = metres / height;
    }

    private static void Wavelength(SerializedObject config, string property, string label)
    {
        SerializedProperty value = Field(config, property);
        float worldSize = Mathf.Max(1, Field(config, "WorldSize").intValue);
        EditorGUI.BeginChangeCheck();
        float scale = EditorGUILayout.FloatField(new GUIContent(label, "Общий масштаб для всех биомов: больше значение — шире и реже перепады."), worldSize / Mathf.Max(0.1f, value.floatValue));

        if (EditorGUI.EndChangeCheck())
            value.floatValue = worldSize / Mathf.Clamp(scale, 1f, worldSize * 10f);
    }

    private static string BiomeName(BiomeType type)
    {
        return type switch
        {
            BiomeType.PineForest => "Сосновый лес",
            BiomeType.BurntForest => "Выжженный лес",
            BiomeType.Desert => "Пустыня",
            BiomeType.Snow => "Снег",
            BiomeType.Wasteland => "Пустошь",
            _ => type.ToString()
        };
    }
}

[CustomEditor(typeof(WorldGenerator))]
public class WorldGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var owner = (WorldGenerator)target;

        if (GUILayout.Button("Открыть генерацию мира", GUILayout.Height(32f)))
            WorldGenerationWindow.Open(owner);

        if (Application.isPlaying)
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 22f), owner.Progress, owner.Status);
        else
            EditorGUILayout.HelpBox(WorldGenerationWindow.Describe(owner.World), owner.World != null && owner.World.IsValid ? MessageType.Info : MessageType.Warning);
    }
}

[CustomEditor(typeof(WorldBuildSettings))]
public class WorldBuildSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Настройки мира редактируются в общей панели.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Открыть генерацию мира", GUILayout.Height(32f)))
            WorldGenerationWindow.OpenSettings((WorldBuildSettings)target);
    }
}
#endif
