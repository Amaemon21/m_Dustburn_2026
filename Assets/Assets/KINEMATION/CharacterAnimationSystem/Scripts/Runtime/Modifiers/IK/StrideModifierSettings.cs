// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public interface IStrideJobComponent<TJob> where TJob : struct
    {
        void UpdateJobData(TJob job, float weight);
    }

    [CreateAssetMenu(fileName = "NewStrideModifier", menuName = ModifiersMenuPath + "Stride Modifier")]
    [ScriptableComponentGroup("IK", "Stride Modifier")]
    public class StrideModifierSettings : AnimationModifierSettings, IFootWarpingModifier, IFootPinningModifier
    {
        [Unfold] public LowerBodyBones bones = LowerBodyBones.Default;
        [Tab("Warping"), Unfold] public FootWarpingData footWarping = FootWarpingData.Default;
        [Tab("Pinning"), Unfold] public FootPinningData footPinning = FootPinningData.Default;

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new StrideModifierJob();
        }

        public FootWarpingData GetFootWarpingData()
        {
            FootWarpingData data = footWarping;
            data.bones = bones;
            return data;
        }

        public FootPinningData GetFootPinningData()
        {
            FootPinningData data = footPinning;
            data.bones = bones;
            return data;
        }
    }
}