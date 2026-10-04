// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public interface IFootPinningModifier
    {
        FootPinningData GetFootPinningData();
    }

    [Serializable]
    public struct FootPinningData
    {
        [HideInInspector] public LowerBodyBones bones;

        [Tooltip("Enables foot pinning.")]
        public bool isEnabled;

        [Tooltip("Multiplier applied to the default leg length for leg IK distance clamping.")]
        [Range(0f, 1f)] public float legStretchFactor;

        [Tooltip("Maximum delta angle (degrees) between hip-axis and foot-axis before/after pinning.")]
        [Range(0f, 180f)] public float pinYawThreshold;

        [Tooltip("Maximum pitch angle (degrees) between root forward and foot forward allowed for pinning.")]
        [Range(0f, 180f)] public float angularPinThreshold;

        [Min(0f)] public float pinSmoothing;

        [Tooltip("What height is recognized as a locked position.")]
        public float pinHeightOffset;
        
        public static FootPinningData Default => new FootPinningData()
        {
            bones = LowerBodyBones.Default,
            isEnabled = true,
            legStretchFactor = 0.98f,
            pinYawThreshold = 30f,
            angularPinThreshold = 5f,
            pinSmoothing = 8f,
            pinHeightOffset = 0.01f
        };
    }

    [CreateAssetMenu(fileName = "NewFootPinningModifier", menuName = ModifiersMenuPath + "Foot Pinning Modifier")]
    [ScriptableComponentGroup("IK", "Foot Pinning Modifier")]
    public class FootPinningModifierSettings : AnimationModifierSettings, IFootPinningModifier
    {
        [Unfold] public LowerBodyBones bones = LowerBodyBones.Default;
        [Unfold] public FootPinningData footPinning = FootPinningData.Default;

        public FootPinningData GetFootPinningData()
        {
            FootPinningData data = footPinning;
            data.bones = bones;
            return data;
        }

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new FootPinningModifierJob();
        }
    }
}