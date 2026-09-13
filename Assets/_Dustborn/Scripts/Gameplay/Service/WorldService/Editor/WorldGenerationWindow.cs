#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class WorldGenerationWindow : EditorWindow
{
    public const string DEFAULT_SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";

    private const float TIMINGS_HEIGHT = 260f;
    private const double MIN_SHOWN_SECONDS = 0.05d;

    [SerializeField] private WorldGenerator _owner;
    [SerializeField] private WorldBuildSettings _settings;
    [SerializeField] private int _tab;
    [SerializeField] private int _biome;
    [SerializeField] private bool _advanced;
    [SerializeField] private bool _timings = true;
    [SerializeField] private bool _details;
    private Vector2 _scroll;
    private Vector2 _timingsScroll;

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

    private void OnEnable()
    {
        minSize = new Vector2(400f, 530f);

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
        float previousWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.52f, 190f, 300f);

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
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Один запуск — весь мир", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(WorldGenerationEditorRun.Busy || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            WorldGenerator selected = EditorGUILayout.ObjectField("Мир в сцене", _owner, typeof(WorldGenerator), true) as WorldGenerator;

            if (selected != _owner)
                Select(selected);

            if (_owner == null && GUILayout.Button("Добавить управление миром в сцену"))
            {
                var holder = new GameObject("WorldGenerator");
                Undo.RegisterCreatedObjectUndo(holder, "Добавить управление миром");
                Select(Undo.AddComponent<WorldGenerator>(holder));
            }

            if (_settings == null)
            {
                _settings = EditorGUILayout.ObjectField("Настройки мира", _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;
                EditorGUILayout.HelpBox("Назначьте набор настроек мира.", MessageType.Warning);
                return;
            }

            _tab = GUILayout.Toolbar(_tab, new[] { "Основное", "Редактор", "В игре" });
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var settings = new SerializedObject(_settings);

            if (_tab == 0)
                MainSettings(settings);
            else if (_tab == 1)
                PreviewSettings(settings);
            else
                RuntimeSettings(settings);

            EditorGUILayout.Space(8f);
            _advanced = EditorGUILayout.Foldout(_advanced, "Дополнительные настройки", true);

            if (_advanced)
                AdvancedSettings(settings);

            settings.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(_owner == null || !_settings.IsValid))
            {
                if (GUILayout.Button("Сгенерировать мир", GUILayout.Height(38f)))
                    WorldGenerationEditorRun.Start(_owner, _settings, true);
            }

            BakedWorld saved = WorldGenerationEditorRun.SavedWorld(_owner, _settings);

            using (new EditorGUI.DisabledScope(_owner == null || saved == null || !saved.IsValid))
            {
                if (GUILayout.Button("Обновить только предпросмотр"))
                    WorldGenerationEditorRun.Start(_owner, _settings, false);
            }

            using (new EditorGUI.DisabledScope(_owner == null || _owner.PreviewRoot == null))
            {
                if (GUILayout.Button("Убрать предпросмотр из сцены"))
                    WorldGenerationEditorRun.ClearPreview(_owner);
            }
        }

        EditorGUILayout.Space(6f);
        Rect progress = EditorGUILayout.GetControlRect(false, 22f);
        bool runtime = Application.isPlaying && _owner != null;
        EditorGUI.ProgressBar(progress, runtime ? _owner.Progress : WorldGenerationEditorRun.Progress,
            runtime ? _owner.Status : WorldGenerationEditorRun.Status);
        DrawTimings();
        EditorGUILayout.Space(6f);
    }

    private void DrawTimings()
    {
        if (WorldGenerationEditorRun.Busy || WorldGenerationEditorRun.Timings.Count == 0)
            return;

        double total = WorldGenerationEditorRun.TotalSeconds;
        string title = WorldGenerationEditorRun.LastRunGenerated ? "Время генерации" : "Время обновления предпросмотра";

        EditorGUILayout.Space(4f);
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

    private void MainSettings(SerializedObject settings)
    {
        if (_settings.Config == null || _settings.Biomes == null || _settings.Voxels == null)
        {
            _advanced = true;
            return;
        }

        var config = new SerializedObject(_settings.Config);
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Мир и сид", EditorStyles.boldLabel);
        Property(config, "WorldSize", "Размер мира, м");
        using (new EditorGUILayout.HorizontalScope())
        {
            Property(config, "Seed", "Сид мира", "Один сид определяет биомы, рельеф, дороги, здания и растительность.");

            if (GUILayout.Button("Новый", GUILayout.Width(65f)))
                Field(config, "Seed").intValue = System.Guid.NewGuid().GetHashCode();
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Рельеф биома", EditorStyles.boldLabel);
        DrawBiome(config);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Растительность", EditorStyles.boldLabel);
        Property(settings, "SpawnDecor", "Трава, деревья и камни");
        Property(settings, "GrassDensity", "Плотность травы", "Доля от плотности, заданной в биомах. Одинакова для редактора и игры.");

        var voxels = new SerializedObject(_settings.Voxels);
        Property(voxels, "GrassDistance", "Дальность травы в игре, м");
        voxels.ApplyModifiedProperties();
        config.ApplyModifiedProperties();
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

    private void PreviewSettings(SerializedObject settings)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Весь ландшафт и все здания видны в редакторе. Трава и мелкие детали ограничены кольцами вокруг центра, чтобы не перегружать сцену.", EditorStyles.wordWrappedLabel);
        Property(settings, "PreviewCenter", "Центр детализации, м");

        if (GUILayout.Button("Взять центр из окна сцены") && SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            float worldSize = _settings.Config == null ? 8192f : _settings.Config.WorldSize;
            Field(settings, "PreviewCenter").vector2Value = new Vector2(Mathf.Clamp(pivot.x, 0f, worldSize), Mathf.Clamp(pivot.z, 0f, worldSize));
        }

        Property(settings, "PreviewColliders", "Коллизия в предпросмотре");
        Property(settings, "MemoryBudget", "Память геометрии, МБ", "Общий лимит сеток и коллизии для редактора и игры, не для карт и растительности. Проверяется до создания мира.");

        if (_settings.Voxels != null)
        {
            var voxels = new SerializedObject(_settings.Voxels);
            Property(voxels, "GrassMaxLod", "Кольцо травы в редакторе");
            Property(voxels, "RockMaxLod", "Кольцо камней");
            Property(voxels, "TreeMaxLod", "Кольцо деревьев");
            voxels.ApplyModifiedProperties();
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Геометрия предпросмотра не записывается в сцену, чтобы не раздувать её размер. После повторного открытия сцены нажмите «Обновить только предпросмотр». POI сохраняют связи с префабами. В сборку игры предпросмотр не попадает.", EditorStyles.wordWrappedLabel);
    }

    private void RuntimeSettings(SerializedObject settings)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Игра читает сохранённый мир: собирает ландшафт, затем размещает POI. Карты заново не генерируются. Растительность следует за игроком.", EditorStyles.wordWrappedLabel);

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

        Property(settings, "ShowLoadingScreen", "Показывать экран загрузки");
        Property(settings, "LoadingBudget", "Время загрузки на кадр, мс");
        Property(settings, "PoisPerFrame", "Зданий за кадр");
    }

    private void AdvancedSettings(SerializedObject settings)
    {
        Property(settings, "LodCount", "Количество уровней детализации");
        Property(settings, "NearDistance", "Радиус лучшей детализации, м");
        Property(settings, "ControlResolution", "Разрешение текстур поверхности");
        Property(settings, "MeshWorkers", "Параллельных задач геометрии");
        Property(settings, "DecorBudget", "Растительность: время на кадр, мс");

        if (_settings.Config != null)
        {
            var config = new SerializedObject(_settings.Config);
            Property(config, "MaxHeight", "Максимальная высота, м");
            Property(config, "ReliefScale", "Общий множитель рельефа");
            Property(config, "BiomeBlendRadius", "Плавность границ биомов, м");
            Property(config, "SeaLevel", "Уровень воды, м");
            Property(config, "HeightCellSize", "Шаг карты высот, м");
            Property(config, "HubCount", "Всего поселений");
            Property(config, "MinHouses", "Домов в поселении, от");
            Property(config, "MaxHouses", "Домов в поселении, до");
            config.ApplyModifiedProperties();
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Источники настроек — без копирования значений", EditorStyles.boldLabel);
        Property(settings, "Config", "Параметры генерации");
        Property(settings, "Biomes", "Набор биомов");
        Property(settings, "Pois", "Набор зданий POI");
        Property(settings, "Voxels", "Параметры вокселей");
        WorldBuildSettings selected = EditorGUILayout.ObjectField("Набор настроек мира", _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;

        if (selected != null && selected != _settings)
        {
            settings.ApplyModifiedProperties();
            _settings = selected;
            GUIUtility.ExitGUI();
        }
    }

    private static SerializedProperty Field(SerializedObject target, string property)
    {
        return target.FindProperty($"<{property}>k__BackingField");
    }

    private static void PausedBehaviours(SerializedProperty paused)
    {
        paused.isExpanded = EditorGUILayout.Foldout(paused.isExpanded,
            new GUIContent("Приостановить до загрузки", "Скрипты управления игроком. Включаются после появления коллизии и POI."), true);

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

        if (GUILayout.Button("Добавить скрипт"))
        {
            paused.InsertArrayElementAtIndex(paused.arraySize);
            paused.GetArrayElementAtIndex(paused.arraySize - 1).objectReferenceValue = null;
        }
    }

    private static void Property(SerializedObject target, string property, string label, string tooltip = null)
    {
        EditorGUILayout.PropertyField(Field(target, property), new GUIContent(label, tooltip));
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
        EditorGUILayout.LabelField("Управление миром", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Настройки и запуск находятся в одной панели.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Открыть генерацию мира", GUILayout.Height(32f)))
            WorldGenerationWindow.Open(owner);

        if (Application.isPlaying)
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 22f), owner.Progress, owner.Status);
        else
            EditorGUILayout.LabelField(owner.World != null && owner.World.IsValid ? "Сохранённый мир готов к игре" : "Мир ещё не сохранён через общую генерацию");
    }
}

[CustomEditor(typeof(WorldBuildSettings))]
public class WorldBuildSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Настройки мира", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Основные параметры, режимы редактора и игры редактируются в общей панели.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Открыть генерацию мира", GUILayout.Height(32f)))
            WorldGenerationWindow.OpenSettings((WorldBuildSettings)target);
    }
}
#endif
