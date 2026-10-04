// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Modify Bone")]
    public class ModifyBoneSettings : AnimationModifierSettings
    {
        public KRigElement boneToModify = new KRigElement(-1);
        
        [Header("Position")]
        public BindableProperty<Vector3> position = new (Vector3.zero);
        public ESpaceType positionSpace = ESpaceType.BoneSpace;
        public EModifyMode positionMode = EModifyMode.Ignore;
        
        [Header("Rotation")]
        public BindableProperty<Quaternion> rotation = new (Quaternion.identity);
        public ESpaceType rotationSpace = ESpaceType.BoneSpace;
        public EModifyMode rotationMode = EModifyMode.Ignore;
        
        [Header("Scale")]
        public BindableProperty<Vector3> scale = new (Vector3.one);
        public EModifyMode scaleMode = EModifyMode.Ignore;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new ModifyBoneJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            
            position.Initialize(characterPrefab);
            rotation.Initialize(characterPrefab);
            scale.Initialize(characterPrefab);
        }
    }
}