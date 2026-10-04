// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Stabilize Bone")]
    public class StabilizeBoneModifierSettings : AnimationModifierSettings
    {
        [Header("Bone")]
        public KRigElement boneToStabilize = new KRigElement(-1);

        [Header("Stability")]
        [Tooltip("Maximum vertical component-space deviation allowed before searching for a new stance pose.")]
        [Min(0f)] public float positionThreshold = 0.02f;
        [Tooltip("Maximum per-frame vertical component-space movement considered a stable stance pose.")]
        [Min(0f)] public float stanceThreshold = 0.005f;
        [Tooltip("Speed used to move the pivot toward a newly found stance pose.")]
        [Min(0f)] public float pivotSmoothing = 12f;

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new StabilizeBoneModifierJob();
        }
    }
}
