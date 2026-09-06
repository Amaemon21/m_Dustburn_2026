// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.BoneLookups;
using KINEMATION.RetargetPro.Editor.Scripts.Presets;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.RetargetPro.Runtime.Features.IKRetargeting;
using KINEMATION.RetargetPro.Runtime.Features.RootPelvisRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Mapping
{
    public static class RetargetProfileMappingUtility
    {
        private const string SkeletonMappingFailureMessage =
            "Could not map the source/target skeleton. Add chains manually.";

        public static bool TryRebuildProfileMappings(RetargetProfile profile, out string message)
        {
            message = string.Empty;

            if (profile == null)
            {
                message = "Retarget Profile is missing.";
                return false;
            }

            if (profile.sourceRig == null || profile.targetRig == null)
            {
                message = "Assign Source and Target rigs before remapping.";
                return false;
            }

            Dictionary<RetargetChainId, KRigElementChain> sourceMap =
                RetargetPresetUtility.BuildBestChainMap(profile.sourceRig, out RetargetPreset sourcePreset,
                    out bool hasSourcePreset, true, profile.sourceCharacter);
            Dictionary<RetargetChainId, KRigElementChain> targetMap =
                RetargetPresetUtility.BuildBestChainMap(profile.targetRig, out RetargetPreset targetPreset,
                    out bool hasTargetPreset, true, profile.targetCharacter);

            if (!hasSourcePreset || !hasTargetPreset)
            {
                message = SkeletonMappingFailureMessage;
                return false;
            }

            Dictionary<RetargetChainId, RetargetBoneNameLookups> sourceLookups =
                sourcePreset.GetChainLookups();
            Dictionary<RetargetChainId, RetargetBoneNameLookups> targetLookups =
                targetPreset.GetChainLookups();

            string sourcePresetName = sourcePreset.GetName();
            string targetPresetName = targetPreset.GetName();
            IReadOnlyList<RetargetChainId> targetSupportedChainIds =
                RetargetPresetUtility.GetSupportedChainIds(targetPreset);
            if (targetSupportedChainIds.Count == 0)
            {
                message = SkeletonMappingFailureMessage;
                return false;
            }

            Undo.RecordObject(profile, "Remap Retarget Profile");

            List<RetargetFeature> preserved = new List<RetargetFeature>();
            if (profile.retargetFeatures != null)
            {
                foreach (RetargetFeature feature in profile.retargetFeatures)
                {
                    if (feature == null) continue;

                    if (feature is BasicRetargetFeature)
                    {
                        Undo.DestroyObjectImmediate(feature);
                        continue;
                    }

                    preserved.Add(feature);
                }
            }

            if (profile.retargetFeatures == null)
            {
                profile.retargetFeatures = new List<RetargetFeature>();
            }
            else
            {
                profile.retargetFeatures.Clear();
            }

            int created = 0;
            int unresolvedSourceChains = 0;
            int unresolvedTargetChains = 0;
            foreach (RetargetChainId chainId in targetSupportedChainIds)
            {
                string chainName = RetargetPresetUtility.GetChainName(chainId);

                sourceMap.TryGetValue(chainId, out KRigElementChain mappedSourceChain);
                targetMap.TryGetValue(chainId, out KRigElementChain mappedTargetChain);

                KRigElementChain sourceChain = mappedSourceChain != null
                    ? CopyChain(mappedSourceChain, chainName)
                    : ResolveChain(profile.sourceRig, chainId, chainName, sourceLookups);
                KRigElementChain targetChain = mappedTargetChain != null
                    ? CopyChain(mappedTargetChain, chainName)
                    : ResolveChain(profile.targetRig, chainId, chainName, targetLookups);

                sourceChain ??= new KRigElementChain { chainName = chainName };
                targetChain ??= new KRigElementChain { chainName = chainName };

                if (sourceChain.elementChain.Count == 0)
                {
                    unresolvedSourceChains++;
                }

                if (targetChain.elementChain.Count == 0)
                {
                    unresolvedTargetChains++;
                }

                bool useIk = IsIkChain(chainId);
                bool useRootOrPelvis = IsRootOrPelvisChain(chainId);
                RetargetFeature feature = (RetargetFeature)ScriptableObject.CreateInstance(
                    useIk ? typeof(IKRetargetFeature) :
                    useRootOrPelvis ? typeof(RootPelvisRetargetFeature) :
                    typeof(BasicRetargetFeature));

                feature.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                feature.name = feature.GetType().Name;
                feature.sourceRig = profile.sourceRig;
                feature.targetRig = profile.targetRig;

                if (feature is IKRetargetFeature ikFeature)
                {
                    ikFeature.jointOffset = RetargetPresetUtility.GetDefaultIkJointOffset(chainId);
                    ikFeature.useChainIk = targetPreset != null && targetPreset.ShouldUseChainIk(chainId);
                }

                if (feature is BasicRetargetFeature basicFeature)
                {
                    basicFeature.sourceChain = sourceChain;
                    basicFeature.targetChain = targetChain;

                    if (feature is RootPelvisRetargetFeature)
                    {
                        basicFeature.translationWeight = 1f;
                    }
                }

                Undo.RegisterCreatedObjectUndo(feature, "Create Retarget Feature");
                AssetDatabase.AddObjectToAsset(feature, profile);
                profile.retargetFeatures.Add(feature);
                created++;
            }

            foreach (RetargetFeature feature in preserved)
            {
                feature.sourceRig = profile.sourceRig;
                feature.targetRig = profile.targetRig;
                feature.OnRigUpdated();
                profile.retargetFeatures.Add(feature);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            if (created == 0)
            {
                message = $"No preset feature template was resolved. Source={sourcePresetName}, Target={targetPresetName}.";
                return false;
            }

            int preservedCount = preserved.Count;
            message = preservedCount > 0
                ? $"Recreated {created} feature(s). Source={sourcePresetName}, Target={targetPresetName}. " +
                  $"Unresolved chains: source {unresolvedSourceChains}, target {unresolvedTargetChains}. " +
                  $"Preserved {preservedCount} custom feature(s)."
                : $"Recreated {created} feature(s). Source={sourcePresetName}, Target={targetPresetName}. " +
                  $"Unresolved chains: source {unresolvedSourceChains}, target {unresolvedTargetChains}.";

            return true;
        }

        public static bool AutoMapFeature(RetargetFeature feature)
        {
            if (feature == null) return false;
            feature.MapChains();
            return true;
        }

        public static bool TryRemapProfile(RetargetProfile profile, out string message)
        {
            message = string.Empty;

            if (profile == null)
            {
                message = "Retarget Profile is missing.";
                return false;
            }

            if (profile.sourceRig == null || profile.targetRig == null)
            {
                message = "Assign Source and Target rigs before remapping.";
                return false;
            }

            if (profile.retargetFeatures == null || profile.retargetFeatures.Count == 0)
            {
                message = "No retarget features to remap.";
                return false;
            }

            Dictionary<RetargetChainId, KRigElementChain> sourceMap =
                RetargetPresetUtility.BuildBestChainMap(profile.sourceRig, out RetargetPreset sourcePreset,
                    out bool hasSourcePreset, true, profile.sourceCharacter);
            Dictionary<RetargetChainId, KRigElementChain> targetMap =
                RetargetPresetUtility.BuildBestChainMap(profile.targetRig, out RetargetPreset targetPreset,
                    out bool hasTargetPreset, true, profile.targetCharacter);

            if (!hasSourcePreset || !hasTargetPreset)
            {
                message = SkeletonMappingFailureMessage;
                return false;
            }

            Dictionary<RetargetChainId, RetargetBoneNameLookups> sourceLookups =
                sourcePreset.GetChainLookups();
            Dictionary<RetargetChainId, RetargetBoneNameLookups> targetLookups =
                targetPreset.GetChainLookups();
            string sourcePresetName = sourcePreset.GetName();
            string targetPresetName = targetPreset.GetName();

            Dictionary<string, RetargetChainId> chainIdByName =
                new Dictionary<string, RetargetChainId>(StringComparer.OrdinalIgnoreCase);
            foreach (var chainId in RetargetPresetUtility.StandardChainOrder)
            {
                chainIdByName[RetargetPresetUtility.GetChainName(chainId)] = chainId;
            }

            int presetRemapped = 0;
            int fallbackRemapped = 0;

            Undo.RecordObject(profile, "Remap Retarget Profile");

            foreach (var feature in profile.retargetFeatures)
            {
                if (feature == null) continue;

                Undo.RecordObject(feature, "Remap Retarget Feature");

                feature.sourceRig = profile.sourceRig;
                feature.targetRig = profile.targetRig;

                bool remapped = false;

                if (feature is BasicRetargetFeature basicFeature)
                {
                    string chainName = basicFeature.targetChain != null &&
                                       !string.IsNullOrEmpty(basicFeature.targetChain.chainName)
                        ? basicFeature.targetChain.chainName
                        : basicFeature.sourceChain != null
                            ? basicFeature.sourceChain.chainName
                        : string.Empty;

                    if (!string.IsNullOrEmpty(chainName) && chainIdByName.TryGetValue(chainName, out var chainId))
                    {
                        targetMap.TryGetValue(chainId, out KRigElementChain targetChain);
                        sourceMap.TryGetValue(chainId, out KRigElementChain sourceChain);

                        basicFeature.targetChain = targetChain != null
                            ? CopyChain(targetChain, chainName)
                            : ResolveChain(profile.targetRig, chainId, chainName, targetLookups);
                        basicFeature.sourceChain = sourceChain != null
                            ? CopyChain(sourceChain, chainName)
                            : ResolveChain(profile.sourceRig, chainId, chainName, sourceLookups);

                        basicFeature.targetChain ??= new KRigElementChain { chainName = chainName };
                        basicFeature.sourceChain ??= new KRigElementChain { chainName = chainName };

                        if (feature is IKRetargetFeature ikFeature &&
                            targetPreset != null &&
                            targetPreset.ShouldUseChainIk(chainId))
                        {
                            ikFeature.useChainIk = true;
                        }

                        remapped = true;
                    }
                }

                if (!remapped)
                {
                    if (AutoMapFeature(feature))
                    {
                        fallbackRemapped++;
                    }
                }
                else
                {
                    presetRemapped++;
                }

                EditorUtility.SetDirty(feature);
            }

            EditorUtility.SetDirty(profile);

            if (presetRemapped > 0 || fallbackRemapped > 0)
            {
                message = presetRemapped > 0
                    ? $"Remapped {presetRemapped} feature(s). Source={sourcePresetName}, Target={targetPresetName}."
                    : "Remapped using fallback mapping.";
                if (fallbackRemapped > 0)
                {
                    message += $" {fallbackRemapped} feature(s) used fallback mapping.";
                }

                return true;
            }

            message = "No features were remapped.";
            return false;
        }

        private static KRigElementChain CopyChain(KRigElementChain sourceChain, string fallbackName)
        {
            KRigElementChain copy = new KRigElementChain
            {
                chainName = fallbackName
            };

            if (sourceChain == null) return copy;

            copy.chainName = sourceChain.chainName;
            foreach (var element in sourceChain.elementChain)
            {
                copy.elementChain.Add(element);
            }

            return copy;
        }

        private static bool IsIkChain(RetargetChainId chainId)
        {
            return chainId == RetargetChainId.RightArm ||
                   chainId == RetargetChainId.LeftArm ||
                   chainId == RetargetChainId.RightLeg ||
                   chainId == RetargetChainId.LeftLeg;
        }

        private static bool IsRootOrPelvisChain(RetargetChainId chainId)
        {
            return chainId == RetargetChainId.Root || chainId == RetargetChainId.Pelvis;
        }

        private static KRigElementChain ResolveChain(KRig rig, RetargetChainId chainId, string chainName,
            Dictionary<RetargetChainId, RetargetBoneNameLookups> lookupsById)
        {
            if (rig == null) return null;

            KRigElementChain existing = TryGetChainByName(rig, chainName);
            if (existing != null && existing.elementChain.Count > 0)
            {
                return existing.GetCopy();
            }

            if (lookupsById != null && lookupsById.TryGetValue(chainId, out RetargetBoneNameLookups lookups) && lookups.IsValid)
            {
                KRigElementChain bestChain = RetargetBoneLookupUtility.BuildBestChain(rig, lookups, chainName);
                if (bestChain.elementChain.Count > 0)
                {
                    return bestChain;
                }
            }

            KRigElementChain fallback = FindBestChainByName(rig, chainName);
            if (fallback != null && fallback.elementChain.Count > 0)
            {
                return fallback.GetCopy();
            }

            return new KRigElementChain { chainName = chainName };
        }

        private static KRigElementChain TryGetChainByName(KRig rig, string chainName)
        {
            if (rig == null || rig.rigElementChains == null || string.IsNullOrEmpty(chainName))
            {
                return null;
            }

            foreach (KRigElementChain chain in rig.rigElementChains)
            {
                if (chain == null || string.IsNullOrEmpty(chain.chainName)) continue;
                if (chain.chainName.Equals(chainName, StringComparison.OrdinalIgnoreCase))
                {
                    return chain;
                }
            }

            return null;
        }

        private static KRigElementChain FindBestChainByName(KRig rig, string chainName)
        {
            if (rig == null || rig.rigElementChains == null || string.IsNullOrEmpty(chainName))
            {
                return null;
            }

            KRigElementChain best = null;
            int bestCount = -1;

            foreach (KRigElementChain chain in rig.rigElementChains)
            {
                if (chain == null || string.IsNullOrEmpty(chain.chainName)) continue;
                if (!RetargetUtility.IsNameMatching(chain.chainName, chainName)) continue;

                int count = chain.elementChain != null ? chain.elementChain.Count : 0;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = chain;
                }
            }

            return best;
        }
    }
}
