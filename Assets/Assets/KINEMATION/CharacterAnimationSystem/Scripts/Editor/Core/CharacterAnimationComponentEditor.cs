// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    [CustomEditor(typeof(CharacterAnimationComponent))]
    public class CharacterAnimationComponentEditor : UnityEditor.Editor
    {
        private CharacterAnimationComponent _owner;
        private AnimationAsset _animation;

        private void OnEnable()
        {
            _owner = target as CharacterAnimationComponent;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            if (!Application.isPlaying) return;
            
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            _animation = EditorGUILayout.ObjectField(_animation, typeof(AnimationAsset), false)
                as AnimationAsset;

            if (GUILayout.Button("Play Animation"))
            {
                _owner.PlayAnimation(_animation);
            }

            EditorGUILayout.EndHorizontal();
        }
        
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            CharacterAnimationComponent.onCreateAsset = CasEditorUtility.CreateAsset<CharacterAnimationSettings>;
        }
    }
}