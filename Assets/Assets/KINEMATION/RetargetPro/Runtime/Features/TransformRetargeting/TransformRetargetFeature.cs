// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.TransformRetargeting
{
    [ScriptableComponentGroup("Pose", "Transform Modifier")]
    public class TransformRetargetFeature : RetargetFeature, IDynamicRetarget
    {
        private const string DefaultChainName = "None";

        [Header("Target Chain")]
        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, true)]
        [Tooltip("Target bone chain that receives the transform override.")]
        public KRigElementChain targetChain = CreateDefaultChain();

        [Header("Position")]
        [Tooltip("Position offset or replacement applied to every bone in the selected chain.")]
        public Vector3 position = Vector3.zero;

        [Tooltip("Space used when applying position to the selected chain.")]
        public ESpaceType positionSpace = ESpaceType.BoneSpace;

        [Tooltip("How position is applied to the selected chain.")]
        public EModifyMode positionMode = EModifyMode.Ignore;

        [Header("Rotation")]
        [Tooltip("Rotation offset or replacement applied to every bone in the selected chain.")]
        public Quaternion rotation = Quaternion.identity;

        [Tooltip("Space used when applying rotation to the selected chain.")]
        public ESpaceType rotationSpace = ESpaceType.BoneSpace;

        [Tooltip("How rotation is applied to the selected chain.")]
        public EModifyMode rotationMode = EModifyMode.Ignore;

        [Header("Scale")]
        [Tooltip("Scale offset or replacement applied to every bone in the selected chain.")]
        public Vector3 scale = Vector3.one;

        [Tooltip("How local scale is applied to the selected chain.")]
        public EModifyMode scaleMode = EModifyMode.Ignore;

        public override RetargetFeatureState CreateFeatureState()
        {
            return new TransformRetargetFeatureState();
        }

        public IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable)
        {
            TransformRetargetJob job = new TransformRetargetJob();
            playable = AnimationScriptPlayable.Create(graph, job);
            return job;
        }

        private static KRigElementChain CreateDefaultChain()
        {
            return new KRigElementChain { chainName = DefaultChainName };
        }

        private static bool HasChain(KRigElementChain chain)
        {
            return chain is { elementChain: { Count: > 0 } };
        }

        private static KRigElementChain ResolveChain(KRig rig, KRigElementChain currentChain)
        {
            if (currentChain == null || rig == null)
            {
                return currentChain;
            }

            if (!string.IsNullOrEmpty(currentChain.chainName))
            {
                KRigElementChain rigChain = rig.GetElementChainByName(currentChain.chainName);
                if (rigChain != null)
                {
                    return rigChain.GetCopy();
                }
            }

            KRigElementChain resolved = new KRigElementChain
            {
                chainName = currentChain.chainName
            };

            foreach (KRigElement element in currentChain.elementChain)
            {
                if (string.IsNullOrEmpty(element.name))
                {
                    continue;
                }

                KRigElement rigElement = rig.GetElementByName(element.name);
                if (!string.IsNullOrEmpty(rigElement.name))
                {
                    resolved.elementChain.Add(rigElement);
                }
            }

            return resolved;
        }

#if UNITY_EDITOR
        public override string GetDisplayName()
        {
            string chainName = targetChain.chainName;
            if (string.IsNullOrEmpty(chainName) || chainName == DefaultChainName)
            {
                chainName = HasChain(targetChain) ? targetChain.elementChain[0].name : DefaultChainName;
            }

            return $"Transform Modifier: {chainName}";
        }

        public override void OnRigUpdated()
        {
            targetChain = ResolveChain(targetRig, targetChain);
        }

        public override void MapChains()
        {
            targetChain = ResolveChain(targetRig, targetChain);
        }

        public override bool GetStatus()
        {
            return HasChain(targetChain);
        }

        public override string GetErrorMessage()
        {
            return "Target bone chain must be assigned.";
        }
#endif
    }
}
