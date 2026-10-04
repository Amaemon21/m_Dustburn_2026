// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Editor.Widgets;
using UnityEditor;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Modifiers
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AnimationModifierSettings), true)]
    public class AnimationModifierSettingsEditor : UnityEditor.Editor
    {
        private TabInspectorWidget _tabInspectorWidget;
        
        private void OnEnable()
        {
            if (target == null) return;
            
            _tabInspectorWidget = new TabInspectorWidget(serializedObject);
            _tabInspectorWidget.Init();
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            
            _tabInspectorWidget.OnGUI();
        }
    }
}