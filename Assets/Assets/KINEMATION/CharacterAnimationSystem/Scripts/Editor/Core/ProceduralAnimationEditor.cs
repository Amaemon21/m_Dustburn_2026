// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEditor;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    public class ProceduralAnimationEditor
    {
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            ProceduralAnimationComponent.onCreateAsset = CasEditorUtility.CreateAsset<ProceduralAnimationSettings>;
        }
    }
}