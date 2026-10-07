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
            new GUIContent("Приостановить до загрузки", "Скрипты управления игроком. Включаются после появления коллизии и зданий."), true);

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

        if (!GUILayout.Button("Добавить скрипт"))
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
