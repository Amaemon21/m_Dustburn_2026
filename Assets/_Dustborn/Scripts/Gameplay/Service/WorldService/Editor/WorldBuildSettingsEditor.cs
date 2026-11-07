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
        EditorGUILayout.LabelField("Настройки мира редактируются в общей панели.", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Открыть генерацию мира", GUILayout.Height(32f)))
            WorldGenerationWindow.OpenSettings((WorldBuildSettings)target);
    }
}
