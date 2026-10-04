// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Editor.Widgets;
using UnityEditor;

namespace KINEMATION.CharacterAnimationSystem.Examples.Scripts.Editor
{
    [CustomEditor(typeof(CharacterExampleController), true)]
    public class CharacterAnimationControllerEditor : UnityEditor.Editor
    {
        private TabInspectorWidget _tabWidget;

        private void OnEnable()
        {
            _tabWidget = new TabInspectorWidget(serializedObject);
            _tabWidget.Init();
        }

        public override void OnInspectorGUI()
        {
            _tabWidget.OnGUI();
        }
    }
}