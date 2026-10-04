// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    public struct DynamicBoneData
    {
        public TransformStreamHandle source;
        public TransformStreamHandle space;
        public BlendMode updateMode;
        public KTransform spacePose;
        public bool applyBlending;
    }
    
    public enum BlendMode
    {
        Default,
        PreserveOverlay,
        FootTarget,
    }
    
    [HelpURL("https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/dynamic-bones")]
    [AddComponentMenu(CasNames.Path_ComponentMenu + "Dynamic Bone")]
    public class DynamicBone : MonoBehaviour
    {
        [Tooltip("This bone pose will be used as animated target.")]
        public Transform target;
        public BlendMode updateMode = BlendMode.Default;
        [Tooltip("If true, will be blended like any other bone.")]
        public bool applyBlending = true;
        
        public static KTransform GetDynamicBonePose(AnimationStream stream, DynamicBoneData dynamicBoneData)
        {
            if (!stream.isValid)
            {
                return KTransform.Identity;
            }

            var source = KAnimationMath.GetTransform(stream, dynamicBoneData.source);
            var space = KAnimationMath.GetTransform(stream, dynamicBoneData.space);
            return space.GetRelativeTransform(source, false);
        }
    }
}