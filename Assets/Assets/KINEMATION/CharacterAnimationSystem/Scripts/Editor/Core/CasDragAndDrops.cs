// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Editor.Widgets;
using UnityEditor;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    public class CharacterAnimationSettingsDragAndDrop
        : AssetDragAndDrop<CharacterAnimationComponent, CharacterAnimationSettings>
    {
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.AddDropHandlerV2(OnInspectorDrop);
            DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);
#else
            DragAndDrop.AddDropHandler(OnInspectorDrop);
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
#endif
        }
    }
    
    public class ProceduralAnimationSettingsDragAndDrop
        : AssetDragAndDrop<ProceduralAnimationComponent, ProceduralAnimationSettings>
    {
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.AddDropHandlerV2(OnInspectorDrop);
            DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);
#else
            DragAndDrop.AddDropHandler(OnInspectorDrop);
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
#endif
        }
    }
}