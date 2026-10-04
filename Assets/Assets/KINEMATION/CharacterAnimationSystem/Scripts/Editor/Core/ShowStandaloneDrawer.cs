// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Attributes;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    [CustomPropertyDrawer(typeof(ShowStandaloneAttribute))]
    public class ShowStandaloneDrawer : PropertyDrawer
    {
        private bool _isInitialized = false;
        private bool _isStandalone;
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (_isStandalone) EditorGUI.PropertyField(position, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!_isInitialized)
            {
                string path = AssetDatabase.GetAssetPath(property.serializedObject.targetObject);
                _isStandalone = AssetDatabase.LoadAllAssetsAtPath(path).Length == 1;
                _isInitialized = true;
            }
            
            return _isStandalone ? base.GetPropertyHeight(property, label) : 0f;
        }
    }
}