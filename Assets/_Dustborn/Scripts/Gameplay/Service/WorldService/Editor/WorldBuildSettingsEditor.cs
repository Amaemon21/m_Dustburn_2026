using UnityEditor;
using UnityEngine;

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
