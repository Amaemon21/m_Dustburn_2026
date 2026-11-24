using System;
using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow
{
    private void WorldTab(SerializedObject settings)
    {
        var config = new SerializedObject(_settings.Config);

        Section(LocalizationService.Text("editor.worldWindow.seedAndSize"));

        using (new EditorGUILayout.HorizontalScope())
        {
            Property(config, "Seed", LocalizationService.Text("editor.worldWindow.seed"), LocalizationService.Text("editor.worldWindow.seedHelp"));

            if (GUILayout.Button(LocalizationService.Text("editor.worldWindow.randomSeed"), GUILayout.Width(84f)))
                Field(config, "Seed").intValue = Guid.NewGuid().GetHashCode();
        }

        Property(config, "WorldSize", LocalizationService.Text("editor.worldWindow.worldSize"), LocalizationService.Text("editor.worldWindow.worldSizeHelp"));

        Section(LocalizationService.Text("editor.worldWindow.relief"));
        Property(config, "MaxHeight", LocalizationService.Text("editor.worldWindow.maxHeight"));
        Property(config, "ReliefScale", LocalizationService.Text("editor.worldWindow.reliefScale"));
        Property(config, "BiomeBlendRadius", LocalizationService.Text("editor.worldWindow.biomeBlendRadius"));
        Nested(config, "Stamps", "Enabled", LocalizationService.Text("editor.worldWindow.terrainStamps"));
        Nested(config, "Stamps", "Count", LocalizationService.Text("editor.worldWindow.terrainStampCount"));
        Nested(config, "Stamps", "AmplitudeScale", LocalizationService.Text("editor.worldWindow.terrainStampAmplitude"));

        _relief = EditorGUILayout.Foldout(_relief, LocalizationService.Text("editor.worldWindow.biomeRelief"), true);

        if (_relief)
        {
            EditorGUI.indentLevel++;
            DrawBiome(config);
            EditorGUI.indentLevel--;
        }

        Section(LocalizationService.Text("editor.worldWindow.vegetation"));
        Property(settings, "SpawnDecor", LocalizationService.Text("loading.decor"));
        Property(settings, "GrassDensity", LocalizationService.Text("editor.worldWindow.grassDensity"), LocalizationService.Text("editor.worldWindow.grassDensityHelp"));

        var voxels = new SerializedObject(_settings.Voxels);
        Property(voxels, "GrassDistance", LocalizationService.Text("editor.worldWindow.grassDistance"));
        voxels.ApplyModifiedProperties();
        config.ApplyModifiedProperties();
    }

    private void WaterTab(SerializedObject settings)
    {
        var config = new SerializedObject(_settings.Config);

        Section(LocalizationService.Text("editor.worldWindow.coastSection"));
        Property(config, "SeaLevel", LocalizationService.Text("editor.worldWindow.seaLevel"), LocalizationService.Text("editor.worldWindow.seaLevelHelp"));
        Nested(config, "Water", "Coast", LocalizationService.Text("editor.worldWindow.coast"));
        Nested(config, "Water", "CoastWidth", LocalizationService.Text("editor.worldWindow.coastWidth"));

        Section(LocalizationService.Text("editor.worldWindow.inlandWaterSection"));
        Nested(config, "Water", "Enabled", LocalizationService.Text("editor.worldWindow.inlandWater"));
        Nested(config, "Water", "DrainageValleys", LocalizationService.Text("editor.worldWindow.drainageValleys"));
        Nested(config, "Water", "RiverStartArea", LocalizationService.Text("editor.worldWindow.riverStartArea"));
        Nested(config, "Water", "RiverWidthScale", LocalizationService.Text("editor.worldWindow.riverWidthScale"));
        Nested(config, "Water", "RiverDepthScale", LocalizationService.Text("editor.worldWindow.riverDepthScale"));
        Nested(config, "Water", "ThroughRiver", LocalizationService.Text("editor.worldWindow.throughRiver"));
        Nested(config, "Water", "LakeDensity", LocalizationService.Text("editor.worldWindow.lakeDensity"));
        Nested(config, "Water", "PondDensity", LocalizationService.Text("editor.worldWindow.pondDensity"));
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.dryBiomeHelp"), _hint);

        SerializedProperty stamps = Field(config, "Water")?.FindPropertyRelative("<Stamps>k__BackingField");

        if (stamps != null)
        {
            EditorGUILayout.PropertyField(stamps.FindPropertyRelative("<Enabled>k__BackingField"), new GUIContent(LocalizationService.Text("editor.worldWindow.waterStamps")));
            EditorGUILayout.PropertyField(stamps.FindPropertyRelative("<Database>k__BackingField"), new GUIContent(LocalizationService.Text("editor.worldWindow.waterStampDatabase")));
        }

        Section(LocalizationService.Text("editor.worldWindow.materials"));
        Property(settings, "WaterMaterial", LocalizationService.Text("editor.worldWindow.waterMaterial"));
        Property(settings, "IceMaterial", LocalizationService.Text("editor.worldWindow.iceMaterial"));
        Property(settings, "BridgeMaterial", LocalizationService.Text("editor.worldWindow.bridgeMaterial"));
        Property(settings, "CulvertPrefab", LocalizationService.Text("editor.worldWindow.culvertPrefab"));
        config.ApplyModifiedProperties();
    }

    private void SettlementTab()
    {
        var config = new SerializedObject(_settings.Config);

        Section(LocalizationService.Text("editor.worldWindow.settlementCounts"));
        Nested(config, "CityProfile", "Count", LocalizationService.Text("editor.worldWindow.cities"));
        Nested(config, "TownProfile", "Count", LocalizationService.Text("editor.worldWindow.towns"));
        Nested(config, "CountryTownProfile", "Count", LocalizationService.Text("editor.worldWindow.villages"));
        Nested(config, "GhostTownProfile", "Count", LocalizationService.Text("editor.worldWindow.ghostTowns"));

        Section(LocalizationService.Text("editor.worldWindow.streets"));
        Property(config, "TileSize", LocalizationService.Text("editor.worldWindow.tileSize"));
        Property(config, "TargetLinkRatio", LocalizationService.Text("editor.worldWindow.targetLinkRatio"));
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.settlementProfilesHelp"), _hint);
        config.ApplyModifiedProperties();
    }

    private void DetailTab(SerializedObject settings)
    {
        Section(LocalizationService.Text("editor.worldWindow.editorPreview"));

        using (new EditorGUILayout.HorizontalScope())
        {
            Property(settings, "PreviewCenter", LocalizationService.Text("editor.worldWindow.previewCenter"));

            if (GUILayout.Button(LocalizationService.Text("editor.worldWindow.centerFromScene"), GUILayout.Width(80f)) && SceneView.lastActiveSceneView != null)
            {
                Vector3 pivot = SceneView.lastActiveSceneView.pivot;
                float worldSize = _settings.Config.WorldSize;
                Field(settings, "PreviewCenter").vector2Value = new Vector2(Mathf.Clamp(pivot.x, 0f, worldSize), Mathf.Clamp(pivot.z, 0f, worldSize));
            }
        }

        Property(settings, "PreviewColliders", LocalizationService.Text("editor.worldWindow.previewColliders"));
        Property(settings, "PreviewDecorEverywhere", LocalizationService.Text("editor.worldWindow.previewDecorEverywhere"));

        var voxels = new SerializedObject(_settings.Voxels);
        Property(voxels, "GrassMaxLod", LocalizationService.Text("editor.worldWindow.grassMaxLod"));
        Property(voxels, "RockMaxLod", LocalizationService.Text("editor.worldWindow.rockMaxLod"));
        Property(voxels, "TreeMaxLod", LocalizationService.Text("editor.worldWindow.treeMaxLod"));
        voxels.ApplyModifiedProperties();
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.previewPersistenceHelp"), _hint);

        Section(LocalizationService.Text("editor.worldWindow.terrainDetail"));
        Property(settings, "LodCount", LocalizationService.Text("editor.worldWindow.lodCount"));
        Property(settings, "NearDistance", LocalizationService.Text("editor.worldWindow.nearDistance"));
        Property(settings, "ControlResolution", LocalizationService.Text("editor.worldWindow.controlResolution"));
        Property(settings, "MemoryBudget", LocalizationService.Text("editor.worldWindow.memoryBudget"), LocalizationService.Text("editor.worldWindow.memoryBudgetHelp"));

        var config = new SerializedObject(_settings.Config);
        Property(config, "HeightCellSize", LocalizationService.Text("editor.worldWindow.heightCellSize"));
        config.ApplyModifiedProperties();
    }

    private void GameTab(SerializedObject settings)
    {
        Section(LocalizationService.Text("editor.worldWindow.gameLoading"));
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.gameLoadingHelp"), _hint);

        if (_owner != null)
        {
            var owner = new SerializedObject(_owner);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(owner.FindProperty("_viewer"), new GUIContent(LocalizationService.Text("editor.worldWindow.viewer")));
            bool viewerChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.PropertyField(owner.FindProperty("_loadOnStart"), new GUIContent(LocalizationService.Text("editor.worldWindow.loadOnStart")));
            PausedBehaviours(owner.FindProperty("_pauseWhileLoading"));
            owner.ApplyModifiedProperties();

            if (viewerChanged)
                WorldGenerationMigration.ConfigurePlayerPause(_owner);
        }

        Property(settings, "ShowLoadingScreen", LocalizationService.Text("editor.worldWindow.showLoadingScreen"), LocalizationService.Text("editor.worldWindow.showLoadingScreenHelp"));

        Section(LocalizationService.Text("editor.worldWindow.frameBudget"));
        Property(settings, "LoadingBudget", LocalizationService.Text("editor.worldWindow.loadingBudget"));
        Property(settings, "DecorBudget", LocalizationService.Text("editor.worldWindow.decorBudget"));
        Property(settings, "PoisPerFrame", LocalizationService.Text("editor.worldWindow.poisPerFrame"));
        Property(settings, "MeshWorkers", LocalizationService.Text("editor.worldWindow.meshWorkers"));
    }

    private void Sources(SerializedObject settings)
    {
        EditorGUI.indentLevel++;
        Property(settings, "Config", LocalizationService.Text("editor.worldWindow.config"));
        Property(settings, "Biomes", LocalizationService.Text("editor.worldWindow.biomes"));
        Property(settings, "Pois", LocalizationService.Text("editor.worldWindow.pois"));
        Property(settings, "Voxels", LocalizationService.Text("editor.worldWindow.voxels"));
        Property(settings, "DecorEdits", LocalizationService.Text("editor.worldWindow.decorEdits"));
        WorldBuildSettings selected = EditorGUILayout.ObjectField(LocalizationService.Text("editor.worldWindow.selectSettings"), _settings, typeof(WorldBuildSettings), false) as WorldBuildSettings;
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
            names[index] = biomes.Get(index) == null ? LocalizationService.Text("editor.worldWindow.unassigned") : BiomeName(biomes.Get(index).Type);

        _biome = EditorGUILayout.Popup(LocalizationService.Text("editor.worldWindow.biome"), Mathf.Clamp(_biome, 0, biomes.Count - 1), names);
        BiomeDefinition biome = biomes.Get(_biome);

        if (biome == null)
            return;

        var profile = new SerializedObject(biome);
        float maxHeight = Mathf.Max(1f, _settings.Config.MaxHeight);
        Metres(profile, "BaseHeight", LocalizationService.Text("editor.worldWindow.baseHeight"), maxHeight, 1f);
        Metres(profile, "HillAmplitude", LocalizationService.Text("editor.worldWindow.smoothRelief"), maxHeight, 1f);
        Wavelength(config, "HillFrequency", LocalizationService.Text("editor.worldWindow.reliefFrequency"));
        Metres(profile, "DetailAmplitude", LocalizationService.Text("editor.worldWindow.detailAmplitude"), maxHeight, 0.2f);
        Wavelength(config, "DetailFrequency", LocalizationService.Text("editor.worldWindow.detailFrequency"));
        profile.ApplyModifiedProperties();
    }
}
