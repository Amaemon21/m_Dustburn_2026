// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.FPSRetargeting
{
    [ScriptableComponentGroup("Retargeting", "FPS")]
    public class FPSRetargetFeature : RetargetFeature
    {
        [Header("Source Chains")]
        [RigAssetSelector("sourceRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Source right arm chain. Expected order: upper arm, forearm, hand.")]
        public KRigElementChain sourceRightArm;

        [RigAssetSelector("sourceRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Source left arm chain. Expected order: upper arm, forearm, hand.")]
        public KRigElementChain sourceLeftArm;

        [RigAssetSelector("sourceRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Source weapon or weapon-handle bone used to anchor the FPS setup.")]
        public KRigElementChain sourceWeapon;

        [Header("Target Chains")]
        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Target right arm chain. Expected order: upper arm, forearm, hand.")]
        public KRigElementChain targetRightArm;

        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Target left arm chain. Expected order: upper arm, forearm, hand.")]
        public KRigElementChain targetLeftArm;

        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, false)]
        [Tooltip("Target weapon or weapon-handle bone used to anchor the FPS setup.")]
        public KRigElementChain targetWeapon;

        [HideInInspector] public bool enableWeaponHandle;
        [HideInInspector] public bool enableRightHandHandle;
        [HideInInspector] public bool enableLeftHandle;
        [HideInInspector] public bool enableRightPole;
        [HideInInspector] public bool enableLeftPole;

        [Header("IK Offsets")] [Tooltip("Right hand translation offset.")]
        public Vector3 rightHandOffset = Vector3.zero;

        [Tooltip("Left hand translation offset.")]
        public Vector3 leftHandOffset = Vector3.zero;

        [Tooltip("Weapon translation offset.")]
        public Vector3 weaponOffset = Vector3.zero;

        [Header("Pole IK Offsets")] [Tooltip("Right pole (elbow) translation offset.")]
        public Vector3 rightPoleOffset = Vector3.zero;

        [Tooltip("Left pole (elbow) translation offset.")]
        public Vector3 leftPoleOffset = Vector3.zero;

        public override RetargetFeatureState CreateFeatureState()
        {
            return new FPSRetargetFeatureState();
        }

        internal static bool IsArmPairValid(KRigElementChain source, KRigElementChain target)
        {
            return source?.elementChain != null && target?.elementChain != null &&
                   source.elementChain.Count >= 3 && source.elementChain.Count == target.elementChain.Count;
        }

        internal static bool IsWeaponChainStructurallyValid(KRigElementChain chain)
        {
            return chain?.elementChain != null && chain.elementChain.Count > 0;
        }

