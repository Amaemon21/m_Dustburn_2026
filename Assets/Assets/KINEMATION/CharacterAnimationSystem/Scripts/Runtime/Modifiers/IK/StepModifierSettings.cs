// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public enum AnimatedLeadFoot
    {
        RightFoot,
        LeftFoot
    }
    
    [CreateAssetMenu(fileName = "NewStepModifier", menuName = ModifiersMenuPath + "Step Modifier")]
    [ScriptableComponentGroup("IK", "Step Modifier")]
    public class StepModifierSettings : AnimationModifierSettings
    {
        [Header("Bone References")]
        public KRigElement pelvis = new (-1);
        public KRigElement rightFootIkTarget = new (-1, CasNames.Bone_IkFootRight);
        public KRigElement leftFootIkTarget = new (-1, CasNames.Bone_IkFootLeft);
        
        [Header("Ground Alignment")]
        [Tooltip("Character's right foot bone.")]
        public KRigElement rightFoot = new KRigElement(-1);
        [Tooltip("Character's left foot bone.")]
        public KRigElement leftFoot = new KRigElement(-1);
        [Tooltip("Increase this value to reduce leg stretching.")]
        [Min(0f)] public float legStretchFactor = 0.05f;
        
        [Header("Step Motion")]
        public VectorCurve leadFootMotion = VectorCurve.Constant(0f, 1f, 0f);
        public VectorCurve followFootMotion = VectorCurve.Constant(0f, 1f, 0f);
        public VectorCurve pelvisMotion = VectorCurve.Constant(0f, 1f, 0f);

        [Tooltip("Leading foot animation scale.")]
        public Vector3 leadFootScale = Vector3.one;
        [Tooltip("Follow foot animation scale.")]
        public Vector3 followFootScale = Vector3.one;
        [Tooltip("Pelvis animation scale.")]
        public Vector3 pelvisScale = Vector3.one;
        [Min(0f)] public float playRate = 1f;
        
        [Header("Stride Settings")]
        [Tooltip("Foot making the step first.")]
        public AnimatedLeadFoot animatedLeadFoot = AnimatedLeadFoot.RightFoot;
        [Tooltip("If true, the lead foot will always be set to `animatedLeadFoot`.")]
        public bool forceLeadFoot = false;
        
        [Tooltip("If exceeded, feet and pelvis will be adjusted to avoid stretching.")]
        [Min(0f)] public float maxAllowedStride = 0.3f;
        [Tooltip("If exceeded, feet will be rotated around pelvis to avoid twists.")]
        [Range(0f, 180f)] public float maxAllowedOffsetAngle = 45f;
        [Tooltip("Delay in seconds for the next step.")]
        [Min(0f)] public float stepCooldownRate = 0f;
        
        [HideInInspector] public Vector3 totalLeadFootMotion;
        [HideInInspector] public Vector3 totalFollowFootMotion;
        [HideInInspector] public Vector3 totalPelvisMotion;

        private float ComputeTotalMotion(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0) return 0f;
            
            float prevValue = curve.keys[0].value;
            float totalMotion = 0f;

            foreach (var key in curve.keys)
            {
                totalMotion += Mathf.Abs(key.value - prevValue);
                prevValue = key.value;
            }
            
            return totalMotion;
        }

        private Vector3 ComputeTotalMotionVector(VectorCurve curve)
        {
            return new Vector3
            {
                x = ComputeTotalMotion(curve.x),
                y = ComputeTotalMotion(curve.y),
                z = ComputeTotalMotion(curve.z)
            };
        }

        private void OnEnable()
        {
            totalLeadFootMotion = ComputeTotalMotionVector(leadFootMotion);
            totalFollowFootMotion = ComputeTotalMotionVector(followFootMotion);
            totalPelvisMotion = ComputeTotalMotionVector(pelvisMotion);
        }

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new StepModifierJob();
        }
    }
}