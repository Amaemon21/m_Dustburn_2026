// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.ScriptableWidget.Editor;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    [CustomEditor(typeof(ProceduralAnimationSettings), true)]
    public class ProceduralAnimationSettingsEditor : UnityEditor.Editor
    {
        private static readonly string[] EnabledIconNames =
        {
            "animationvisibilitytoggleon",
            "d_animationvisibilitytoggleon"
        };

        private static readonly string[] DisabledIconNames =
        {
            "animationvisibilitytoggleoff",
            "d_animationvisibilitytoggleoff"
        };

        private static GUIStyle _gizmoIconButtonStyle;
        private static GUIStyle _eyeIconButtonStyle;

        private ProceduralAnimationSettings _settings;
        private ScriptableComponentListWidget _listWidget;

        private static GUIContent GetBuiltInIcon(string[] iconNames, string tooltip)
        {
            foreach (string iconName in iconNames)
            {
                GUIContent icon = EditorGUIUtility.IconContent(iconName);
                if (icon == null || icon.image == null) continue;

                return new GUIContent(icon.image, tooltip);
            }

            GUIContent fallbackIcon = EditorGUIUtility.IconContent("d_scenevis_visible_hover");
            if (fallbackIcon != null && fallbackIcon.image != null)
            {
                return new GUIContent(fallbackIcon.image, tooltip);
            }

            return new GUIContent(string.Empty, tooltip);
        }

        private static GUIStyle GetGizmoIconButtonStyle()
        {
            if (_gizmoIconButtonStyle != null)
            {
                return _gizmoIconButtonStyle;
            }

            _gizmoIconButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                fixedWidth = 16f,
                fixedHeight = 16f,
                imagePosition = ImagePosition.ImageOnly
            };

            return _gizmoIconButtonStyle;
        }

        private static GUIStyle GetEyeIconButtonStyle()
        {
            if (_eyeIconButtonStyle != null)
            {
                return _eyeIconButtonStyle;
            }

            _eyeIconButtonStyle = new GUIStyle(GUIStyle.none)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                fixedWidth = 16f,
                fixedHeight = 16f,
                imagePosition = ImagePosition.ImageOnly
            };

            return _eyeIconButtonStyle;
        }

        private void DrawModifierHeader(int index, SerializedProperty property, Rect rect)
        {
            AnimationModifierSettings settings = property.objectReferenceValue as AnimationModifierSettings;
            if (settings == null)
            {
                return;
            }
            
            GUIStyle iconButtonStyle = GetGizmoIconButtonStyle();
            GUIStyle eyeIconButtonStyle = GetEyeIconButtonStyle();

            // Use Mesh icon
            GUIContent icon = EditorGUIUtility.IconContent("Transform Icon");
            icon.tooltip = "Toggle to activate Gizmo for this modifier.";

            // Define icon rect
            Rect iconRect = new Rect(rect.x, rect.y + 1f, 16f, 16f);

            Color originalColor = GUI.color;
            if(!settings.showSceneGui) GUI.color = new Color(1, 1, 1, 0.4f);
            if (GUI.Button(iconRect, icon, iconButtonStyle))
            {
                Undo.RecordObject(settings, "Toggle Modifier Gizmos");
                settings.showSceneGui = !settings.showSceneGui;
                EditorUtility.SetDirty(settings);
            }
            GUI.color = originalColor;

            float spacing = 4f;
            Rect toggleRect = new Rect(iconRect.xMax + spacing, rect.y + 1f, 16f, 16f);
            bool isModifierEnabled = !Mathf.Approximately(settings.alpha, 0f);
            GUIContent toggleIcon = isModifierEnabled
                ? GetBuiltInIcon(EnabledIconNames, "Disable this modifier.")
                : GetBuiltInIcon(DisabledIconNames, "Enable this modifier.");

            Color initialColor = GUI.color;
            if (!isModifierEnabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
            }

            if (GUI.Button(toggleRect, toggleIcon, eyeIconButtonStyle))
            {
                Undo.RecordObject(settings, "Toggle Modifier");
                settings.alpha = isModifierEnabled ? 0f : 1f;
                EditorUtility.SetDirty(settings);

                if (_listWidget != null && _listWidget.IsComponentEditorActive(index))
                {
                    _listWidget.RepaintComponentEditor(index);
                }

                Repaint();
            }
            
            GUI.color = initialColor;

            float padding = 6f;
            float controlsWidth = iconRect.width + spacing + toggleRect.width + padding;
            rect.x += controlsWidth;
            rect.width -= controlsWidth;
            
            string elementName = property.objectReferenceValue.name;
            string prevElementName = elementName;
            
            elementName = EditorGUI.TextField(rect, elementName);

            if (prevElementName != elementName) property.objectReferenceValue.name = elementName;
        }
        
        private void OnEnable()
        {
            _settings = target as ProceduralAnimationSettings;
            
            _listWidget = new ScriptableComponentListWidget("Animation Modifier");
            _listWidget.Init(serializedObject, typeof(AnimationModifierSettings), "modifiers");

            _listWidget.minSize = new Vector2(470f, 400f);
            _listWidget.onComponentAdded = (int index) =>
            {
                _settings.modifiers[index].characterPrefab = _settings.characterPrefab;
            };
            _listWidget.onComponentPasted = (int index) =>
            {
                _settings.modifiers[index].characterPrefab = _settings.characterPrefab;
            };
            
            _listWidget.onDrawComponentHeader = DrawModifierHeader;
            _listWidget.editButtonText = () => "Edit Modifier";
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            base.OnInspectorGUI();
            
            _listWidget.OnGUI();
            serializedObject.ApplyModifiedProperties();
        }
    }
}