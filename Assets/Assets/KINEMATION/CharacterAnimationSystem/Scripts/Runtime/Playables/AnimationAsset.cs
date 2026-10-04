// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    public enum AnimationSlot
    {
        DefaultOverlay,
        Overlay,
        FullBody
    }
    
    public class AnimationAsset : ScriptableObject
    {
        [Tooltip("Target animation.")]
        public AnimationClip clip;

        [Tooltip("What bones will be animated.")]
        public AvatarMask mask;
        
        [Tooltip("Smooth blend in/out parameters.")]
        public BlendTime blendTime = new(0.2f, 0.2f, new EaseMode(EEaseFunc.Linear));
        
        [Tooltip("Animation speed multiplier.")]
        [Min(0f)] public float playRate = 1f;
        
        public bool isAdditive = false;
        public bool autoBlendOut = true;

        [Tooltip("Defines when this clip will be played.")]
        public AnimationSlot slot = AnimationSlot.FullBody;

        public float GetPlayLength()
        {
            return clip == null ? 0f : clip.length / (Mathf.Approximately(playRate, 0f) ? 1f : playRate);
        }
    }
}