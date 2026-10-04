// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    [CustomEditor(typeof(CharacterSkeleton))]
    public class CharacterSkeletonEditor : UnityEditor.Editor
    {
        private CharacterSkeleton _skeleton;

        private void OnEnable()
        {
            _skeleton = target as CharacterSkeleton;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            bool showButton = !Application.isPlaying && _skeleton != null;
            if (!showButton) return;
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Update Skeleton"))
            {
                _skeleton.UpdateSkeleton();
                EditorUtility.SetDirty(_skeleton);
            }
        }
    }
}