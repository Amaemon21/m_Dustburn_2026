using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameplayLifetimeScope))]
[CanEditMultipleObjects]
public sealed class GameplayLifetimeScopeEditor : Editor
{
    private static readonly GUIContent SceneTypeLabel = new("Scene Type", "Choose how the gameplay scene prepares the world and spawns the player.");
    private static readonly GUIContent TestSpawnLabel = new("Player Spawn Point", "Initial player position in the test scene.");
    private static readonly GUIContent AutoInjectLabel = new("Auto Inject Game Objects", "VContainer injects these roots and their children when the scene scope is built.");

    private SerializedProperty _script;
    private SerializedProperty _sceneType;
    private SerializedProperty _mainCamera;
    private SerializedProperty _testSpawnPoint;
    private SerializedProperty _spawnPoints;
    private SerializedProperty _autoInject;
    private SerializedProperty _parent;
    private SerializedProperty _autoRun;
    private bool _showScopeSettings;

    private void OnEnable()
    {
        _script = serializedObject.FindProperty("m_Script");
        _sceneType = serializedObject.FindProperty("_kind");
        _mainCamera = serializedObject.FindProperty("_mainCamera");
        _testSpawnPoint = serializedObject.FindProperty("_testSpawnPoint");
        _spawnPoints = serializedObject.FindProperty("_spawnPoints");
        _autoInject = serializedObject.FindProperty("autoInjectGameObjects");
        _parent = serializedObject.FindProperty("parentReference");
        _autoRun = serializedObject.FindProperty("autoRun");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(_script);

        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Scene Setup", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sceneType, SceneTypeLabel);
            EditorGUILayout.PropertyField(_mainCamera);

            if (!_sceneType.hasMultipleDifferentValues && _sceneType.intValue == (int)GameplaySceneKind.TestScene)
                EditorGUILayout.PropertyField(_testSpawnPoint, TestSpawnLabel);
        }

        EditorGUILayout.Space();

        bool collect;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Unit Spawning", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_spawnPoints, true);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                collect = GUILayout.Button("Collect Spawn Points in Scene");
        }

        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Scene Injection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_autoInject, AutoInjectLabel, true);
        }

        EditorGUILayout.Space();
        _showScopeSettings = EditorGUILayout.Foldout(_showScopeSettings, "VContainer Settings", true);

        if (_showScopeSettings)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_autoRun);
                EditorGUILayout.PropertyField(_parent, true);
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (collect)
        {
            foreach (GameplayLifetimeScope scope in targets)
                CollectSpawnPoints(scope);
        }
    }

    private static void CollectSpawnPoints(GameplayLifetimeScope scope)
    {
        var scene = scope.gameObject.scene;

        if (!scene.isLoaded)
            return;

        List<UnitSpawnPoint> found = new();

        foreach (GameObject root in scene.GetRootGameObjects())
            found.AddRange(root.GetComponentsInChildren<UnitSpawnPoint>(true));

        using SerializedObject scopeObject = new(scope);
        SerializedProperty points = scopeObject.FindProperty("_spawnPoints");
        points.arraySize = found.Count;

        for (int i = 0; i < found.Count; i++)
            points.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

        scopeObject.ApplyModifiedProperties();
    }
}
