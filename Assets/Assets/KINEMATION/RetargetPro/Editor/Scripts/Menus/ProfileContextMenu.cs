// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FuzzySharp;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Editor.Scripts.Presets;
using KINEMATION.RetargetPro.Editor.Scripts.Window;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.RetargetPro.Runtime.Features.IKRetargeting;
using KINEMATION.RetargetPro.Runtime.Features.RootPelvisRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Menus
{
    public class ProfileContextMenu
    {
        private static bool TryGetSelectedModelAssets(out GameObject sourceModel, out GameObject targetModel)
        {
            sourceModel = null;
            targetModel = null;

            if (Selection.objects == null || Selection.objects.Length != 2)
            {
                return false;
            }

            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i] is not GameObject modelAsset)
                {
                    return false;
                }

                if (!IsSupportedModelAsset(modelAsset))
                {
                    return false;
                }

                if (i == 0)
                {
                    sourceModel = modelAsset;
                }
                else
                {
                    targetModel = modelAsset;
                }
            }

            return sourceModel != null && targetModel != null;
        }

        private static bool IsSupportedModelAsset(GameObject modelAsset)
        {
            if (modelAsset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(modelAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension == ".fbx" || extension == ".prefab";
        }

        [MenuItem("Assets/Create Retarget Profile", true)]
        private static bool ValidateCreateRetargetProfile()
        {
            return TryGetSelectedModelAssets(out _, out _);
        }

        [MenuItem("Assets/Create Retarget Profile")]
        private static void CreateRetargetProfile()
        {
            if (!TryGetSelectedModelAssets(out GameObject sourceModel, out GameObject targetModel))
            {
                return;
            }

            if (!RetargetProfileModelRigUtility.TryBuildRigFromModel(sourceModel, null, $"{sourceModel.name}_Rig",
                    out KRig sourceRig, out string sourceRigError))
            {
                ShowProfileCreationError($"Failed to create source rig from `{sourceModel.name}`: {sourceRigError}");
                return;
            }

            if (!RetargetProfileModelRigUtility.TryBuildRigFromModel(targetModel, null, $"{targetModel.name}_Rig",
                    out KRig targetRig, out string targetRigError))
            {
                Object.DestroyImmediate(sourceRig);
                ShowProfileCreationError($"Failed to create target rig from `{targetModel.name}`: {targetRigError}");
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceModel);
            string directory = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(directory))
            {
                directory = "Assets";
            }

            string profilePath = $"{directory}/Retarget_{sourceModel.name}_{targetModel.name}.asset";
            profilePath = AssetDatabase.GenerateUniqueAssetPath(profilePath);

            RetargetProfile profile = ScriptableObject.CreateInstance<RetargetProfile>();
            profile.sourceCharacter = sourceModel;
            profile.targetCharacter = targetModel;
            profile.sourceRig = sourceRig;
            profile.targetRig = targetRig;
            profile.retargetFeatures = new List<RetargetFeature>();

            Undo.RegisterCreatedObjectUndo(profile, "Create Retarget Profile");
            AssetDatabase.CreateAsset(profile, profilePath);

            Undo.RegisterCreatedObjectUndo(sourceRig, "Create Source Rig");
            Undo.RegisterCreatedObjectUndo(targetRig, "Create Target Rig");
            AssetDatabase.AddObjectToAsset(sourceRig, profile);
            AssetDatabase.AddObjectToAsset(targetRig, profile);

            bool mapped = RetargetProfileMappingUtility.TryRebuildProfileMappings(profile, out string mappingMessage);

            EditorUtility.SetDirty(sourceRig);
            EditorUtility.SetDirty(targetRig);
            EditorUtility.SetDirty(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = profile;
            OpenRetargetWindow(profile, mappingMessage, mapped ? MessageType.Info : MessageType.Warning);
        }

        private static bool TryBuildRigFromModel(GameObject modelAsset, string rigName, out KRig rig, out string error)
        {
            rig = null;
            error = string.Empty;

            if (modelAsset == null)
            {
                error = "Model asset is null.";
                return false;
            }

            var skinnedMeshRenderers = modelAsset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMeshRenderers == null || skinnedMeshRenderers.Length == 0)
            {
                error = "No SkinnedMeshRenderer found on model.";
                return false;
            }

            List<KRigElement> hierarchy = new List<KRigElement>();
            List<int> depths = new List<int>();
            Dictionary<Transform, int> transformToIndex = new Dictionary<Transform, int>();

            TraverseHierarchy(modelAsset.transform, 0, hierarchy, depths, transformToIndex);

            HashSet<int> skeletonBoneIndices = new HashSet<int>();
            foreach (var renderer in skinnedMeshRenderers)
            {
                Transform stopBone = renderer.rootBone != null ? renderer.rootBone : modelAsset.transform;
                if (renderer.rootBone != null)
                {
                    AddBonePath(renderer.rootBone, stopBone, modelAsset.transform, transformToIndex, skeletonBoneIndices);
                }

                var bones = renderer.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform bone = bones[i];
                    if (bone != null)
                    {
                        AddBonePath(bone, stopBone, modelAsset.transform, transformToIndex, skeletonBoneIndices);
                    }
                }
            }

            if (skeletonBoneIndices.Count == 0)
            {
                error = "SkinnedMeshRenderer bones were not found in model hierarchy.";
                return false;
            }

            List<KRigElement> skeletonBones = new List<KRigElement>();
            foreach (var element in hierarchy)
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

            rig = ScriptableObject.CreateInstance<KRig>();
            rig.name = rigName;
            rig.rigHierarchy = hierarchy;
#if UNITY_EDITOR
            rig.rigDepths = depths;
#endif
            rig.rigElementChains = GenerateChainsFromSkeletonBones(skeletonBones, modelAsset);

            Animator animator = modelAsset.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                rig.targetAnimator = animator.runtimeAnimatorController;
            }

            if (rig.rigElementChains == null || rig.rigElementChains.Count == 0)
            {
                error = "No rig chains were generated from model skeleton.";
                Object.DestroyImmediate(rig);
                rig = null;
                return false;
            }

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

            if (RetargetPresetUtility.TryMatchPreset(tempRig, out RetargetPreset preset, character))
            {
                Dictionary<RetargetChainId, KRigElementChain> chainMap =
                    RetargetPresetUtility.BuildChainMap(tempRig, preset);
                IReadOnlyList<RetargetChainId> supportedChainIds =
                    RetargetPresetUtility.GetSupportedChainIds(preset);

                foreach (RetargetChainId chainId in supportedChainIds)
                {
                    if (!chainMap.TryGetValue(chainId, out KRigElementChain chain) ||
                        chain == null || chain.elementChain.Count == 0)
                    {
                        continue;
                    }

                    result.Add(ExpandChainToFullPath(skeletonBones, chain, RetargetPresetUtility.GetChainName(chainId)));
                }
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

        private static void CreatePresetMappedProfile(RetargetProfile profile, KRig sourceRig, KRig targetRig)
        {
            var sourceChainMap = BuildStandardChainMapFromRig(sourceRig);
            var targetChainMap = BuildStandardChainMapFromRig(targetRig);

            foreach (var chainId in RetargetPresetUtility.StandardChainOrder)
            {
                if (!targetChainMap.TryGetValue(chainId, out var targetChain) || targetChain.elementChain.Count == 0)
                {
                    continue;
                }

                bool isRootOrPelvis = chainId == RetargetChainId.Root || chainId == RetargetChainId.Pelvis;
                RetargetFeature feature = ScriptableObject.CreateInstance(
                    isRootOrPelvis ? typeof(RootPelvisRetargetFeature) : typeof(IKRetargetFeature)) as RetargetFeature;

                if (feature is IKRetargetFeature ikFeature)
                {
                    ikFeature.ikWeight = 0f;
                    ikFeature.jointOffset = RetargetPresetUtility.GetDefaultIkJointOffset(chainId);
                }

                feature.name = feature.GetType().Name;
                feature.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                feature.sourceRig = sourceRig;
                feature.targetRig = targetRig;

                if (feature is BasicRetargetFeature basicFeature)
                {
                    basicFeature.sourceChain = CopyChain(
                        sourceChainMap.TryGetValue(chainId, out var sourceChain) ? sourceChain : null,
                        "None");
                    basicFeature.targetChain = CopyChain(targetChain, targetChain.chainName);

                    if (feature is RootPelvisRetargetFeature)
                    {
                        basicFeature.translationWeight = 1f;
                    }
                }

                Undo.RegisterCreatedObjectUndo(feature, "Create Retarget Feature");
                profile.retargetFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, profile);
            }
        }

        private static Dictionary<RetargetChainId, KRigElementChain> BuildStandardChainMapFromRig(KRig rig)
        {
            Dictionary<RetargetChainId, KRigElementChain> map = new Dictionary<RetargetChainId, KRigElementChain>();
            if (rig == null || rig.rigElementChains == null)
            {
                return map;
            }

            foreach (var chainId in RetargetPresetUtility.StandardChainOrder)
            {
                string chainName = RetargetPresetUtility.GetChainName(chainId);
                foreach (var chain in rig.rigElementChains)
                {
                    if (chain == null || string.IsNullOrEmpty(chain.chainName))
                    {
                        continue;
                    }

                    if (!chain.chainName.Equals(chainName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    map[chainId] = CopyChain(chain, chain.chainName);
                    break;
                }
            }

            return map;
        }

        private static void CreateFuzzyMappedProfile(RetargetProfile profile, KRig sourceRig, KRig targetRig)
        {
            List<KRigElementChain> sourceChains = new List<KRigElementChain>();
            foreach (var chain in sourceRig.rigElementChains)
            {
                sourceChains.Add(chain);
            }

            foreach (var targetChain in targetRig.rigElementChains)
            {
                int bestScore = -1;
                KRigElementChain sourceChain = null;

                foreach (var chain in sourceChains)
                {
                    string a = targetChain.chainName.Replace("mixamo", "");
                    string b = chain.chainName.Replace("mixamo", "");

                    a = a.Replace("CC_", "");
                    b = b.Replace("CC_", "");

                    a = a.Replace("Proximal", "");
                    b = b.Replace("Proximal", "");

                    a = Regex.Replace(a, @"\d", "");
                    b = Regex.Replace(b, @"\d", "");

                    int score = Fuzz.WeightedRatio(a, b) + Fuzz.PartialRatio(a, b);
                    if (score > bestScore)
                    {
                        sourceChain = chain;
                        bestScore = score;
                    }
                }

                if (bestScore < 120)
                {
                    sourceChain = null;
                }

                bool isRoot = RetargetUtility.IsNameMatching(targetChain.chainName,
                    RetargetPresetUtility.GetChainName(RetargetChainId.Root));
                bool isPelvis = RetargetUtility.IsNameMatching(targetChain.chainName,
                    RetargetPresetUtility.GetChainName(RetargetChainId.Pelvis));

                RetargetFeature feature = ScriptableObject.CreateInstance(
                    isRoot || isPelvis ? typeof(RootPelvisRetargetFeature) : typeof(IKRetargetFeature)) as RetargetFeature;

                if (feature is IKRetargetFeature ikFeature)
                {
                    ikFeature.ikWeight = 0f;
                    ikFeature.jointOffset = GetDefaultIkJointOffset(targetChain.chainName);
                }

                feature.name = feature.GetType().Name;
                feature.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                feature.sourceRig = sourceRig;
                feature.targetRig = targetRig;

                if (feature is BasicRetargetFeature basicFeature)
                {
                    basicFeature.sourceChain = CopyChain(sourceChain, "None");
                    basicFeature.targetChain = CopyChain(targetChain, targetChain.chainName);

                    if (feature is RootPelvisRetargetFeature)
                    {
                        basicFeature.translationWeight = 1f;
                    }

                    if (sourceChain != null)
                    {
                        sourceChains.Remove(sourceChain);
                    }
                }

                Undo.RegisterCreatedObjectUndo(feature, "Create Retarget Feature");
                profile.retargetFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, profile);
            }
        }

        private static Vector3 GetDefaultIkJointOffset(string chainName)
        {
            if (RetargetUtility.IsNameMatching(chainName, RetargetPresetUtility.GetChainName(RetargetChainId.RightArm)) ||
                RetargetUtility.IsNameMatching(chainName, RetargetPresetUtility.GetChainName(RetargetChainId.LeftArm)))
            {
                return RetargetPresetUtility.GetDefaultIkJointOffset(RetargetChainId.RightArm);
            }

            return Vector3.one;
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
            foreach (var element in sourceChain.elementChain)
            {
                copy.elementChain.Add(element);
            }

            return copy;
        }

        private static void ShowProfileCreationError(string message)
        {
            RetargetProWindow window = RetargetProWindow.ShowWindow(null);
            window.AddNotification(message, MessageType.Error);
            window.Focus();
        }

        private static void OpenRetargetWindow(RetargetProfile profile, string message = null,
            MessageType messageType = MessageType.Info)
        {
            if (profile == null)
            {
                return;
            }

            RetargetProWindow window = RetargetProWindow.ShowWindow(profile);
            if (messageType == MessageType.Info)
            {
                window.AddProfileMessage(message, messageType);
            }
            else
            {
                window.AddNotification(message, messageType);
            }
            window.Focus();
        }

        [OnOpenAsset]
#if UNITY_6000_5_OR_NEWER
        private static bool OpenRetargetProfileOnDoubleClick(EntityId entityId, int line)
#else
        private static bool OpenRetargetProfileOnDoubleClick(int instanceId, int line)
#endif
        {
#if UNITY_6000_5_OR_NEWER
            var asset = EditorUtility.EntityIdToObject(entityId);
#else
            var asset = EditorUtility.InstanceIDToObject(instanceId);
#endif
            if (asset is not RetargetProfile profile)
            {
                return false;
            }

            OpenRetargetWindow(profile);
            return true;
        }

        [MenuItem("Assets/Open with Retargeting Window", true)]
        private static bool ValidateOpenRetargetProfile(MenuCommand command)
        {
            if (command?.context is RetargetProfile)
            {
                return true;
            }

            return Selection.activeObject is RetargetProfile;
        }

        [MenuItem("Assets/Open with Retargeting Window")]
        private static void OpenRetargetProfile(MenuCommand command)
        {
            var profile = command?.context as RetargetProfile;
            if (profile == null)
            {
                profile = Selection.activeObject as RetargetProfile;
            }

            if (profile == null)
            {
                return;
            }

            OpenRetargetWindow(profile);
        }
    }
}
