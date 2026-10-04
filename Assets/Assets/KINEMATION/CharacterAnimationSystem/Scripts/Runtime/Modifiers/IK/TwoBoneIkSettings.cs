// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    [ScriptableComponentGroup("IK", "Two Bone IK")]
    public class TwoBoneIkSettings : AnimationModifierSettings
    {
        [Tooltip("The last bone in the chain (e.g. a hand or a foot).")]
        public KRigElement tip = new KRigElement(-1);

        [Tooltip("IK target bone or Game Object.")]
        public KRigElement target = new KRigElement(-1);
        [Tooltip("IK target space.")]
        public bool useWorldTarget = false;
        public BindableProperty<Transform> ikTargetTransform = new (null);
        
        [Range(0f, 1f)] public float hintWeight = 1f;
        [Tooltip("Middle bone look target (e.g. elbow or a knee).")]
        public KRigElement hintTarget = new KRigElement(-1);
        public Vector3 hintOffset = Vector3.forward;
        [Tooltip("Scales the runtime limb reach used to clamp the effector target before solving IK.")]
        [Range(0f, 1f)] public float maxLimbLengthScale = 1f;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new TwoBoneIkJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            ikTargetTransform.Initialize(characterPrefab);
        }
    }
}