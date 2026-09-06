// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.Presets;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Mapping
{
    public static class RetargetProfileModelRigUtility
    {
        public static bool TryEnsureProfileRigs(RetargetProfile profile, bool remapFeatures, out string message)
        {
            message = string.Empty;

            if (profile == null)
            {
                message = "Retarget Profile is null.";
                return false;
            }

            if (profile.sourceRig != null && profile.targetRig != null)
            {
                return true;
            }

            return TryComposeProfileRigs(profile, remapFeatures, out message);
        }

        public static bool TryComposeProfileRigs(RetargetProfile profile, bool remapFeatures, out string message)
        {
            message = string.Empty;

            if (profile == null)
            {
                message = "Retarget Profile is null.";
                return false;
            }

            if (profile.sourceCharacter == null || profile.targetCharacter == null)
            {
                message = "Source and Target models must be assigned on the Retarget Profile.";
                return false;
            }

            if (!TryBuildRigFromModel(profile.sourceCharacter, profile.sourceRig,
                    $"{profile.sourceCharacter.name}_Rig", out KRig sourceRig, out string sourceError))
            {
                message = $"Failed to build source rig: {sourceError}";
                return false;
            }

            if (!TryBuildRigFromModel(profile.targetCharacter, profile.targetRig,
                    $"{profile.targetCharacter.name}_Rig", out KRig targetRig, out string targetError))
            {
                message = $"Failed to build target rig: {targetError}";
                return false;
            }

            string profilePath = AssetDatabase.GetAssetPath(profile);
            if (!string.IsNullOrEmpty(profilePath))
            {
                if (AssetDatabase.GetAssetPath(sourceRig) != profilePath)
                {
                    AssetDatabase.AddObjectToAsset(sourceRig, profile);
                }

                if (AssetDatabase.GetAssetPath(targetRig) != profilePath)
                {
                    AssetDatabase.AddObjectToAsset(targetRig, profile);
                }
            }

            profile.sourceRig = sourceRig;
            profile.targetRig = targetRig;

            if (profile.retargetFeatures != null)
            {
                foreach (RetargetFeature feature in profile.retargetFeatures)
                {
                    if (feature == null) continue;
                    feature.sourceRig = sourceRig;
                    feature.targetRig = targetRig;
                    EditorUtility.SetDirty(feature);
                }
            }

            DeleteUnusedProfileRigs(profile, sourceRig, targetRig);

            if (!remapFeatures)
            {
                profile.OnRigUpdated();
            }

            EditorUtility.SetDirty(sourceRig);
            EditorUtility.SetDirty(targetRig);
            EditorUtility.SetDirty(profile);

            string remapMessage = string.Empty;
            if (remapFeatures)
            {
                RetargetProfileMappingUtility.TryRebuildProfileMappings(profile, out remapMessage);
            }

            AssetDatabase.SaveAssets();

            if (!string.IsNullOrEmpty(remapMessage))
            {
                message = remapMessage;
            }
            else
            {
                message = "Recomposed source/target rigs from model hierarchies.";
            }

            return true;
        }

        private static void DeleteUnusedProfileRigs(RetargetProfile profile, KRig sourceRig, KRig targetRig)
        {
            if (profile == null)
            {
                return;
            }

            string profilePath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(profilePath))
            {
                return;
            }

            HashSet<KRig> activeRigs = new HashSet<KRig>();
            if (sourceRig != null && AssetDatabase.GetAssetPath(sourceRig) == profilePath)
            {
                activeRigs.Add(sourceRig);
            }

            if (targetRig != null && AssetDatabase.GetAssetPath(targetRig) == profilePath)
            {
                activeRigs.Add(targetRig);
            }

            Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(profilePath);
            foreach (Object asset in assetsAtPath)
            {
                KRig rigAsset = asset as KRig;
                if (rigAsset == null || activeRigs.Contains(rigAsset))
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(rigAsset);
            }
        }

        public static bool TryBuildRigFromModel(GameObject modelAsset, KRig existingRig, string rigName, out KRig rig,
            out string error)
        {
            rig = existingRig;
            error = string.Empty;

            if (modelAsset == null)
            {
                error = "Model asset is null.";
                return false;
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = modelAsset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            List<KRigElement> hierarchy = new List<KRigElement>();
            List<int> depths = new List<int>();
            Dictionary<Transform, int> transformToIndex = new Dictionary<Transform, int>();
            TraverseHierarchy(modelAsset.transform, 0, hierarchy, depths, transformToIndex);

            HashSet<int> skeletonBoneIndices = new HashSet<int>();
            if (skinnedMeshRenderers.Length == 0)
            {
                foreach (KRigElement element in hierarchy)
                {
                    skeletonBoneIndices.Add(element.index);
                }
            }
            else
            {
                foreach (SkinnedMeshRenderer renderer in skinnedMeshRenderers)
                {
                    Transform stopBone = renderer.rootBone != null ? renderer.rootBone : modelAsset.transform;
                    if (renderer.rootBone != null)
                    {
                        AddBonePath(renderer.rootBone, modelAsset.transform, modelAsset.transform, transformToIndex,
                            skeletonBoneIndices);
                    }

                    Transform[] bones = renderer.bones;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform bone = bones[i];
                        if (bone != null)
                        {
                            AddBonePath(bone, stopBone, modelAsset.transform, transformToIndex, skeletonBoneIndices);
                        }
                    }
                }
            }

            if (skeletonBoneIndices.Count == 0)
            {
                error = "SkinnedMeshRenderer bones were not found in model hierarchy.";
                return false;
            }

            List<KRigElement> skeletonBones = new List<KRigElement>();
            foreach (KRigElement element in hierarchy)
            {
                if (skeletonBoneIndices.Contains(element.index))
                {
                    skeletonBones.Add(new KRigElement(element.index, element.name, element.depth));
                }
            }

            if (skeletonBones.Count == 0)
            {
                error = "Unable to collect skeleton bones from SkinnedMeshRenderer components.";
                return false;
            }

            if (rig == null)
            {
                rig = ScriptableObject.CreateInstance<KRig>();
            }

            rig.name = rigName;
            rig.rigHierarchy = hierarchy;
#if UNITY_EDITOR
            rig.rigDepths = depths;
#endif
            rig.rigElementChains = GenerateChainsFromSkeletonBones(skeletonBones, modelAsset);

            Animator animator = modelAsset.GetComponentInChildren<Animator>(true);
            rig.targetAnimator = animator != null ? animator.runtimeAnimatorController : null;

            return true;
        }

        private static void AddBonePath(Transform bone, Transform stopBone, Transform modelRoot,
            Dictionary<Transform, int> transformToIndex, HashSet<int> skeletonBoneIndices)
        {
            Transform current = bone;
            while (current != null)
            {
                if (transformToIndex.TryGetValue(current, out int boneIndex))
                {
                    skeletonBoneIndices.Add(boneIndex);
                }

                if (current == stopBone || current == modelRoot)
                {
                    break;
                }

                current = current.parent;
            }
        }

        private static void TraverseHierarchy(Transform current, int depth, List<KRigElement> hierarchy,
            List<int> depths, Dictionary<Transform, int> transformToIndex)
        {
            int index = hierarchy.Count;
            hierarchy.Add(new KRigElement(index, current.name, depth));
            depths.Add(depth);
            transformToIndex[current] = index;

            foreach (Transform child in current)
            {
                TraverseHierarchy(child, depth + 1, hierarchy, depths, transformToIndex);
            }
        }

        private static List<KRigElementChain> GenerateChainsFromSkeletonBones(List<KRigElement> skeletonBones,
            GameObject character)
        {
            List<KRigElementChain> result = new List<KRigElementChain>();

            KRig tempRig = ScriptableObject.CreateInstance<KRig>();
            tempRig.rigHierarchy = skeletonBones;

            Dictionary<RetargetChainId, KRigElementChain> chainMap =
                RetargetPresetUtility.BuildBestChainMap(tempRig, out RetargetPreset resolvedPreset, out _, true,
                    character);
            IReadOnlyList<RetargetChainId> supportedChainIds = RetargetPresetUtility.GetSupportedChainIds(resolvedPreset);

            foreach (RetargetChainId chainId in supportedChainIds)
            {
                string chainName = RetargetPresetUtility.GetChainName(chainId);
                if (!chainMap.TryGetValue(chainId, out KRigElementChain chain) || chain == null)
                {
                    chain = new KRigElementChain
                    {
                        chainName = chainName
                    };
                }

                result.Add(ExpandChainToFullPath(skeletonBones, chain, chainName));
            }

            Object.DestroyImmediate(tempRig);
            return result;
        }

        private static KRigElementChain ExpandChainToFullPath(IReadOnlyList<KRigElement> skeletonBones,
            KRigElementChain sourceChain, string fallbackName)
        {
            KRigElementChain expanded = CopyChain(sourceChain, fallbackName);
            if (skeletonBones == null || skeletonBones.Count == 0 || expanded.elementChain.Count < 2)
            {
                return expanded;
            }

            Dictionary<int, int> positionByIndex = new Dictionary<int, int>(skeletonBones.Count);
            int[] parentByPosition = new int[skeletonBones.Count];
            Stack<int> parentStack = new Stack<int>();

            for (int i = 0; i < skeletonBones.Count; i++)
            {
                KRigElement element = skeletonBones[i];
                while (parentStack.Count > 0 && skeletonBones[parentStack.Peek()].depth >= element.depth)
                {
                    parentStack.Pop();
                }

                parentByPosition[i] = parentStack.Count > 0 ? parentStack.Peek() : -1;
                parentStack.Push(i);
                positionByIndex[element.index] = i;
            }

            List<KRigElement> completedChain = new List<KRigElement>(expanded.elementChain.Count);
            HashSet<int> seenIndices = new HashSet<int>();

            for (int i = 0; i < expanded.elementChain.Count; i++)
            {
                KRigElement current = expanded.elementChain[i];
                AddUniqueElement(completedChain, seenIndices, current);

                if (i == expanded.elementChain.Count - 1)
                {
                    continue;
                }

                KRigElement next = expanded.elementChain[i + 1];
                if (!positionByIndex.TryGetValue(current.index, out int currentPosition) ||
                    !positionByIndex.TryGetValue(next.index, out int nextPosition) ||
                    !IsDescendant(nextPosition, currentPosition, parentByPosition))
                {
                    continue;
                }

                int walker = parentByPosition[nextPosition];
                List<KRigElement> gapElements = new List<KRigElement>();
                while (walker >= 0 && walker != currentPosition)
                {
                    gapElements.Add(skeletonBones[walker]);
                    walker = parentByPosition[walker];
                }

                for (int gapIndex = gapElements.Count - 1; gapIndex >= 0; gapIndex--)
                {
                    AddUniqueElement(completedChain, seenIndices, gapElements[gapIndex]);
                }
            }

            expanded.elementChain.Clear();
            foreach (KRigElement element in completedChain)
            {
                expanded.elementChain.Add(element);
            }

            return expanded;
        }

        private static bool IsDescendant(int candidatePosition, int ancestorPosition, int[] parentByPosition)
        {
            int current = parentByPosition[candidatePosition];
            while (current >= 0)
            {
                if (current == ancestorPosition)
                {
                    return true;
                }

                current = parentByPosition[current];
            }

            return false;
        }

        private static void AddUniqueElement(List<KRigElement> destination, HashSet<int> seenIndices, KRigElement element)
        {
            if (!seenIndices.Add(element.index))
            {
                return;
            }

            destination.Add(element);
        }

        private static KRigElementChain CopyChain(KRigElementChain sourceChain, string fallbackName)
        {
            KRigElementChain copy = new KRigElementChain
            {
                chainName = fallbackName
            };

            if (sourceChain == null)
            {
                return copy;
            }

            copy.chainName = sourceChain.chainName;
            foreach (KRigElement element in sourceChain.elementChain)
            {
                copy.elementChain.Add(element);
            }

            return copy;
        }
    }
}
