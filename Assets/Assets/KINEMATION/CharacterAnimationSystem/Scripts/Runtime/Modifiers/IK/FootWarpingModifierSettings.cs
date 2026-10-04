// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public interface IFootWarpingModifier
    {
        FootWarpingData GetFootWarpingData();
    }

    [Serializable]
    public struct FootWarpingData
    {
        [HideInInspector] public LowerBodyBones bones;

        [Tooltip("Enables velocity and trajectory foot warping.")]
        public bool isEnabled;

        [Header("Velocity Warping")]
        [Tooltip("Scale applied to foot forward component in movement-vector space when alpha is 1.")]
        [Min(0f)] public float strideSmoothing;
        [Range(0f, 1f)] public float minStrideScale;

        [Header("Trajectory Warping")]
        [Range(0f, 180f)] public float maxYawAngle;
        [Min(0f)] public float feetSmoothing;

        [Header("Pelvis Leaning")]
        [Min(0f)] public float leanSmoothing;
        [Tooltip("Forward-axis pelvis roll angle = player yaw delta * this value.")]
        public float pelvisRollScale;
        [Range(0f, 1f)] public float legHipRotation;

        public static FootWarpingData Default => new FootWarpingData()
        {
            bones = LowerBodyBones.Default,
            isEnabled = true,
            strideSmoothing = 8f,
            minStrideScale = 0.3f,
            maxYawAngle = 60f,
            feetSmoothing = 8f,
            leanSmoothing = 4f,
            pelvisRollScale = 0.1f,
            legHipRotation = 0.5f
        };
    }

    [CreateAssetMenu(fileName = "NewFootWarpingModifier", menuName = ModifiersMenuPath + "Foot Warping Modifier")]
    [ScriptableComponentGroup("IK", "Foot Warping Modifier")]
    public class FootWarpingModifierSettings : AnimationModifierSettings, IFootWarpingModifier
    {
        [Unfold] public LowerBodyBones bones = LowerBodyBones.Default;
        [Unfold] public FootWarpingData footWarping = FootWarpingData.Default;

        public FootWarpingData GetFootWarpingData()
        {
            FootWarpingData data = footWarping;
            data.bones = bones;
            return data;
        }

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new FootWarpingModifierJob();
        }
    }
}