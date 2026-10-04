// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Modify Bone Chain")]
    public class ModifyBoneChainSettings : AnimationModifierSettings
    {
        [CustomElementChainDrawer(true, false)]
        public KRigElementChain chainToModify = new KRigElementChain();
        
        public BindableProperty<Vector3> position = new (Vector3.zero);
        public BindableProperty<Quaternion> rotation = new (Quaternion.identity);
        public ESpaceType space = ESpaceType.ParentBoneSpace;
        public EModifyMode mode = EModifyMode.Add;

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new ModifyBoneChainJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            
            position.Initialize(characterPrefab);
            rotation.Initialize(characterPrefab);
        }
    }
}