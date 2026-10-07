using System;
using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow
{
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
}
