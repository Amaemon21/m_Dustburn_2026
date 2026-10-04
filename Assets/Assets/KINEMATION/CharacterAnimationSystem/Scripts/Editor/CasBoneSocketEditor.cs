// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEditor;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor
{
    [CustomEditor(typeof(CasBoneSocket))]
    public class CasBoneSocketEditor : UnityEditor.Editor
    {
        private const string HelpText = "Transforms parented to this game object won't be added to the skeleton.\n" +
                                        "Use it for parenting items or weapons (e.g., sword attachment or camera socket).";
        
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.HelpBox(HelpText, MessageType.Info);
        }
    }
}