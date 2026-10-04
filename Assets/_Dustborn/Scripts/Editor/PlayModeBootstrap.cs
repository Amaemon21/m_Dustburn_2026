using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayModeBootstrap
{
    static PlayModeBootstrap()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorSceneManager.activeSceneChangedInEditMode += (_, _) => Apply();
        EditorApplication.delayCall += Apply;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
            Apply();
    }

    private static void Apply()
    {
        if (EditorApplication.isPlaying)
            return;

        string active = SceneManager.GetActiveScene().name;
        bool inFlow = Enum.TryParse(active, out Scenes _);

        SessionState.SetString(PlayModeStartScene.SESSION_KEY, inFlow ? active : string.Empty);
        EditorSceneManager.playModeStartScene = inFlow ? FindBootScene() : null;
    }

    private static SceneAsset FindBootScene()
    {
        string boot = Scenes.Boot.ToString();

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(SceneAsset)} {boot}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetFileNameWithoutExtension(path) == boot)
                return AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }

        Debug.LogWarning($"{nameof(PlayModeBootstrap)}: no '{boot}' scene in the project, play mode starts from the open scene");
        return null;
    }
}
