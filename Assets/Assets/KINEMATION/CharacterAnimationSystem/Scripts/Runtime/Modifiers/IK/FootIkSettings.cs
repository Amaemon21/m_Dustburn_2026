// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    [ScriptableComponentGroup("IK", "Foot IK")]
    public class FootIkSettings : AnimationModifierSettings
    {
        [Header("Bone References")]
        [Tooltip("The hip bone. Used to lower the body when legs are compressed.")]
        public KRigElement pelvis = new KRigElement(-1);
        
        [Tooltip("The right foot effector. This bone will be moved to match the detected ground height.")]
        [FormerlySerializedAs("rightFoot")]
        public KRigElement rightFootIk = new KRigElement(-1, CasNames.Bone_IkFootRight);
        
        [Tooltip("The left foot effector. This bone will be moved to match the detected ground height.")]
        [FormerlySerializedAs("leftFoot")]
        public KRigElement leftFootIk = new KRigElement(-1, CasNames.Bone_IkFootLeft);
        
        [Header("Trace Settings")]
        [Tooltip("Which physics layers define the ground.")]
        public LayerMask layerMask = 1;
        
        [FormerlySerializedAs("footHeight")]
        [Tooltip("Max distance to search for ground below the foot position.")]
        [Min(0f)] public float traceOffset = 0.3f;
        
        [FormerlySerializedAs("footRadius")]
        [Tooltip("The thickness of the SphereCast. Use 0 for a single-point Raycast.")]
        [Min(0f)] public float traceRadius = 0.05f;

        [Tooltip("How fast the foot matches the ground height. Use 0 for instant snapping.")]
        [Min(0f)] public float interpSpeed = 20f;
        
        [Tooltip("Interpolation speed for the root bone.")]
        [Min(0f)] public float rootInterpSpeed = 8f;
        
        [Tooltip("Optional IsGrounded binding. When bound, this value controls in-air behavior.")]
        public BindableProperty<bool> isGrounded = new (true);
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new FootIkJob();
        }
    }
}