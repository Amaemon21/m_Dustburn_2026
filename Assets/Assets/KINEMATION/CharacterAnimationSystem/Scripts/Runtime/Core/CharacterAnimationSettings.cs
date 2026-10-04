// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    public struct CasSettingsOverride
    {
        public AnimationClip basePose;
        public AnimationClip overlayPose;
        public RuntimeAnimatorController overlayAnimator;
    }
    
    [CreateAssetMenu(fileName = "NewAnimationSettings", menuName = "KINEMATION/CAS/CAS Settings")]
    public class CharacterAnimationSettings : ScriptableObject
    {
        [Header("Blending")]
        [Tooltip("Blending time and type.")]
        public BlendTime blendTime = new BlendTime(0.25f, 0.25f, new EaseMode(EEaseFunc.Linear));

        [Header("Layering")]
        [Tooltip("Standing idle animation.")]
        public AnimationClip basePose;
        [Tooltip("This pose will be animated dynamically.")]
        public AnimationClip overlayPose;
        [Tooltip("This controller will be animated dynamically.")]
        public RuntimeAnimatorController overlayAnimator;

        [Header("Procedural Animation")]
        public ProceduralAnimationSettings proceduralSettings;
    }
}