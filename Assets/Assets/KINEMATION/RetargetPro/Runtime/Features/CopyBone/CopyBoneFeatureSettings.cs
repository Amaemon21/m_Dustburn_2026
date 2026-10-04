// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.CopyBone
{
    [ScriptableComponentGroup("Utility", "Copy Bone")]
    public class CopyBoneFeatureSettings : RetargetFeature
    {
        [Header("Source Bone")]
        [RigAssetSelector("sourceRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Source bone chain to copy from. The first bone in the chain is used.")]
        public KRigElementChain copyFrom;
        
        [Header("Target Bone")]
        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Target bone chain that receives the copied transform.")]
        public KRigElementChain copyTo;

        public override RetargetFeatureState CreateFeatureState()
        {
            return new CopyBoneFeatureState();
        }

#if UNITY_EDITOR
        private static bool HasChain(KRigElementChain chain)
        {
            return chain is { elementChain: { Count: > 0 } };
        }

        public override string GetDisplayName()
        {
            string fromName = "None";
            if (copyFrom is {elementChain: {Count: > 0}}) fromName = copyFrom.elementChain[0].name;

            string toName = "None";
            if (copyTo is {elementChain: {Count: > 0}}) toName = copyTo.elementChain[0].name;

            return $"Copy from: {fromName}, to: {toName}";
        }

        public override bool GetStatus()
        {
            return HasChain(copyFrom) && HasChain(copyTo) && copyFrom.elementChain.Count == copyTo.elementChain.Count;
        }

        public override string GetErrorMessage()
        {
            if (!HasChain(copyFrom) && !HasChain(copyTo))
            {
                return "Source and target bones must be assigned.";
            }

            if (!HasChain(copyFrom))
            {
                return "Source bone must be assigned.";
            }

            if (!HasChain(copyTo))
            {
                return "Target bone must be assigned.";
            }

            return "Source and target chains must contain the same number of bones.";
        }
#endif
    }
}