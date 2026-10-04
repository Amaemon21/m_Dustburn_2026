// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Copy Bone")]
    public class CopyBoneSettings : AnimationModifierSettings
    {
        [Tooltip("The bone to copy from.")]
        public KRigElement copyFrom = new KRigElement(-1);
        [Tooltip("The bone to copy to.")]
        public KRigElement copyTo = new KRigElement(-1);
        [Tooltip("The pose will be copied relatively to this space.")]
        public ESpaceType copySpace = ESpaceType.ComponentSpace;
        
        [Tooltip("Whether to accumulate or replace current pose.")]
        public EModifyMode copyMode = EModifyMode.Replace;

        public bool copyPosition = true;
        public bool copyRotation = true;
        public bool copyScale = false;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new CopyBoneJob();
        }
    }
}