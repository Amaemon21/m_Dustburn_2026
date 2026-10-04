// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Aim At")]
    public class AimAtModifierSettings : AnimationModifierSettings
    {
        [Header("Bones")]
        public KRigElement aimBone = new KRigElement(-1);
        public List<LookLayerElement> supportAimBones = new List<LookLayerElement>();
        
        [Header("Aim At")]
        [Tooltip("Forward axis of the aimBone.")]
        public Vector3 forwardAxis = Vector3.forward;
        [Tooltip("Max aim at distance.")]
        [Min(0f)] public float maxAimDistance = 100f;

        [Tooltip("Max pitch angle.")]
        [Range(0, 90f)] public float maxAimPitchAngle = 90f;
        [Tooltip("Max yaw angle.")]
        [Range(0, 180f)] public float maxAimYawAngle = 90f;
        [Tooltip("Aiming interpolation speed.")]
        [Min(0f)] public float aimAtSmoothing = 8f;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new AimAtModifierJob();
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            LookModifierSettings.ApplyAngleDistribution(ref supportAimBones);
        }
#endif
    }
}