#if UNITY_EDITOR
        private enum BoneSide
        {
            Unknown = 0,
            Left = 1,
            Right = 2
        }

        private static readonly string[] ArmQueries =
        {
            "arm", "upper arm", "upperarm", "forearm", "lower arm", "lowerarm", "hand", "wrist"
        };

        private static readonly string[] ArmAvoidQueries =
        {
            "finger", "thumb", "index", "middle", "ring", "pinky", "weapon", "gun", "leg", "foot", "toe"
        };

        private static readonly string[] UpperArmQueries = { "upper arm", "upperarm", "bicep", "arm" };

        private static readonly string[] UpperArmAvoidQueries =
        {
            "forearm", "lower arm", "lowerarm", "hand", "wrist", "finger", "thumb", "index", "middle", "ring",
            "pinky", "weapon", "gun", "ik", "twist", "roll"
        };

        private static readonly string[] LowerArmQueries = { "forearm", "fore arm", "lower arm", "lowerarm", "elbow" };

        private static readonly string[] LowerArmAvoidQueries =
        {
            "upper arm", "upperarm", "hand", "wrist", "finger", "thumb", "index", "middle", "ring", "pinky",
            "weapon", "gun", "ik", "twist", "roll"
        };

        private static readonly string[] HandQueries = { "hand", "wrist", "palm" };

        private static readonly string[] HandAvoidQueries =
        {
            "finger", "thumb", "index", "middle", "ring", "pinky", "weapon", "gun", "ik", "socket"
        };

        private static readonly string[] WeaponQueries =
        {
            "weapon root", "weapon", "ik hand gun", "ik gun", "hand gun", "gun", "rifle", "pistol", "shotgun",
            "smg", "carbine", "launcher", "firearm"
        };

        private static readonly string[] WeaponAvoidQueries =
        {
            "muzzle", "mag", "magazine", "ammo", "trigger", "bolt", "slide", "sight", "socket", "barrel", "tip",
            "end", "fx", "vfx"
        };

        private static bool IsWeaponMappingValid(KRig rig, KRigElementChain chain)
        {
            if (!IsWeaponChainStructurallyValid(chain)) return false;

            KRigElement selectedElement = chain.elementChain[0];
            if (ScoreName(selectedElement.name, WeaponQueries, WeaponAvoidQueries, BoneSide.Unknown) > 0)
            {
                return true;
            }

            KRigElementChain rigChain = rig?.GetElementChainByName(chain.chainName);
            if (rigChain?.elementChain == null ||
                ScoreName(chain.chainName, WeaponQueries, WeaponAvoidQueries, BoneSide.Unknown) <= 0)
            {
                return false;
            }

            foreach (KRigElement element in rigChain.elementChain)
            {
                if (element.name == selectedElement.name) return true;
            }

            return false;
        }

        private static KRigElementChain BuildArmChain(KRig rig, BoneSide side, string fallbackName)
        {
            KRigElementChain best = new KRigElementChain { chainName = fallbackName };
            if (rig == null || rig.rigElementChains == null)
            {
                return best;
            }

            int bestScore = 0;
            foreach (KRigElementChain chain in rig.rigElementChains)
            {
                KRigElementChain candidate = ExtractArmChain(chain, side, fallbackName, out int score);
                if (candidate.elementChain.Count == 0 || score <= bestScore)
                {
                    continue;
                }

                best = candidate;
                bestScore = score;
            }

            return best;
        }

        private static KRigElementChain ExtractArmChain(KRigElementChain sourceChain, BoneSide side,
            string fallbackName, out int score)
        {
            KRigElementChain result = new KRigElementChain { chainName = fallbackName };
            score = 0;
            if (sourceChain == null || sourceChain.elementChain == null || sourceChain.elementChain.Count < 3)
            {
                return result;
            }

            string chainName = string.IsNullOrEmpty(sourceChain.chainName) ? fallbackName : sourceChain.chainName;
            int chainScore = ScoreName(chainName, ArmQueries, ArmAvoidQueries, side);
            List<KRigElement> elements = sourceChain.elementChain;
            int bestUpperIndex = -1;
            int bestLowerIndex = -1;
            int bestHandIndex = -1;
            int bestSequenceScore = 0;
            for (int upperIndex = 0; upperIndex < elements.Count; upperIndex++)
            {
                int upperScore = ScoreName(elements[upperIndex].name, UpperArmQueries, UpperArmAvoidQueries, side);
                if (upperScore <= 0)
                {
                    continue;
                }

                for (int lowerIndex = upperIndex + 1; lowerIndex < elements.Count; lowerIndex++)
                {
                    int lowerScore = ScoreName(elements[lowerIndex].name, LowerArmQueries, LowerArmAvoidQueries, side);
                    if (lowerScore <= 0)
                    {
                        continue;
                    }

                    for (int handIndex = lowerIndex + 1; handIndex < elements.Count; handIndex++)
                    {
                        int handScore = ScoreName(elements[handIndex].name, HandQueries, HandAvoidQueries, side);
                        if (handScore <= 0)
                        {
                            continue;
                        }

                        int span = handIndex - upperIndex;
                        int sequenceScore = chainScore + upperScore + lowerScore + handScore;
                        sequenceScore += Mathf.Max(0, 8 - Mathf.Abs(span - 2) * 3);
                        if (sequenceScore <= bestSequenceScore)
                        {
                            continue;
                        }

                        bestSequenceScore = sequenceScore;
                        bestUpperIndex = upperIndex;
                        bestLowerIndex = lowerIndex;
                        bestHandIndex = handIndex;
                    }
                }
            }

            if (bestUpperIndex < 0 || bestLowerIndex < 0 || bestHandIndex < 0)
            {
                return result;
            }

            result.chainName = chainName;
            AddUniqueElement(result.elementChain, elements[bestUpperIndex]);
            AddUniqueElement(result.elementChain, elements[bestLowerIndex]);
            AddUniqueElement(result.elementChain, elements[bestHandIndex]);
            score = bestSequenceScore + result.elementChain.Count * 5;
            return result;
        }

        private static KRigElementChain BuildWeaponChain(KRig rig, string fallbackName)
        {
            KRigElementChain result = new KRigElementChain { chainName = fallbackName };
            if (rig == null)
            {
                return result;
            }

            KRigElement bestElement = new KRigElement();
            bool hasBest = false;
            int bestScore = 0;
            int bestDepth = int.MaxValue;
            if (rig.rigElementChains != null)
            {
                foreach (KRigElementChain chain in rig.rigElementChains)
                {
                    if (chain == null || chain.elementChain == null || chain.elementChain.Count == 0)
                    {
                        continue;
                    }

                    int chainScore = ScoreName(chain.chainName, WeaponQueries, WeaponAvoidQueries, BoneSide.Unknown);
                    if (chainScore <= 0)
                    {
                        continue;
                    }

                    KRigElement element = chain.elementChain[0];
                    int score = chainScore + 5;
                    if (!hasBest || score > bestScore || (score == bestScore && element.depth < bestDepth))
                    {
                        bestElement = element;
                        bestScore = score;
                        bestDepth = element.depth;
                        hasBest = true;
                    }
                }
            }

            if (rig.rigHierarchy != null)
            {
                foreach (KRigElement element in rig.rigHierarchy)
                {
                    int score = ScoreName(element.name, WeaponQueries, WeaponAvoidQueries, BoneSide.Unknown);
                    if (score <= 0)
                    {
                        continue;
                    }

                    if (!hasBest || score > bestScore || (score == bestScore && element.depth < bestDepth))
                    {
                        bestElement = element;
                        bestScore = score;
                        bestDepth = element.depth;
                        hasBest = true;
                    }
                }
            }

            if (!hasBest)
            {
                return result;
            }

            result.chainName = bestElement.name;
            result.elementChain.Add(bestElement);
            return result;
        }

        private static void AddUniqueElement(List<KRigElement> destination, KRigElement element)
        {
            foreach (KRigElement existing in destination)
            {
                if (existing.index == element.index)
                {
                    return;
                }
            }

            destination.Add(element);
        }

        private static int ScoreName(string name, string[] queries, string[] avoidQueries, BoneSide side)
        {
            if (string.IsNullOrEmpty(name) || queries == null || queries.Length == 0)
            {
                return 0;
            }

            string normalized = NormalizeSearchName(name);
            if (string.IsNullOrEmpty(normalized))
            {
                return 0;
            }

            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string searchCompact = BuildSearchCompact(tokens);
            if (string.IsNullOrEmpty(searchCompact))
            {
                return 0;
            }

            BoneSide detectedSide = DetectSide(tokens);
            if (side != BoneSide.Unknown && detectedSide != BoneSide.Unknown && detectedSide != side)
            {
                return 0;
            }

            if (ContainsAnyQuery(tokens, searchCompact, avoidQueries))
            {
                return 0;
            }

            int score = 0;
            foreach (string query in queries)
            {
                string queryCompact = CompactSearchQuery(query);
                if (string.IsNullOrEmpty(queryCompact) || !searchCompact.Contains(queryCompact))
                {
                    continue;
                }

                score += 10 + queryCompact.Length;
                if (HasExactToken(tokens, queryCompact))
                {
                    score += 5;
                }
            }

            if (score > 0 && side != BoneSide.Unknown && detectedSide == side)
            {
                score += 5;
            }

            return score;
        }

        private static bool ContainsAnyQuery(string[] tokens, string searchCompact, string[] queries)
        {
            if (queries == null || queries.Length == 0)
            {
                return false;
            }

            foreach (string query in queries)
            {
                string queryCompact = CompactSearchQuery(query);
                if (string.IsNullOrEmpty(queryCompact))
                {
                    continue;
                }

                if (searchCompact.Contains(queryCompact) || HasExactToken(tokens, queryCompact))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasExactToken(string[] tokens, string queryCompact)
        {
            if (tokens == null || string.IsNullOrEmpty(queryCompact))
            {
                return false;
            }

            foreach (string token in tokens)
            {
                if (token == queryCompact)
                {
                    return true;
                }
            }

            return false;
        }

        private static BoneSide DetectSide(string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return BoneSide.Unknown;
            }

            bool hasLeft = false;
            bool hasRight = false;
            foreach (string token in tokens)
            {
                if (token == "left" || token == "l" || token == "lf" || token == "fl" || token == "lft")
                {
                    hasLeft = true;
                }
                else if (token == "right" || token == "r" || token == "rf" || token == "fr" || token == "rgt")
                {
                    hasRight = true;
                }
            }

            if (hasLeft == hasRight)
            {
                return BoneSide.Unknown;
            }

            return hasLeft ? BoneSide.Left : BoneSide.Right;
        }

        private static string CompactSearchQuery(string query)
        {
            string normalized = NormalizeSearchName(query);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder builder = new StringBuilder();
            foreach (string token in tokens)
            {
                if (IsSideToken(token))
                {
                    continue;
                }

                builder.Append(token);
            }

            return builder.ToString();
        }

        private static string BuildSearchCompact(string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            foreach (string token in tokens)
            {
                if (string.IsNullOrEmpty(token) || IsSideToken(token))
                {
                    continue;
                }

                builder.Append(token);
            }

            return builder.ToString();
        }

        private static bool IsSideToken(string token)
        {
            return token == "l" || token == "r" || token == "left" || token == "right" || token == "lf" ||
                   token == "rf" || token == "fl" || token == "fr" || token == "lft" || token == "rgt";
        }

        private static string NormalizeSearchName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(name.Length * 2);
            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];
                if (!char.IsLetterOrDigit(current))
                {
                    AppendSpace(builder);
                    continue;
                }

                if (i > 0)
                {
                    char previous = name[i - 1];
                    if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                    {
                        AppendSpace(builder);
                    }
                    else if (char.IsDigit(current) && char.IsLetter(previous))
                    {
                        AppendSpace(builder);
                    }
                }

                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString().Trim();
        }

        private static void AppendSpace(StringBuilder builder)
        {
            if (builder.Length == 0 || builder[builder.Length - 1] == ' ')
            {
                return;
            }

            builder.Append(' ');
        }

        public override void OnFeatureAdded()
        {
            MapChains();
        }

        public override void MapChains()
        {
            sourceLeftArm = BuildArmChain(sourceRig, BoneSide.Left, "LeftArm");
            sourceRightArm = BuildArmChain(sourceRig, BoneSide.Right, "RightArm");
            targetLeftArm = BuildArmChain(targetRig, BoneSide.Left, "LeftArm");
            targetRightArm = BuildArmChain(targetRig, BoneSide.Right, "RightArm");
            sourceWeapon = BuildWeaponChain(sourceRig, "Weapon");
            targetWeapon = BuildWeaponChain(targetRig, "Weapon");
        }

        public override string GetDisplayName()
        {
            return "FPS Animation Retarget";
        }

        public override bool GetStatus()
        {
            return IsArmPairValid(sourceRightArm, targetRightArm) &&
                   IsArmPairValid(sourceLeftArm, targetLeftArm) &&
                   IsWeaponMappingValid(sourceRig, sourceWeapon) &&
                   IsWeaponMappingValid(targetRig, targetWeapon);
        }

        public override string GetErrorMessage()
        {
            if (!IsArmPairValid(sourceRightArm, targetRightArm) ||
                !IsArmPairValid(sourceLeftArm, targetLeftArm))
            {
                return "FPS Retargeting requires matching source and target arm chains with at least three bones.";
            }

            return "FPS Retargeting requires valid source and target weapon bones. Press Refresh or assign them manually.";
        }
#endif
    }
}
