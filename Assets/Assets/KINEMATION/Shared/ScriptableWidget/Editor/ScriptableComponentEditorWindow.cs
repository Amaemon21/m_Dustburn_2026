// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace KINEMATION.Shared.ScriptableWidget.Editor
{
    public class ScriptableComponentEditorWindow : EditorWindow
    {
        private UnityEditor.Editor _targetEditor;
        private Vector2 _scrollPosition;

        public static ScriptableComponentEditorWindow CreateWindow()
        {
            var newWindow = GetWindow<ScriptableComponentEditorWindow>(false, "Window", true);
            return newWindow;
        }

        public UnityEditor.Editor GetEditor()
        {
            return _targetEditor;
        }

        public void RefreshEditor(UnityEditor.Editor newEditor, string windowTitle)
        {
            titleContent = new GUIContent(windowTitle);
            
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var parent = typeof(EditorWindow).GetField("m_Parent", flags)?.GetValue(this);
            var window = parent?.GetType().GetProperty("window", flags)?.GetValue(parent);
            var showMode = window?.GetType().GetField("m_ShowMode", flags)?.GetValue(window);
            
            if (!(showMode is int showModeValue) || showModeValue != 4)
            {
                var titleProperty = window?.GetType().GetProperty("title", flags);
                
                if (titleProperty != null && titleProperty.CanWrite && titleProperty.PropertyType == typeof(string))
                {
                    titleProperty.SetValue(window, windowTitle);
                }
            }
            
            _targetEditor = newEditor;
            Assert.IsNotNull(_targetEditor);
        }

        public void OnGUI()
        {
            if (_targetEditor == null)
            {
                Close();
                return;
            }
            
            if (!EditorGUIUtility.wideMode)
            {
                EditorGUIUtility.wideMode = true;
            }
            
            GUIStyle paddedStyle = new GUIStyle()
            {
                // Set the padding you want (left, right, top, bottom)
                padding = new RectOffset(15, 5, 5, 5)
            };
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, paddedStyle);
            _targetEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }
    }
}