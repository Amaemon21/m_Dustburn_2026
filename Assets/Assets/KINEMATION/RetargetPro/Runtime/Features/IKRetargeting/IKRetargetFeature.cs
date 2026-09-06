// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.IKRetargeting
{
    [ScriptableComponentGroup("Retargeting", "IK")]
    public class IKRetargetFeature : BasicRetargetFeature
    {
        [Header("IK Solve")]
        [Tooltip("Overall influence of the IK solve for this chain.")]
        [Range(0f, 1f)] public float ikWeight = 1f;

        [Tooltip("Whether to use IK for more than 3 bones in the chain.")]
        public bool useChainIk = false;

        [Header("Effector Offset")]
        [Tooltip("Position offset applied to the IK effector target.")]
        public Vector3 effectorOffset;
        [Tooltip("Space used for the effector offset. Component space is relative to the target model root. Bone and Parent Bone use the tip bone.")]
        public ESpaceType effectorSpace = ESpaceType.ComponentSpace;

        [Header("Pole Direction")]
        [RigAssetSelector("targetRig")]
        [Tooltip("Target-chain bone used to place and apply the pole target. Defaults to the second-to-last bone.")]
        public KRigElement jointBone = new KRigElement(-1);

        [Tooltip("Direction offset in the detected local joint basis used to place the IK pole target.")]
        public Vector3 jointOffset = Vector3.one;

        [Header("Stability")]
        [Tooltip("Controls how strongly the pole vector stabilizes the limb bend direction.")]
        [Range(0f, 1f)] public float poleWeight = 1f;
        [Tooltip("Maximum IK reach as a fraction of the chain length. Lower values help avoid full extension and visible stretch.")]
        [Range(0.1f, 1f)] public float maxReachMultiplier = 0.98f;

        public float GetReachMultiplier()
        {
            return maxReachMultiplier <= KMath.FloatMin ? 0.95f : maxReachMultiplier;
        }

        private static bool HasJointBone(KRigElement bone)
        {
            return !string.IsNullOrEmpty(bone.name) && bone.name != "None";
        }

        private int GetDefaultJointBoneIndex()
        {
            EnsureChainDefaults();

            int count = targetChain.elementChain.Count;
            return count == 0 ? -1 : Mathf.Max(0, count - 2);
        }

        public int ResolveJointBoneIndex()
        {
            int fallbackIndex = GetDefaultJointBoneIndex();
            if (fallbackIndex < 0)
            {
                jointBone = new KRigElement(-1);
                return -1;
            }

            if (HasJointBone(jointBone))
            {
                int count = targetChain.elementChain.Count;
                for (int i = 0; i < count; i++)
                {
                    KRigElement element = targetChain.elementChain[i];
                    if (element.name != jointBone.name)
                    {
                        continue;
                    }

                    jointBone = element;
                    return i;
                }
            }

            jointBone = targetChain.elementChain[fallbackIndex];
            return fallbackIndex;
        }
        
        public override RetargetFeatureState CreateFeatureState()
        {
            return new IKRetargetingState();
        }

        public override IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable)
        {
            IKRetargetJob job = new IKRetargetJob();
            playable = AnimationScriptPlayable.Create(graph, job);
            return job;
        }

#if UNITY_EDITOR
        public override void OnFeatureAdded()
        {
            ResolveJointBoneIndex();
        }

        public override void OnRigUpdated()
        {
            ResolveJointBoneIndex();
        }

        public override void MapChains()
        {
            base.MapChains();
            ResolveJointBoneIndex();
        }

        public override bool GetStatus()
        {
            ResolveJointBoneIndex();
            return base.GetStatus();
        }
#endif
    }
}