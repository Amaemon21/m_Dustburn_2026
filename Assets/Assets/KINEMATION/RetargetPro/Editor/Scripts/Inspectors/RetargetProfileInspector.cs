// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Editor.Scripts.Window;
using KINEMATION.RetargetPro.Runtime;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Inspectors
{
    [CustomEditor(typeof(RetargetProfile))]
    public class RetargetProfileInspector : UnityEditor.Editor
    {
        private RetargetProfile _retargetProfile;
        private SerializedProperty _sourceCharacterProperty;
        private SerializedProperty _targetCharacterProperty;

        private void OnEnable()
        {
            _retargetProfile = target as RetargetProfile;
            _sourceCharacterProperty = serializedObject.FindProperty(nameof(RetargetProfile.sourceCharacter));
            _targetCharacterProperty = serializedObject.FindProperty(nameof(RetargetProfile.targetCharacter));
        }

        public override void OnInspectorGUI()
        {
            if (_retargetProfile == null || _sourceCharacterProperty == null || _targetCharacterProperty == null)
            {
                base.OnInspectorGUI();
                return;
            }

            serializedObject.Update();
            EditorGUILayout.PropertyField(_sourceCharacterProperty,
                new GUIContent("Source Character", "The model to retarget FROM."), false);
            EditorGUILayout.PropertyField(_targetCharacterProperty,
                new GUIContent("Target Character", "The model to retarget TO."), false);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Open with Retarget Window"))
            {
                RetargetProWindow.ShowWindow(_retargetProfile);
            }
        }
    }
}