// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.RetargetPro.Editor.Scripts.BoneLookups;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Presets
{
    public enum RetargetChainId
    {
        Root,
        Pelvis,
        RightLeg,
        LeftLeg,
        Toes,
        Spine,
        Neck,
        Tail,
        RightArm,
        LeftArm,
        IndexFingers,
        MiddleFingers,
        RingFingers,
        PinkyFingers,
        ThumbFingers,
        LeftEar,
        RightEar,
        Jaw
    }

    public struct RetargetChainDefinition
    {
        public RetargetChainId id;
        public RetargetBoneNameLookups lookups;
        public string[] exactBones;
        public bool collectAllMatches;

        public bool HasExactBones => exactBones != null && exactBones.Length > 0;
        public bool IsSupported => lookups.IsValid || HasExactBones;
    }

    public abstract class RetargetPreset
    {
        protected static readonly string[] RightPrefixes = { "right", "r_", "right_", "r." };
        protected static readonly string[] RightPostfixes = { "right", "_r", "_right", ".r" };

        protected static readonly string[] LeftPrefixes = { "left", "l_", "left_", "l." };
        protected static readonly string[] LeftPostfixes = { "left", "_l", "_left", ".l" };
        protected static readonly string[] SidePostfixes = RightPostfixes.Concat(LeftPostfixes).Distinct().ToArray();

        protected static readonly string[] GeneralAvoid = { "ik", "twist" };
        protected static readonly string[] ArmAvoid =
        {
            "ik", "twist", "finger", "index", "middle", "ring", "pinky", "thumb"
        };
        protected static readonly string[] LegAvoid = { "ik", "twist", "toe", "ball" };
        protected static readonly string[] FingerAvoid = { "ik" };

        protected static readonly string[] RootQueries = { "root", "armature", "skeleton" };
        protected static readonly string[] PelvisQueries = { "pelvis", "hips", "hip", "spine", "armature" };
        protected static readonly string[] SpineQueries = { "spine", "waist", "chest" };
        protected static readonly string[] NeckQueries = { "neck", "head" };
        protected static readonly string[] ArmQueries =
        {
            "clavicle", "shoulder", "upperarm", "arm", "forearm", "lowerarm", "elbow", "hand", "wrist"
        };
        protected static readonly string[] LegQueries =
        {
            "thigh", "upleg", "upperleg", "leg", "calf", "lowerleg", "shin", "knee", "foot", "ankle"
        };
        protected static readonly string[] ToeQueries = { "toe", "ball" };
        protected static readonly string[] IndexQueries = { "index" };
        protected static readonly string[] MiddleQueries = { "middle" };
        protected static readonly string[] RingQueries = { "ring" };
        protected static readonly string[] PinkyQueries = { "pinky", "pinkie" };
        protected static readonly string[] ThumbQueries = { "thumb" };

        private RetargetChainDefinition[] _cachedChainDefinitions;
        private Dictionary<RetargetChainId, RetargetBoneNameLookups> _cachedLookups;
        private RetargetBoneNameLookups[] _cachedSignatureLookups;
        private Dictionary<RetargetChainId, string[]> _cachedExactBoneChains;
        private RetargetChainId[] _cachedCollectAllMatchChains;

        public abstract string GetName();
        public virtual bool ShouldUseChainIk(RetargetChainId chainId) => false;

        public IReadOnlyList<RetargetChainDefinition> GetChainDefinitions()
        {
            if (_cachedChainDefinitions == null)
            {
                _cachedChainDefinitions = BuildChainDefinitions() ?? Array.Empty<RetargetChainDefinition>();
            }

            return _cachedChainDefinitions;
        }

        public Dictionary<RetargetChainId, RetargetBoneNameLookups> GetChainLookups()
        {
            EnsureChainMetadata();
            return _cachedLookups;
        }

        public RetargetBoneNameLookups[] GetSignatureLookups()
        {
            if (_cachedSignatureLookups == null)
            {
                _cachedSignatureLookups = BuildSignatureLookups() ?? Array.Empty<RetargetBoneNameLookups>();
            }

            return _cachedSignatureLookups;
        }

        public Dictionary<RetargetChainId, string[]> GetExactBoneChains()
        {
            EnsureChainMetadata();
            return _cachedExactBoneChains;
        }

        public RetargetChainId[] GetCollectAllMatchChains()
        {
            EnsureChainMetadata();
            return _cachedCollectAllMatchChains;
        }

        protected abstract RetargetChainDefinition[] BuildChainDefinitions();

        protected virtual RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return Array.Empty<RetargetBoneNameLookups>();
        }

        private void EnsureChainMetadata()
        {
            if (_cachedLookups != null && _cachedExactBoneChains != null && _cachedCollectAllMatchChains != null)
            {
                return;
            }

            _cachedLookups = new Dictionary<RetargetChainId, RetargetBoneNameLookups>();
            _cachedExactBoneChains = new Dictionary<RetargetChainId, string[]>();
            List<RetargetChainId> collectAllMatches = new List<RetargetChainId>();
            HashSet<RetargetChainId> collectAllSeen = new HashSet<RetargetChainId>();

            foreach (RetargetChainDefinition definition in GetChainDefinitions())
            {
                if (definition.lookups.IsValid)
                {
                    _cachedLookups[definition.id] = definition.lookups;
                }

                if (definition.HasExactBones)
                {
                    _cachedExactBoneChains[definition.id] = definition.exactBones;
                }

                if (definition.collectAllMatches && collectAllSeen.Add(definition.id))
                {
                    collectAllMatches.Add(definition.id);
                }
            }

            _cachedCollectAllMatchChains = collectAllMatches.ToArray();
        }

        protected static RetargetBoneNameLookups MakeLookups(string[] queries, string[] prefixes = null,
            string[] postfixes = null, string[] avoidQueries = null)
        {
            return new RetargetBoneNameLookups
            {
                queries = queries,
                prefixes = prefixes,
                postfixes = postfixes,
                avoidQueries = avoidQueries
            };
        }

        protected static RetargetBoneNameLookups MakeArmLookups(string[] prefixes, string[] postfixes)
        {
            return MakeLookups(ArmQueries, prefixes, postfixes, ArmAvoid);
        }

        protected static RetargetBoneNameLookups MakeLegLookups(string[] prefixes, string[] postfixes)
        {
            return MakeLookups(LegQueries, prefixes, postfixes, LegAvoid);
        }

        protected static RetargetBoneNameLookups MakeFingerLookups(string[] queries, string[] prefixes)
        {
            return MakeLookups(queries, prefixes, null, FingerAvoid);
        }

        protected static RetargetChainDefinition MakeChain(RetargetChainId id, RetargetBoneNameLookups lookups,
            string[] exactBones = null, bool collectAllMatches = false)
        {
            return new RetargetChainDefinition
            {
                id = id,
                lookups = lookups,
                exactBones = exactBones,
                collectAllMatches = collectAllMatches
            };
        }
    }

    public static class RetargetPresetUtility
    {
        private enum BoneSide
        {
            Unknown = 0,
            Left = 1,
            Right = 2
        }

        public static readonly RetargetChainId[] StandardChainOrder =
        {
            RetargetChainId.Root,
            RetargetChainId.Pelvis,
            RetargetChainId.RightLeg,
            RetargetChainId.LeftLeg,
            RetargetChainId.Toes,
            RetargetChainId.Spine,
            RetargetChainId.Neck,
            RetargetChainId.Jaw,
            RetargetChainId.LeftEar,
            RetargetChainId.RightEar,
            RetargetChainId.Tail,
            RetargetChainId.RightArm,
            RetargetChainId.LeftArm,
            RetargetChainId.IndexFingers,
            RetargetChainId.MiddleFingers,
            RetargetChainId.RingFingers,
            RetargetChainId.PinkyFingers,
            RetargetChainId.ThumbFingers
        };

        private static List<RetargetPreset> _cachedPresets;

        public static IReadOnlyList<RetargetPreset> GetPresets()
        {
            if (_cachedPresets != null)
            {
                return _cachedPresets;
            }

            _cachedPresets = new List<RetargetPreset>();
            var presetTypes = TypeCache.GetTypesDerivedFrom<RetargetPreset>()
                .Where(type => !type.IsAbstract)
                .ToArray();

            foreach (var type in presetTypes)
            {
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                var preset = Activator.CreateInstance(type) as RetargetPreset;
                if (preset != null)
                {
                    _cachedPresets.Add(preset);
                }
            }

            _cachedPresets = _cachedPresets
                .OrderBy(preset => preset is UnityHumanoidRetargetPreset ? 1 : 0)
                .ToList();

            return _cachedPresets;
        }

        public static IReadOnlyList<RetargetChainId> GetSupportedChainIds(RetargetPreset preset)
        {
            if (preset == null)
            {
                return Array.Empty<RetargetChainId>();
            }

            List<RetargetChainId> ordered = new List<RetargetChainId>();
            HashSet<RetargetChainId> seen = new HashSet<RetargetChainId>();
            foreach (RetargetChainDefinition definition in preset.GetChainDefinitions())
            {
                if (!definition.IsSupported || !seen.Add(definition.id))
                {
                    continue;
                }

                ordered.Add(definition.id);
            }

            return ordered;
        }

        public static Vector3 GetDefaultIkJointOffset(RetargetChainId chainId)
        {
            return chainId == RetargetChainId.RightArm || chainId == RetargetChainId.LeftArm
                ? -Vector3.forward : Vector3.forward;
        }

        public static bool TryMatchPreset(KRig rig, out RetargetPreset preset, GameObject character = null)
        {
            preset = null;
            if (rig == null || rig.rigHierarchy == null || rig.rigHierarchy.Count == 0)
            {
                return false;
            }

            foreach (RetargetPreset candidate in GetPresets())
            {
                if (!MatchesPresetSignature(rig, candidate))
                {
                    continue;
                }

                preset = candidate;
                return true;
            }

            Animator animator = character != null ? character.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }

            preset = GetPresets().FirstOrDefault(candidate => candidate is UnityHumanoidRetargetPreset);
            return preset != null;
        }

        public static Dictionary<RetargetChainId, KRigElementChain> BuildBestChainMap(KRig rig, out bool hasPreset,
            GameObject character = null)
        {
            return BuildBestChainMap(rig, out _, out hasPreset, false, character);
        }

        public static Dictionary<RetargetChainId, KRigElementChain> BuildBestChainMap(KRig rig, out RetargetPreset preset,
            out bool hasPreset, bool includeEmptyChains = false, GameObject character = null)
        {
            hasPreset = TryMatchPreset(rig, out preset, character);
            return hasPreset
                ? BuildChainMap(rig, preset, includeEmptyChains)
                : new Dictionary<RetargetChainId, KRigElementChain>();
        }

        public static Dictionary<RetargetChainId, KRigElementChain> BuildChainMap(KRig rig, RetargetPreset preset,
            bool includeEmptyChains = false)
        {
            var map = new Dictionary<RetargetChainId, KRigElementChain>();
            if (rig == null || preset == null) return map;

            var lookups = preset.GetChainLookups();
            var exactBoneChains = preset.GetExactBoneChains();
            var collectAllMatchChains = preset.GetCollectAllMatchChains();
            IReadOnlyList<RetargetChainId> supportedChainIds = GetSupportedChainIds(preset);
            foreach (RetargetChainId chainId in supportedChainIds)
            {
                string chainName = GetChainName(chainId);
                KRigElementChain chain = null;

                if (exactBoneChains != null &&
                    exactBoneChains.TryGetValue(chainId, out string[] exactBones) &&
                    exactBones != null && exactBones.Length > 0)
                {
                    chain = BuildExactChain(rig, exactBones, chainName);
                }
                else if (lookups != null &&
                         lookups.TryGetValue(chainId, out RetargetBoneNameLookups lookup) &&
                         lookup.IsValid)
                {
                    bool collectAllMatches = Array.IndexOf(collectAllMatchChains, chainId) >= 0;
                    chain = collectAllMatches
                        ? BuildMatchingChain(rig, lookup, chainName)
                        : BuildChain(rig, lookup, chainName);
                }

                chain ??= new KRigElementChain
                {
                    chainName = chainName
                };

                if (!includeEmptyChains && chain.elementChain.Count == 0) continue;

                map[chainId] = chain;
            }

            return map;
        }

        public static string GetChainName(RetargetChainId chainId)
        {
            switch (chainId)
            {
                case RetargetChainId.Root:
                    return "root";
                case RetargetChainId.Pelvis:
                    return "pelvis";
                case RetargetChainId.RightLeg:
                    return "rightLeg";
                case RetargetChainId.LeftLeg:
                    return "leftLeg";
                case RetargetChainId.Toes:
                    return "toes";
                case RetargetChainId.Spine:
                    return "spine";
                case RetargetChainId.Neck:
                    return "neck";
                case RetargetChainId.Jaw:
                    return "jaw";
                case RetargetChainId.LeftEar:
                    return "leftEar";
                case RetargetChainId.RightEar:
                    return "rightEar";
                case RetargetChainId.Tail:
                    return "tail";
                case RetargetChainId.RightArm:
                    return "rightArm";
                case RetargetChainId.LeftArm:
                    return "leftArm";
                case RetargetChainId.IndexFingers:
                    return "indexFingers";
                case RetargetChainId.MiddleFingers:
                    return "middleFingers";
                case RetargetChainId.RingFingers:
                    return "ringFingers";
                case RetargetChainId.PinkyFingers:
                    return "pinkyFingers";
                case RetargetChainId.ThumbFingers:
                    return "thumbFingers";
                default:
                    return chainId.ToString();
            }
        }

        private static bool MatchesPresetSignature(KRig rig, RetargetPreset preset)
        {
            if (rig == null || preset == null)
            {
                return false;
            }

            RetargetBoneNameLookups[] signatures = preset.GetSignatureLookups();
            if (signatures == null || signatures.Length == 0)
            {
                return false;
            }

            foreach (RetargetBoneNameLookups signature in signatures)
            {
                if (!signature.IsValid || !HasAnyBone(rig, signature))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAnyBone(KRig rig, RetargetBoneNameLookups lookups)
        {
            if (rig == null || rig.rigHierarchy == null) return false;

            foreach (var element in rig.rigHierarchy)
            {
                if (RetargetBoneLookupUtility.IsNameMatching(element.name, lookups)) return true;
            }

            return false;
        }

        private static KRigElementChain BuildChain(KRig rig, RetargetBoneNameLookups lookups, string chainName)
        {
            KRigElementChain existingChain = TryGetExistingChain(rig, chainName);
            if (rig == null || !lookups.IsValid)
            {
                return CopyChain(existingChain, chainName);
            }

            if (rig.rigHierarchy == null || rig.rigHierarchy.Count == 0)
            {
                return CopyChain(existingChain, chainName);
            }

            KRigElementChain generatedChain = RetargetBoneLookupUtility.BuildBestChain(rig, lookups, chainName);
            return SelectPreferredChain(existingChain, generatedChain, lookups, chainName);
        }

        private static KRigElementChain BuildMatchingChain(KRig rig, RetargetBoneNameLookups lookups, string chainName)
        {
            KRigElementChain chain = new KRigElementChain { chainName = chainName };
            KRigElementChain existingChain = TryGetExistingChain(rig, chainName);

            if (rig == null || !lookups.IsValid)
            {
                return CopyChain(existingChain, chainName);
            }

            if (rig.rigHierarchy == null || rig.rigHierarchy.Count == 0)
            {
                return CopyChain(existingChain, chainName);
            }

            foreach (KRigElement element in rig.rigHierarchy)
            {
                if (RetargetBoneLookupUtility.IsNameMatching(element.name, lookups))
                {
                    chain.elementChain.Add(element);
                }
            }

            KRigElementChain preferred = SelectPreferredChain(existingChain, chain, lookups, chainName);
            if (IsFingerChainName(chainName))
            {
                SortChainBySideAndNumber(preferred.elementChain);
            }

            return preferred;
        }

        private static KRigElementChain BuildExactChain(KRig rig, IReadOnlyList<string> exactBones, string chainName)
        {
            KRigElementChain chain = new KRigElementChain { chainName = chainName };

            if (rig == null || rig.rigHierarchy == null || exactBones == null || exactBones.Count == 0)
            {
                return chain;
            }

            HashSet<string> addedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddExactBones(rig, exactBones, chain, addedBones);

            if (IsFingerChainName(chainName))
            {
                for (int i = 0; i < exactBones.Count; i++)
                {
                    string mirroredBoneName = MirrorBoneNameSide(exactBones[i]);
                    if (string.IsNullOrEmpty(mirroredBoneName))
                    {
                        continue;
                    }

                    AddExactBone(rig, mirroredBoneName, chain, addedBones);
                }

                SortChainBySideAndNumber(chain.elementChain);
            }

            return chain;
        }

        private static void AddExactBones(KRig rig, IReadOnlyList<string> exactBones, KRigElementChain chain,
            HashSet<string> addedBones)
        {
            if (exactBones == null)
            {
                return;
            }

            for (int i = 0; i < exactBones.Count; i++)
            {
                AddExactBone(rig, exactBones[i], chain, addedBones);
            }
        }

        private static void AddExactBone(KRig rig, string boneName, KRigElementChain chain, HashSet<string> addedBones)
        {
            if (string.IsNullOrEmpty(boneName) || rig == null || chain == null)
            {
                return;
            }

            if (addedBones != null && !addedBones.Add(boneName))
            {
                return;
            }

            if (TryFindElementByExactName(rig, boneName, out KRigElement element))
            {
                chain.elementChain.Add(element);
            }
        }

        private static bool TryFindElementByExactName(KRig rig, string boneName, out KRigElement element)
        {
            element = default;
            if (rig == null || rig.rigHierarchy == null || string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            foreach (KRigElement candidate in rig.rigHierarchy)
            {
                if (string.IsNullOrEmpty(candidate.name))
                {
                    continue;
                }

                if (!candidate.name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                element = candidate;
                return true;
            }

            return false;
        }

        private static KRigElementChain TryGetExistingChain(KRig rig, string chainName)
        {
            if (rig == null || rig.rigElementChains == null) return null;

            if (!string.IsNullOrEmpty(chainName))
            {
                KRigElementChain directMatch = rig.GetElementChainByName(chainName);
                if (directMatch != null) return directMatch;

                foreach (var chain in rig.rigElementChains)
                {
                    if (chain == null || string.IsNullOrEmpty(chain.chainName)) continue;
                    if (chain.chainName.Equals(chainName, StringComparison.OrdinalIgnoreCase))
                    {
                        return chain;
                    }
                }
            }

            return null;
        }

        private static KRigElementChain SelectPreferredChain(KRigElementChain existingChain, KRigElementChain generatedChain,
            RetargetBoneNameLookups lookups, string chainName)
        {
            bool hasExisting = existingChain != null && existingChain.elementChain.Count > 0;
            bool hasGenerated = generatedChain != null && generatedChain.elementChain.Count > 0;

            if (!hasExisting && !hasGenerated)
            {
                return new KRigElementChain { chainName = chainName };
            }

            if (!hasExisting)
            {
                return CopyChain(generatedChain, chainName);
            }

            if (!hasGenerated)
            {
                return CopyChain(existingChain, chainName);
            }

            int existingMatchCount = CountMatchingElements(existingChain, lookups);
            int generatedMatchCount = CountMatchingElements(generatedChain, lookups);

            if (generatedMatchCount > existingMatchCount ||
                (generatedMatchCount == existingMatchCount &&
                 generatedChain.elementChain.Count > existingChain.elementChain.Count))
            {
                return CopyChain(generatedChain, chainName);
            }

            return CopyChain(existingChain, chainName);
        }

        private static int CountMatchingElements(KRigElementChain chain, RetargetBoneNameLookups lookups)
        {
            if (chain == null || chain.elementChain == null || !lookups.IsValid)
            {
                return 0;
            }

            int count = 0;
            foreach (KRigElement element in chain.elementChain)
            {
                if (RetargetBoneLookupUtility.IsNameMatching(element.name, lookups))
                {
                    count++;
                }
            }

            return count;
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

            if (!string.IsNullOrEmpty(sourceChain.chainName))
            {
                copy.chainName = sourceChain.chainName;
            }

            if (sourceChain.elementChain == null)
            {
                return copy;
            }

            foreach (KRigElement element in sourceChain.elementChain)
            {
                copy.elementChain.Add(element);
            }

            return copy;
        }

        private static bool IsFingerChainName(string chainName)
        {
            return !string.IsNullOrEmpty(chainName) &&
                   chainName.EndsWith("Fingers", StringComparison.OrdinalIgnoreCase);
        }

        private static void SortChainBySideAndNumber(List<KRigElement> elements)
        {
            if (elements == null || elements.Count <= 1)
            {
                return;
            }

            bool hasMirroredNumericSuffix = elements.Exists(element => HasMirroredNumericSuffix(element.name));
            List<(BoneSide side, int number, int index, KRigElement element)> sortable =
                new List<(BoneSide, int, int, KRigElement)>(elements.Count);

            for (int i = 0; i < elements.Count; i++)
            {
                KRigElement element = elements[i];
                sortable.Add((GetBoneSide(element.name, hasMirroredNumericSuffix), GetBoneNumber(element.name), i,
                    element));
            }

            sortable.Sort((a, b) =>
            {
                int sideCompare = GetBoneSideSortOrder(a.side).CompareTo(GetBoneSideSortOrder(b.side));
                if (sideCompare != 0)
                {
                    return sideCompare;
                }

                int numberCompare = a.number.CompareTo(b.number);
                if (numberCompare != 0)
                {
                    return numberCompare;
                }

                return a.index.CompareTo(b.index);
            });

            for (int i = 0; i < sortable.Count; i++)
            {
                elements[i] = sortable[i].element;
            }
        }

        private static bool HasMirroredNumericSuffix(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.EndsWith("_1", StringComparison.Ordinal))
            {
                return false;
            }

            int suffixStart = name.Length - 2;
            for (int i = suffixStart - 1; i >= 0; i--)
            {
                if (char.IsDigit(name[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetBoneNumber(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return int.MaxValue;
            }

            int end = name.Length - 1;
            if (HasMirroredNumericSuffix(name))
            {
                end -= 2;
            }

            while (end >= 0 && !char.IsDigit(name[end]))
            {
                end--;
            }

            if (end < 0)
            {
                return int.MaxValue;
            }

            int start = end;
            while (start >= 0 && char.IsDigit(name[start]))
            {
                start--;
            }

            return int.TryParse(name.Substring(start + 1, end - start), out int number) ? number : int.MaxValue;
        }

        private static BoneSide GetBoneSide(string name, bool inferMirroredNumericSuffix = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return BoneSide.Unknown;
            }

            string lower = name.ToLowerInvariant();
            if (lower.Contains("left") || lower.Contains("_l_") || lower.EndsWith("_l") || lower.StartsWith("l_") ||
                lower.EndsWith(".l") || lower.EndsWith("-l") || lower.Contains(":l_"))
            {
                return BoneSide.Left;
            }

            if (lower.Contains("right") || lower.Contains("_r_") || lower.EndsWith("_r") || lower.StartsWith("r_") ||
                lower.EndsWith(".r") || lower.EndsWith("-r") || lower.Contains(":r_"))
            {
                return BoneSide.Right;
            }

            if (inferMirroredNumericSuffix)
            {
                return HasMirroredNumericSuffix(name) ? BoneSide.Right : BoneSide.Left;
            }

            return BoneSide.Unknown;
        }

        private static int GetBoneSideSortOrder(BoneSide side)
        {
            switch (side)
            {
                case BoneSide.Left:
                    return 0;
                case BoneSide.Right:
                    return 1;
                default:
                    return 2;
            }
        }

        private static string MirrorBoneNameSide(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return string.Empty;
            }

            string mirrored = ReplaceSideToken(boneName, "Left", "Right");
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
    }

    public class MixamoRetargetPreset : RetargetPreset
    {
        private const string MixamoNamespace = "mixamorig:";
        private const string IndexedMixamoNamespace = "mixamorig1:";

        private static readonly string[] MixamoPrefix = { MixamoNamespace, IndexedMixamoNamespace };
        private static readonly string[] MixamoRightPrefix =
        {
            MixamoNamespace + "right", IndexedMixamoNamespace + "right"
        };
        private static readonly string[] MixamoLeftPrefix =
        {
            MixamoNamespace + "left", IndexedMixamoNamespace + "left"
        };

        public override string GetName()
        {
            return "Mixamo";
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            string[] mixamoRootQueries = { "root" };
            string[] mixamoPelvisQueries = { "hips" };
            string[] mixamoLegQueries = { "upleg", "leg", "foot" };
            string[] mixamoToeQueries = { "toebase" };
            string[] mixamoSpineQueries = { "spine", "spine1", "spine2" };
            string[] mixamoNeckQueries = { "neck", "head" };
            string[] mixamoArmQueries = { "shoulder", "arm", "forearm", "hand" };
            string[] mixamoIndexQueries = { "handindex" };
            string[] mixamoMiddleQueries = { "handmiddle" };
            string[] mixamoRingQueries = { "handring" };
            string[] mixamoPinkyQueries = { "handpinky" };
            string[] mixamoThumbQueries = { "handthumb" };

            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(mixamoRootQueries, MixamoPrefix, null, GeneralAvoid),
                    MakeExactBones("Root")),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(mixamoPelvisQueries, MixamoPrefix, null, GeneralAvoid),
                    MakeExactBones("Hips")),
                MakeChain(RetargetChainId.RightLeg, MakeLookups(mixamoLegQueries, MixamoRightPrefix, null, LegAvoid),
                    MakeExactBones("RightUpLeg", "RightLeg", "RightFoot")),
                MakeChain(RetargetChainId.LeftLeg, MakeLookups(mixamoLegQueries, MixamoLeftPrefix, null, LegAvoid),
                    MakeExactBones("LeftUpLeg", "LeftLeg", "LeftFoot")),
                MakeChain(RetargetChainId.Toes, MakeLookups(mixamoToeQueries, MixamoPrefix, null, GeneralAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Spine, MakeLookups(mixamoSpineQueries, MixamoPrefix, null, GeneralAvoid),
                    MakeExactBones("Spine", "Spine1", "Spine2")),
                MakeChain(RetargetChainId.Neck, MakeLookups(mixamoNeckQueries, MixamoPrefix, null, GeneralAvoid),
                    MakeExactBones("Neck", "Head")),
                MakeChain(RetargetChainId.RightArm, MakeLookups(mixamoArmQueries, MixamoRightPrefix, null, ArmAvoid),
                    MakeExactBones("RightShoulder", "RightArm", "RightForeArm", "RightHand")),
                MakeChain(RetargetChainId.LeftArm, MakeLookups(mixamoArmQueries, MixamoLeftPrefix, null, ArmAvoid),
                    MakeExactBones("LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand")),
                MakeChain(RetargetChainId.IndexFingers, MakeLookups(mixamoIndexQueries, MixamoPrefix, null, FingerAvoid),
                    MakeExactBones("LeftHandIndex1", "LeftHandIndex2", "LeftHandIndex3")),
                MakeChain(RetargetChainId.MiddleFingers, MakeLookups(mixamoMiddleQueries, MixamoPrefix, null, FingerAvoid),
                    MakeExactBones("LeftHandMiddle1", "LeftHandMiddle2", "LeftHandMiddle3")),
                MakeChain(RetargetChainId.RingFingers, MakeLookups(mixamoRingQueries, MixamoPrefix, null, FingerAvoid),
                    MakeExactBones("LeftHandRing1", "LeftHandRing2", "LeftHandRing3")),
                MakeChain(RetargetChainId.PinkyFingers, MakeLookups(mixamoPinkyQueries, MixamoPrefix, null, FingerAvoid),
                    MakeExactBones("LeftHandPinky1", "LeftHandPinky2", "LeftHandPinky3")),
                MakeChain(RetargetChainId.ThumbFingers, MakeLookups(mixamoThumbQueries, MixamoPrefix, null, FingerAvoid),
                    MakeExactBones("LeftHandThumb1", "LeftHandThumb2", "LeftHandThumb3"))
            };
        }

        private static string[] MakeExactBones(params string[] boneNames)
        {
            string[] exactBones = new string[boneNames.Length * 2];
            for (int i = 0; i < boneNames.Length; i++)
            {
                exactBones[i] = MixamoNamespace + boneNames[i];
                exactBones[boneNames.Length + i] = IndexedMixamoNamespace + boneNames[i];
            }

            return exactBones;
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(new[] { "hips" }, MixamoPrefix, null, GeneralAvoid),
                MakeLookups(new[] { "spine" }, MixamoPrefix, null, GeneralAvoid),
                MakeLookups(new[] { "arm" }, MixamoLeftPrefix, null, ArmAvoid),
                MakeLookups(new[] { "upleg", "leg" }, MixamoLeftPrefix, null, LegAvoid)
            };
        }

    }

    public class SyntyRetargetPreset : RetargetPreset
    {
        public override string GetName()
        {
            return "Synty";
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            string[] syntyRootQueries = { "root" };
            string[] syntyPelvisQueries = { "pelvis", "hips", "hip" };
            string[] syntyLegQueries = { "thigh", "upperleg", "leg", "calf", "lowerleg", "ankle", "foot" };
            string[] syntyToeQueries = { "ball", "toebase", "toe" };
            string[] syntySpineQueries = { "spine_01", "spine_02", "spine_03" };
            string[] syntyNeckQueries = { "neck_01", "neck", "head" };
            string[] syntyArmQueries = { "clavicle", "shoulder", "upperarm", "lowerarm", "elbow", "hand" };
            string[] syntyIndexQueries =
            {
                "index_01", "index_02", "index_03", "indexfinger"
            };
            string[] syntyMiddleQueries =
            {
                "finger_01", "finger_02", "finger_03", "finger_04"
            };
            string[] syntyMiddleAvoid = { "ik", "index", "thumb", "ring", "pinky", "pinkie" };
            string[] syntyRingQueries =
            {
                "ring_01", "ring_02", "ring_03"
            };
            string[] syntyPinkyQueries =
            {
                "pinky_01", "pinky_02", "pinky_03"
            };
            string[] syntyThumbQueries =
            {
                "thumb_01", "thumb_02", "thumb_03"
            };

            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(syntyRootQueries, null, null, GeneralAvoid),
                    new[] { "root" }),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(syntyPelvisQueries, null, null, GeneralAvoid),
                    new[] { "pelvis", "Hips" }),
                MakeChain(RetargetChainId.RightLeg, MakeLookups(syntyLegQueries, null, RightPostfixes, LegAvoid),
                    new[] { "thigh_r", "UpperLeg_R", "calf_r", "LowerLeg_R", "foot_r", "Ankle_R" }),
                MakeChain(RetargetChainId.LeftLeg, MakeLookups(syntyLegQueries, null, LeftPostfixes, LegAvoid),
                    new[] { "thigh_l", "UpperLeg_L", "calf_l", "LowerLeg_L", "foot_l", "Ankle_L" }),
                MakeChain(RetargetChainId.Toes, MakeLookups(syntyToeQueries, null, SidePostfixes, GeneralAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Spine, MakeLookups(syntySpineQueries, null, null, GeneralAvoid),
                    new[] { "spine_01", "spine_02", "spine_03" }),
                MakeChain(RetargetChainId.Neck, MakeLookups(syntyNeckQueries, null, null, GeneralAvoid),
                    new[] { "neck_01", "Neck", "head" }),
                MakeChain(RetargetChainId.RightArm, MakeLookups(syntyArmQueries, null, RightPostfixes, ArmAvoid),
                    new[] { "clavicle_r", "upperarm_r", "Shoulder_R", "lowerarm_r", "Elbow_R", "hand_r" }),
                MakeChain(RetargetChainId.LeftArm, MakeLookups(syntyArmQueries, null, LeftPostfixes, ArmAvoid),
                    new[] { "clavicle_l", "upperarm_l", "Shoulder_L", "lowerarm_l", "Elbow_L", "hand_l" }),
                MakeChain(RetargetChainId.IndexFingers, MakeLookups(syntyIndexQueries, null, null, FingerAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.MiddleFingers, MakeLookups(syntyMiddleQueries, null, null, syntyMiddleAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.RingFingers, MakeLookups(syntyRingQueries, null, null, FingerAvoid),
                    new[] { "ring_01_l", "ring_02_l", "ring_03_l" }),
                MakeChain(RetargetChainId.PinkyFingers, MakeLookups(syntyPinkyQueries, null, null, FingerAvoid),
                    new[] { "pinky_01_l", "pinky_02_l", "pinky_03_l" }),
                MakeChain(RetargetChainId.ThumbFingers, MakeLookups(syntyThumbQueries, null, null, FingerAvoid),
                    collectAllMatches: true)
            };
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(new[] { "finger_01" , "finger_01_1", "thumb_01", "thumb_01_1"}),
                MakeLookups(new[] { "prop"}, null, new[] { "_l", "_r"}),
                MakeLookups(new[] { "upperleg", "upperleg" }, null, new[] { "_l", "_r"}),
                MakeLookups(new[] { "ball", "toes", "ankle" }, null, new[] { "_l", "_r"})
            };
        }

    }

    public class CharacterCreatorRetargetPreset : RetargetPreset
    {
        private static readonly string[] CcPrefix = { "cc_base_" };
        private static readonly string[] CcRightPrefix = { "cc_base_r_" };
        private static readonly string[] CcLeftPrefix = { "cc_base_l_" };

        public override string GetName()
        {
            return "Character Creator (CC4, CC5)";
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            string[] ccRootQueries = { "boneroot" };
            string[] ccPelvisQueries = { "hip", "pelvis" };
            string[] ccLegQueries = { "thigh", "calf", "foot" };
            string[] ccToeQueries = { "toebase" };
            string[] ccSpineQueries =
            {
                "waist", "spine01", "spine02", "spine03"
            };
            string[] ccNeckQueries = { "necktwist01", "head" };
            string[] ccNeckAvoid = { "ik" };
            string[] ccArmQueries = { "clavicle", "upperarm", "forearm", "hand" };
            string[] ccIndexQueries =
            {
                "index1", "index2", "index3"
            };
            string[] ccMiddleQueries =
            {
                "mid1", "mid2", "mid3"
            };
            string[] ccRingQueries =
            {
                "ring1", "ring2", "ring3"
            };
            string[] ccPinkyQueries =
            {
                "pinky1", "pinky2", "pinky3"
            };
            string[] ccThumbQueries =
            {
                "thumb1", "thumb2", "thumb3"
            };

            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(ccRootQueries, CcPrefix, null, GeneralAvoid),
                    new[] { "CC_Base_BoneRoot" }),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(ccPelvisQueries, CcPrefix, null, GeneralAvoid),
                    new[] { "CC_Base_Hip" }),
                MakeChain(RetargetChainId.RightLeg, MakeLookups(ccLegQueries, CcRightPrefix, null, LegAvoid),
                    new[] { "CC_Base_R_Thigh", "CC_Base_R_Calf", "CC_Base_R_Foot" }),
                MakeChain(RetargetChainId.LeftLeg, MakeLookups(ccLegQueries, CcLeftPrefix, null, LegAvoid),
                    new[] { "CC_Base_L_Thigh", "CC_Base_L_Calf", "CC_Base_L_Foot" }),
                MakeChain(RetargetChainId.Toes, MakeLookups(ccToeQueries, CcPrefix, null, GeneralAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Spine, MakeLookups(ccSpineQueries, CcPrefix, null, GeneralAvoid),
                    new[] { "CC_Base_Waist", "CC_Base_Spine01", "CC_Base_Spine02", "CC_Base_Spine03" }),
                MakeChain(RetargetChainId.Neck, MakeLookups(ccNeckQueries, CcPrefix, null, ccNeckAvoid),
                    new[] { "CC_Base_NeckTwist01", "CC_Base_Head" }),
                MakeChain(RetargetChainId.RightArm, MakeLookups(ccArmQueries, CcRightPrefix, null, ArmAvoid),
                    new[] { "CC_Base_R_Clavicle", "CC_Base_R_UpperArm", "CC_Base_R_Forearm", "CC_Base_R_Hand" }),
                MakeChain(RetargetChainId.LeftArm, MakeLookups(ccArmQueries, CcLeftPrefix, null, ArmAvoid),
                    new[] { "CC_Base_L_Clavicle", "CC_Base_L_UpperArm", "CC_Base_L_Forearm", "CC_Base_L_Hand" }),
                MakeChain(RetargetChainId.IndexFingers, MakeLookups(ccIndexQueries, CcPrefix, null, FingerAvoid),
                    new[] { "CC_Base_L_Index1", "CC_Base_L_Index2", "CC_Base_L_Index3" }),
                MakeChain(RetargetChainId.MiddleFingers, MakeLookups(ccMiddleQueries, CcPrefix, null, FingerAvoid),
                    new[] { "CC_Base_L_Mid1", "CC_Base_L_Mid2", "CC_Base_L_Mid3" }),
                MakeChain(RetargetChainId.RingFingers, MakeLookups(ccRingQueries, CcPrefix, null, FingerAvoid),
                    new[] { "CC_Base_L_Ring1", "CC_Base_L_Ring2", "CC_Base_L_Ring3" }),
                MakeChain(RetargetChainId.PinkyFingers, MakeLookups(ccPinkyQueries, CcPrefix, null, FingerAvoid),
                    new[] { "CC_Base_L_Pinky1", "CC_Base_L_Pinky2", "CC_Base_L_Pinky3" }),
                MakeChain(RetargetChainId.ThumbFingers, MakeLookups(ccThumbQueries, CcPrefix, null, FingerAvoid),
                    new[] { "CC_Base_L_Thumb1", "CC_Base_L_Thumb2", "CC_Base_L_Thumb3" })
            };
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(new[] { "boneroot" }, CcPrefix, null, GeneralAvoid),
                MakeLookups(new[] { "hip", "pelvis" }, CcPrefix, null, GeneralAvoid),
                MakeLookups(new[] { "waist" }, CcPrefix, null, GeneralAvoid),
                MakeLookups(new[] { "upperarm" }, CcLeftPrefix, null, ArmAvoid),
                MakeLookups(new[] { "thigh" }, CcLeftPrefix, null, LegAvoid)
            };
        }

    }

    public class UnrealRetargetPreset : RetargetPreset
    {
        public override string GetName()
        {
            return "Unreal Engine 4/5";
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            string[] ueRootQueries = { "root" };
            string[] uePelvisQueries = { "pelvis" };
            string[] ueLegQueries = { "thigh", "calf", "foot" };
            string[] ueToeQueries = { "ball" };
            string[] ueSpineQueries = { "spine_01", "spine_02", "spine_03", "spine_04", "spine_05" };
            string[] ueNeckQueries = { "neck_01", "head" };
            string[] ueArmQueries = { "clavicle", "upperarm", "lowerarm", "hand" };
            string[] ueIndexQueries =
            {
                "index_01", "index_02", "index_03"
            };
            string[] ueMiddleQueries =
            {
                "middle_01", "middle_02", "middle_03"
            };
            string[] ueRingQueries =
            {
                "ring_01", "ring_02", "ring_03"
            };
            string[] uePinkyQueries =
            {
                "pinky_01", "pinky_02", "pinky_03"
            };
            string[] ueThumbQueries =
            {
                "thumb_01", "thumb_02", "thumb_03"
            };

            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(ueRootQueries, null, null, GeneralAvoid),
                    new[] { "root" }),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(uePelvisQueries, null, null, GeneralAvoid),
                    new[] { "pelvis" }),
                MakeChain(RetargetChainId.RightLeg, MakeLookups(ueLegQueries, null, RightPostfixes, LegAvoid),
                    new[] { "thigh_r", "calf_r", "foot_r" }),
                MakeChain(RetargetChainId.LeftLeg, MakeLookups(ueLegQueries, null, LeftPostfixes, LegAvoid),
                    new[] { "thigh_l", "calf_l", "foot_l" }),
                MakeChain(RetargetChainId.Toes, MakeLookups(ueToeQueries, null, SidePostfixes, GeneralAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Spine, MakeLookups(ueSpineQueries, null, null, GeneralAvoid),
                    new[] { "spine_01", "spine_02", "spine_03", "spine_04", "spine_05" }),
                MakeChain(RetargetChainId.Neck, MakeLookups(ueNeckQueries, null, null, GeneralAvoid),
                    new[] { "neck_01", "neck_02", "head" }),
                MakeChain(RetargetChainId.RightArm, MakeLookups(ueArmQueries, null, RightPostfixes, ArmAvoid),
                    new[] { "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r" }),
                MakeChain(RetargetChainId.LeftArm, MakeLookups(ueArmQueries, null, LeftPostfixes, ArmAvoid),
                    new[] { "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l" }),
                MakeChain(RetargetChainId.IndexFingers, MakeLookups(ueIndexQueries, null, null, FingerAvoid),
                    new[] { "index_01_l", "index_02_l", "index_03_l" }),
                MakeChain(RetargetChainId.MiddleFingers, MakeLookups(ueMiddleQueries, null, null, FingerAvoid),
                    new[] { "middle_01_l", "middle_02_l", "middle_03_l" }),
                MakeChain(RetargetChainId.RingFingers, MakeLookups(ueRingQueries, null, null, FingerAvoid),
                    new[] { "ring_01_l", "ring_02_l", "ring_03_l" }),
                MakeChain(RetargetChainId.PinkyFingers, MakeLookups(uePinkyQueries, null, null, FingerAvoid),
                    new[] { "pinky_01_l", "pinky_02_l", "pinky_03_l" }),
                MakeChain(RetargetChainId.ThumbFingers, MakeLookups(ueThumbQueries, null, null, FingerAvoid),
                    new[] { "thumb_01_l", "thumb_02_l", "thumb_03_l" })
            };
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(new[] { "ik_hand_gun" }),
                MakeLookups(new[] { "spine_01" }, null, null, GeneralAvoid),
                MakeLookups(new[] { "upperarm" }, null, LeftPostfixes, ArmAvoid),
                MakeLookups(new[] { "thigh" }, null, LeftPostfixes, LegAvoid),
            };
        }

    }

    public class QuadrupedRetargetPreset : RetargetPreset
    {
        private static readonly string[] QuadRightPrefixes =
        {
            "right", "r_", "right_", "r.", "r ", "rf_", "fr_", "rb_", "br_", "rfront", "rrear", "rfore", "rhind"
        };

        private static readonly string[] QuadRightPostfixes =
        {
            "right", "_r", "_right", ".r", " r", "_rf", "_fr", "_rb", "_br", ".rf", ".fr", ".rb", ".br"
        };

        private static readonly string[] QuadLeftPrefixes =
        {
            "left", "l_", "left_", "l.", "l ", "lf_", "fl_", "lb_", "bl_", "lfront", "lrear", "lfore", "lhind"
        };

        private static readonly string[] QuadLeftPostfixes =
        {
            "left", "_l", "_left", ".l", " l", "_lf", "_fl", "_lb", "_bl", ".lf", ".fl", ".lb", ".bl"
        };

        private static readonly string[] QuadSpineQueries =
        {
            "spine", "chest", "back", "body", "thorax", "rib", "ribcage"
        };

        private static readonly string[] QuadNeckQueries = { "neck", "head" };
        private static readonly string[] QuadEarQueries = { "ear", "pinna" };
        private static readonly string[] QuadJawQueries = { "jaw", "mandible" };
        private static readonly string[] QuadTailQueries = { "tail", "caudal" };

        private static readonly string[] FrontLegQueries =
        {
            "frontleg", "foreleg", "front", "fore", "shoulder", "clavicle", "upperarm", "arm", "forearm", "lowerarm",
            "elbow", "knee", "carpal", "wrist", "hand", "hoof", "paw", "pastern"
        };

        private static readonly string[] HindLegQueries =
        {
            "hindleg", "rearleg", "backleg", "hind", "rear", "back", "thigh", "upperleg", "leg", "calf",
            "lowerleg", "knee", "hock", "ankle", "foot", "hoof", "paw", "pastern"
        };

        private static readonly string[] FrontAvoid =
        {
            "tail", "spine", "neck", "head", "hind", "rear", "back", "thigh", "upperleg", "pelvis", "hip"
        };

        private static readonly string[] HindAvoid =
        {
            "tail", "spine", "neck", "head", "front", "fore", "shoulder", "arm", "clavicle"
        };

        private static readonly string[] SpineAvoid =
        {
            "tail", "leg", "arm", "head", "neck", "front", "rear", "fore", "hind"
        };

        private static readonly string[] TailAvoid =
        {
            "spine", "leg", "arm", "neck", "head"
        };

        private static readonly string[] EarAvoid =
        {
            "rear", "front", "fore", "hind", "leg", "arm", "paw", "hoof", "tail", "spine", "neck", "jaw", "tongue",
            "tooth", "teeth"
        };

        private static readonly string[] JawAvoid =
        {
            "ear", "tongue", "tooth", "teeth", "fang", "lip", "whisker"
        };

        public override string GetName()
        {
            return "Quadruped";
        }

        public override bool ShouldUseChainIk(RetargetChainId chainId)
        {
            return chainId == RetargetChainId.RightArm ||
                   chainId == RetargetChainId.LeftArm ||
                   chainId == RetargetChainId.RightLeg ||
                   chainId == RetargetChainId.LeftLeg;
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(RootQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(PelvisQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.Spine, MakeLookups(QuadSpineQueries, null, null, SpineAvoid)),
                MakeChain(RetargetChainId.RightLeg, MakeLookups(HindLegQueries, QuadRightPrefixes, QuadRightPostfixes, HindAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.LeftLeg, MakeLookups(HindLegQueries, QuadLeftPrefixes, QuadLeftPostfixes, HindAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Neck, MakeLookups(QuadNeckQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.Jaw, MakeLookups(QuadJawQueries, null, null, JawAvoid)),
                MakeChain(RetargetChainId.LeftEar, MakeLookups(QuadEarQueries, QuadLeftPrefixes, QuadLeftPostfixes, EarAvoid)),
                MakeChain(RetargetChainId.RightEar, MakeLookups(QuadEarQueries, QuadRightPrefixes, QuadRightPostfixes, EarAvoid)),
                MakeChain(RetargetChainId.Tail, MakeLookups(QuadTailQueries, null, null, TailAvoid)),
                MakeChain(RetargetChainId.RightArm, MakeLookups(FrontLegQueries, QuadRightPrefixes, QuadRightPostfixes, FrontAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.LeftArm, MakeLookups(FrontLegQueries, QuadLeftPrefixes, QuadLeftPostfixes, FrontAvoid),
                    collectAllMatches: true)
            };
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(QuadSpineQueries, null, null, SpineAvoid),
                MakeLookups(QuadTailQueries, null, null, TailAvoid),
                MakeLookups(FrontLegQueries, QuadLeftPrefixes, QuadLeftPostfixes, FrontAvoid),
                MakeLookups(HindLegQueries, QuadLeftPrefixes, QuadLeftPostfixes, HindAvoid)
            };
        }

    }

    public class UnityHumanoidRetargetPreset : RetargetPreset
    {
        public override string GetName()
        {
            return "Unity Humanoid";
        }

        protected override RetargetChainDefinition[] BuildChainDefinitions()
        {
            return new[]
            {
                MakeChain(RetargetChainId.Root, MakeLookups(RootQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.Pelvis, MakeLookups(PelvisQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.RightLeg, MakeLegLookups(RightPrefixes, RightPostfixes)),
                MakeChain(RetargetChainId.LeftLeg, MakeLegLookups(LeftPrefixes, LeftPostfixes)),
                MakeChain(RetargetChainId.Toes, MakeLookups(ToeQueries, null, null, GeneralAvoid),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.Spine, MakeLookups(SpineQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.Neck, MakeLookups(NeckQueries, null, null, GeneralAvoid)),
                MakeChain(RetargetChainId.RightArm, MakeArmLookups(RightPrefixes, RightPostfixes)),
                MakeChain(RetargetChainId.LeftArm, MakeArmLookups(LeftPrefixes, LeftPostfixes)),
                MakeChain(RetargetChainId.IndexFingers, MakeFingerLookups(IndexQueries, null),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.MiddleFingers, MakeFingerLookups(MiddleQueries, null),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.RingFingers, MakeFingerLookups(RingQueries, null),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.PinkyFingers, MakeFingerLookups(PinkyQueries, null),
                    collectAllMatches: true),
                MakeChain(RetargetChainId.ThumbFingers, MakeFingerLookups(ThumbQueries, null),
                    collectAllMatches: true)
            };
        }

        protected override RetargetBoneNameLookups[] BuildSignatureLookups()
        {
            return new[]
            {
                MakeLookups(PelvisQueries, null, null, GeneralAvoid),
                MakeLookups(SpineQueries, null, null, GeneralAvoid),
                MakeArmLookups(LeftPrefixes, LeftPostfixes),
                MakeLegLookups(LeftPrefixes, LeftPostfixes)
            };
        }
    }
}
