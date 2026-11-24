using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldBuildSettings))]
public class WorldBuildSettingsEditor : Editor
{
    [MenuItem("CONTEXT/WorldBuildSettings/Open World Generation")]
    private static void Open(MenuCommand command)
    {
        WorldGenerationWindow.OpenSettings((WorldBuildSettings)command.context);
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField(LocalizationService.Text("editor.worldWindow.sharedSettings"), EditorStyles.wordWrappedLabel);

        if (GUILayout.Button(LocalizationService.Text("editor.worldWindow.open"), GUILayout.Height(32f)))
            WorldGenerationWindow.OpenSettings((WorldBuildSettings)target);
    }
}
