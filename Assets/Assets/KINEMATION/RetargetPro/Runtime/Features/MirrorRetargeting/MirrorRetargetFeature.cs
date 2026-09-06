// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.MirrorRetargeting
{
    [ScriptableComponentGroup("Pose", "Mirror Retarget")]
    public class MirrorRetargetFeature : RetargetFeature, IDynamicRetarget
    {
        private const string DefaultChainName = "None";
        private const float AxisEpsilon = 1e-8f;

        [Header("Target Chain")]
        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, true)]
        [Tooltip("Target bone chain to mirror on the target skeleton.")]
        public KRigElementChain targetBoneChain = new KRigElementChain { chainName = DefaultChainName };

        [Header("Mirror Plane")]
        [Tooltip("Mirror plane normal expressed in target component space. Use X for left/right mirroring on a standard character.")]
        public Vector3 axis = Vector3.right;

        public override RetargetFeatureState CreateFeatureState()
        {
            return new MirrorRetargetFeatureState();
        }

        public IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable)
        {
            MirrorRetargetJob job = new MirrorRetargetJob();
            playable = AnimationScriptPlayable.Create(graph, job);
            return job;
        }

        public static Vector3 NormalizeAxis(Vector3 mirrorAxis)
        {
            return mirrorAxis.sqrMagnitude > AxisEpsilon ? mirrorAxis.normalized : Vector3.up;
        }

        public static string GetMirroredBoneName(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return string.Empty;
            }

            string mirrored = ReplaceSideToken(boneName, "Left", "Right");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideToken(boneName, "left", "right");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideToken(boneName, "_L_", "_R_");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideToken(boneName, "_l_", "_r_");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideSuffix(boneName, "_l", "_r");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideSuffix(boneName, "_L", "_R");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSidePrefix(boneName, "l_", "r_");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSidePrefix(boneName, "L_", "R_");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideSuffix(boneName, ".l", ".r");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            mirrored = ReplaceSideSuffix(boneName, ".L", ".R");
            if (!string.IsNullOrEmpty(mirrored)) return mirrored;

            return string.Empty;
        }

        private bool HasTargetChain()
        {
            return targetBoneChain is { elementChain: { Count: > 0 } };
        }

        private static string ReplaceSideToken(string value, string leftToken, string rightToken)
        {
            int index = value.IndexOf(leftToken, StringComparison.Ordinal);
            if (index >= 0)
            {
                return value.Substring(0, index) + rightToken + value.Substring(index + leftToken.Length);
            }

            index = value.IndexOf(rightToken, StringComparison.Ordinal);
            if (index >= 0)
            {
                return value.Substring(0, index) + leftToken + value.Substring(index + rightToken.Length);
            }

            return string.Empty;
        }

        private static string ReplaceSidePrefix(string value, string leftPrefix, string rightPrefix)
        {
            if (value.StartsWith(leftPrefix, StringComparison.Ordinal))
            {
                return rightPrefix + value.Substring(leftPrefix.Length);
            }

            if (value.StartsWith(rightPrefix, StringComparison.Ordinal))
            {
                return leftPrefix + value.Substring(rightPrefix.Length);
            }

            return string.Empty;
        }

        private static string ReplaceSideSuffix(string value, string leftSuffix, string rightSuffix)
        {
            if (value.EndsWith(leftSuffix, StringComparison.Ordinal))
            {
                return value.Substring(0, value.Length - leftSuffix.Length) + rightSuffix;
            }

            if (value.EndsWith(rightSuffix, StringComparison.Ordinal))
            {
                return value.Substring(0, value.Length - rightSuffix.Length) + leftSuffix;
            }

            return string.Empty;
        }

#if UNITY_EDITOR
        public override string GetDisplayName()
        {
            string chainName = targetBoneChain?.chainName;
            if (string.IsNullOrEmpty(chainName) || chainName == DefaultChainName)
            {
                chainName = HasTargetChain() ? targetBoneChain.elementChain[0].name : "None";
            }

            return $"Mirror Retarget: {chainName}";
        }

        public override void OnRigUpdated()
        {
            targetBoneChain = ResolveChain(targetRig, targetBoneChain);
        }

        public override void MapChains()
        {
            targetBoneChain = ResolveChain(targetRig, targetBoneChain);
        }

        public override bool GetStatus()
        {
            return HasTargetChain() && axis.sqrMagnitude > AxisEpsilon;
        }

        public override string GetErrorMessage()
        {
            if (!HasTargetChain())
            {
                return "Target bone chain must be assigned.";
            }

            return "Mirror axis must be non-zero.";
        }

        private static KRigElementChain ResolveChain(KRig rig, KRigElementChain currentChain)
        {
            if (currentChain == null)
            {
                return null;
            }

            if (rig == null)
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

            if (currentChain.elementChain == null)
            {
                return resolved;
            }

            foreach (KRigElement element in currentChain.elementChain)
            {
                if (string.IsNullOrEmpty(element.name)) continue;

                KRigElement rigElement = rig.GetElementByName(element.name);
                if (!string.IsNullOrEmpty(rigElement.name))
                {
                    resolved.elementChain.Add(rigElement);
                }
            }

            return resolved;
        }
#endif
    }
}
