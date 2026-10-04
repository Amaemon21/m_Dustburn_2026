// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.BoneLookups
{
    public struct CasBoneNameLookups
    {
        public string[] queries;
        public string[] prefixes;
        public string[] postfixes;
        public string[] avoidQueries;
        public bool exactMatch;
        
        public bool IsValid => queries != null;
    }

    public struct CasQuickSkeletonRecognizer
    {
        public string[] queries;
        public int minMatches;

        public bool IsValid => queries != null && queries.Length > 0;
    }
    
    public class BoneLookupUtility
    {
        public static bool IsNameMatching(string name, CasBoneNameLookups nameLookups)
        {
            if (!nameLookups.IsValid) return false;

            if (nameLookups.exactMatch)
            {
                return nameLookups.queries.Any(query =>
                    string.Equals(name, query, StringComparison.OrdinalIgnoreCase));
            }

            name = name.ToLower();
            foreach (var query in nameLookups.queries)
            {
                if(!name.Contains(query.ToLower())) continue;

                if (nameLookups.avoidQueries != null)
                {
                    foreach (var avoidQuery in nameLookups.avoidQueries)
                    {
                        if (name.Contains(avoidQuery.ToLower())) return false;
                    }
                }
                
                bool matches = true;
                if (nameLookups.prefixes != null)
                {
                    foreach (var prefix in nameLookups.prefixes)
                    {
                        if (!name.ToLower().StartsWith(prefix.ToLower())) continue;
                        return true;
                    }

                    matches = false;
                }

                if (nameLookups.postfixes != null)
                {
                    foreach (var postfix in nameLookups.postfixes)
                    {
                        if (!name.ToLower().EndsWith(postfix.ToLower())) continue;
                        return true;
                    }

                    matches = false;
                }

                return matches;
            }

            return false;
        }
        
        public static List<CasBoneLookupPreset> GetBoneLookupPresets(CharacterSkeleton skeleton = null)
        {
            var lookupTypes
                = TypeCache.GetTypesDerivedFrom<CasBoneLookupPreset>()
                    .Where(t => !t.IsAbstract && t != typeof(Humanoid_CasBoneLookupPreset))
                    .OrderBy(t => t.FullName)
                    .ToArray();

            List<CasBoneLookupPreset> presets = new List<CasBoneLookupPreset>();
            Animator animator = skeleton == null ? null : skeleton.GetComponentInParent<Animator>(true);
            bool isHumanoid = animator != null && animator.avatar != null
                                               && animator.avatar.isValid && animator.avatar.isHuman;

            presets.Add(isHumanoid
                ? new Humanoid_CasBoneLookupPreset(animator, skeleton)
                : new CasBoneLookupPreset());
            foreach (var type in lookupTypes) presets.Add(Activator.CreateInstance(type) as CasBoneLookupPreset);

            return presets;
        }

        private static string[] NormalizeNames(CharacterSkeleton skeleton)
        {
            if (skeleton == null) return Array.Empty<string>();

            return skeleton.SkeletonBones
                .Select(bone => bone.rigElement.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.ToLowerInvariant())
                .ToArray();
        }

        private static int CountMatches(string[] normalizedNames, CasQuickSkeletonRecognizer recognizer)
        {
            if (!recognizer.IsValid) return 0;

            int matches = 0;
            foreach (var query in recognizer.queries)
            {
                if (string.IsNullOrWhiteSpace(query)) continue;

                string normalizedQuery = query.ToLowerInvariant();
                if (normalizedNames.Any(name => name.Contains(normalizedQuery)))
                {
                    matches++;
                }
            }

            return matches;
        }

        public static int GetDefaultBoneLookupIndex(CharacterSkeleton skeleton, 
            List<CasBoneLookupPreset> presets = null)
        {
            if (presets == null)
            {
                presets = GetBoneLookupPresets(skeleton);
            }

            if (presets.Count == 0) return 0;
            if (presets[0] is Humanoid_CasBoneLookupPreset) return 0;

            string[] normalizedNames = NormalizeNames(skeleton);
            if (normalizedNames.Length == 0) return 0;

            int bestIndex = 0;
            float bestScore = 0f;

            for (int i = 1; i < presets.Count; i++)
            {
                var recognizer = presets[i].GetQuickSkeletonRecognizer();
                if (!recognizer.IsValid) continue;

                int matches = CountMatches(normalizedNames, recognizer);
                int minMatches = Math.Max(1, recognizer.minMatches);
                if (matches < minMatches) continue;

                float score = (float) matches / recognizer.queries.Length;
                if (score <= bestScore) continue;

                bestScore = score;
                bestIndex = i;
            }

            return bestIndex;
        }
    }
}
