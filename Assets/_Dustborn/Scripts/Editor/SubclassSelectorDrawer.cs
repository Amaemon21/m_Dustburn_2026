using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
public sealed class SubclassSelectorDrawer : PropertyDrawer
{
    private const string NONE = "Нет";
    private const float GAP = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        Rect button = new(position.x + EditorGUIUtility.labelWidth + GAP, position.y,
            position.width - EditorGUIUtility.labelWidth - GAP, EditorGUIUtility.singleLineHeight);
        if (EditorGUI.DropdownButton(button, new GUIContent(CurrentName(property)), FocusType.Keyboard))
            ShowMenu(property);
        EditorGUI.PropertyField(position, property, label, true);
    }

    private static void ShowMenu(SerializedProperty property)
    {
        SerializedObject owner = property.serializedObject;
        string path = property.propertyPath;
        Type current = TypeOf(property.managedReferenceFullTypename);
        GenericMenu menu = new();
        menu.AddItem(new GUIContent(NONE), current == null, () => Assign(owner, path, null));
        foreach (Type type in TypeCache.GetTypesDerivedFrom(TypeOf(property.managedReferenceFieldTypename)).Where(IsAssignable).OrderBy(type => type.Name))
            menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), type == current, () => Assign(owner, path, type));
        menu.ShowAsContext();
    }

    private static void Assign(SerializedObject owner, string path, Type type)
    {
        owner.Update();
        SerializedProperty property = owner.FindProperty(path);
        property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
        property.isExpanded = type != null;
        owner.ApplyModifiedProperties();
    }

    private static string CurrentName(SerializedProperty property)
    {
        Type type = TypeOf(property.managedReferenceFullTypename);
        return type == null ? NONE : ObjectNames.NicifyVariableName(type.Name);
    }

    private static bool IsAssignable(Type type)
        => !type.IsAbstract && !type.IsGenericType && type.IsDefined(typeof(SerializableAttribute), false)
            && !typeof(UnityEngine.Object).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null;

    private static Type TypeOf(string typename)
    {
        if (string.IsNullOrEmpty(typename))
            return null;

        int split = typename.IndexOf(' ');
        return split < 0 ? null : Type.GetType($"{typename.Substring(split + 1)}, {typename.Substring(0, split)}");
    }
}
