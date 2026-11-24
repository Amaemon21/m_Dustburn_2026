using UnityEditor;
using UnityEngine;

public partial class WorldGenerationWindow
{
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
            new GUIContent(LocalizationService.Text("editor.worldWindow.pauseBehaviours"), LocalizationService.Text("editor.worldWindow.pauseBehavioursHelp")), true);

        if (!paused.isExpanded)
            return;

        for (int index = 0; index < paused.arraySize; index++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty element = paused.GetArrayElementAtIndex(index);
                element.objectReferenceValue = EditorGUILayout.ObjectField(LocalizationService.Format("editor.worldWindow.scriptNumber", index + 1), element.objectReferenceValue, typeof(Behaviour), true);

                if (!GUILayout.Button("−", GUILayout.Width(24f)))
                    continue;

                element.objectReferenceValue = null;
                paused.DeleteArrayElementAtIndex(index);
                break;
            }
        }

        if (!GUILayout.Button(LocalizationService.Text("editor.worldWindow.addScript")))
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
        float metres = EditorGUILayout.Slider(new GUIContent(label, LocalizationService.Text("editor.worldWindow.biomeAmplitudeHelp")), value.floatValue * height, 0f, height * limit);

        if (EditorGUI.EndChangeCheck())
            value.floatValue = metres / height;
    }

    private static void Wavelength(SerializedObject config, string property, string label)
    {
        SerializedProperty value = Field(config, property);
        float worldSize = Mathf.Max(1, Field(config, "WorldSize").intValue);
        EditorGUI.BeginChangeCheck();
        float scale = EditorGUILayout.FloatField(new GUIContent(label, LocalizationService.Text("editor.worldWindow.biomeScaleHelp")), worldSize / Mathf.Max(0.1f, value.floatValue));

        if (EditorGUI.EndChangeCheck())
            value.floatValue = worldSize / Mathf.Clamp(scale, 1f, worldSize * 10f);
    }

    private static string BiomeName(BiomeType type)
    {
        return type switch
        {
            BiomeType.PineForest => LocalizationService.Text("biome.pineForest"),
            BiomeType.BurntForest => LocalizationService.Text("biome.burntForest"),
            BiomeType.Desert => LocalizationService.Text("biome.desert"),
            BiomeType.Snow => LocalizationService.Text("editor.worldWindow.biomeSnow"),
            BiomeType.Wasteland => LocalizationService.Text("biome.wasteland"),
            _ => type.ToString()
        };
    }
}
