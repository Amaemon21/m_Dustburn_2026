using UnityEditor;
using UnityEngine;

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
