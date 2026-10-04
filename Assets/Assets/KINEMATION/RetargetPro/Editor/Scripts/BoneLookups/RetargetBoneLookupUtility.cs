// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.BoneLookups
{
    public struct RetargetBoneNameLookups
    {
        public string[] queries;
        public string[] prefixes;
        public string[] postfixes;
        public string[] avoidQueries;
        public bool IsValid => queries != null && queries.Length > 0;
    }

    internal struct RigNode
    {
        public KRigElement element;
        public int parentIndex;
        public string rawLower;
        public string normalized;
        public string compact;
        public string searchCompact;
        public string[] tokens;
    }

    internal struct NodeMatch
    {
        public int index;
        public int score;
    }

    public static class RetargetBoneLookupUtility
    {
        private static readonly char[] TokenSplit = { ' ' };

        public static bool IsNameMatching(string name, RetargetBoneNameLookups lookups)
        {
            return ScoreName(name, lookups) > 0;
        }

        public static bool TryFindBestElement(IEnumerable<KRigElement> elements, RetargetBoneNameLookups lookups,
            out KRigElement bestElement)
        {
            bestElement = default;
            if (elements == null || !lookups.IsValid)
            {
                return false;
            }

            bool hasBest = false;
            int bestScore = 0;
            int bestDepth = int.MaxValue;
            foreach (var element in elements)
            {
                if (string.IsNullOrEmpty(element.name)) continue;
                int score = ScoreName(element.name, lookups);
                if (score <= 0) continue;
                int depth = element.depth;
                if (!hasBest || score > bestScore || (score == bestScore && depth < bestDepth))
                {
                    bestScore = score;
                    bestDepth = depth;
                    bestElement = element;
                    hasBest = true;
                }
            }

            return hasBest;
        }

        public static KRigElementChain BuildBestChain(KRig rig, RetargetBoneNameLookups lookups, string chainName)
        {
            KRigElementChain chain = new KRigElementChain { chainName = chainName };
            if (rig == null || rig.rigHierarchy == null || rig.rigHierarchy.Count == 0)
            {
                return chain;
            }

            if (!lookups.IsValid)
            {
                return chain;
            }

            List<RigNode> nodes = BuildNodes(rig.rigHierarchy);
            List<NodeMatch> matches = new List<NodeMatch>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int score = ScoreNode(nodes[i], lookups);
                if (score <= 0) continue;
                matches.Add(new NodeMatch { index = i, score = score });
            }

            if (matches.Count == 0)
            {
                return chain;
            }

            matches.Sort((a, b) =>
            {
                int depthCompare = nodes[a.index].element.depth.CompareTo(nodes[b.index].element.depth);
                if (depthCompare != 0) return depthCompare;
                return a.index.CompareTo(b.index);
            });
            HashSet<int> matchedIndices = new HashSet<int>();
            foreach (var match in matches) matchedIndices.Add(match.index);
            List<int> bestChain = null;
            float bestScore = float.MinValue;
            foreach (var rootMatch in matches)
            {
                if (HasMatchedAncestor(rootMatch.index, matchedIndices, nodes))
                {
                    continue;
                }

                List<int> chainIndices = new List<int> { rootMatch.index };
                int lastIndex = rootMatch.index;
                float totalScore = rootMatch.score;
                for (int i = 0; i < matches.Count; i++)
                {
                    int candidateIndex = matches[i].index;
                    if (candidateIndex == rootMatch.index) continue;
                    if (nodes[candidateIndex].element.depth <= nodes[lastIndex].element.depth)
                    {
                        continue;
                    }

                    if (!IsDescendant(candidateIndex, lastIndex, nodes))
                    {
                        continue;
                    }

                    chainIndices.Add(candidateIndex);
                    totalScore += matches[i].score;
                    lastIndex = candidateIndex;
                }

                float depthSpan = nodes[chainIndices[chainIndices.Count - 1]].element.depth -
                                  nodes[chainIndices[0]].element.depth;
                float chainScore = totalScore + chainIndices.Count * 2f + depthSpan;
                if (chainScore > bestScore + 0.001f || (Mathf.Abs(chainScore - bestScore) <= 0.001f &&
                                                        (bestChain == null || chainIndices.Count > bestChain.Count)))
                {
                    bestScore = chainScore;
                    bestChain = chainIndices;
                }
            }

            if (bestChain == null || bestChain.Count == 0)
            {
                return chain;
            }

            foreach (int index in bestChain)
            {
                chain.elementChain.Add(nodes[index].element);
            }

            return chain;
        }

        private static List<RigNode> BuildNodes(IReadOnlyList<KRigElement> elements)
        {
            List<RigNode> nodes = new List<RigNode>(elements.Count);
            Stack<int> parentStack = new Stack<int>();
            for (int i = 0; i < elements.Count; i++)
            {
                KRigElement element = elements[i];
                while (parentStack.Count > 0 && elements[parentStack.Peek()].depth >= element.depth)
                {
                    parentStack.Pop();
                }

                int parentIndex = parentStack.Count > 0 ? parentStack.Peek() : -1;
                string rawLower = string.IsNullOrEmpty(element.name) ? string.Empty : element.name.ToLowerInvariant();
                string normalized = NormalizeName(element.name);
                string compact = normalized.Replace(" ", string.Empty);
                string[] tokens = normalized.Split(TokenSplit, StringSplitOptions.RemoveEmptyEntries);
                nodes.Add(new RigNode
                {
                    element = element,
                    parentIndex = parentIndex,
                    rawLower = rawLower,
                    normalized = normalized,
                    compact = compact,
                    searchCompact = BuildSearchCompact(tokens),
                    tokens = tokens
                });
                parentStack.Push(i);
            }

            return nodes;
        }

        private static bool HasMatchedAncestor(int index, HashSet<int> matched, List<RigNode> nodes)
        {
            int parent = nodes[index].parentIndex;
            while (parent >= 0)
            {
                if (matched.Contains(parent))
                {
                    return true;
                }

                parent = nodes[parent].parentIndex;
            }

            return false;
        }

        private static bool IsDescendant(int candidateIndex, int ancestorIndex, List<RigNode> nodes)
        {
            int parent = nodes[candidateIndex].parentIndex;
            while (parent >= 0)
            {
                if (parent == ancestorIndex) return true;
                parent = nodes[parent].parentIndex;
            }

            return false;
        }

        private static int ScoreName(string name, RetargetBoneNameLookups lookups)
        {
            if (!lookups.IsValid || string.IsNullOrEmpty(name)) return 0;
            RigNode node = new RigNode { rawLower = name.ToLowerInvariant(), normalized = NormalizeName(name) };
            node.compact = node.normalized.Replace(" ", string.Empty);
            node.tokens = node.normalized.Split(TokenSplit, StringSplitOptions.RemoveEmptyEntries);
            node.searchCompact = BuildSearchCompact(node.tokens);
            return ScoreNode(node, lookups);
        }

        private static int ScoreNode(RigNode node, RetargetBoneNameLookups lookups)
        {
            if (!lookups.IsValid) return 0;
            if (IsAvoided(node, lookups))
            {
                return 0;
            }

            bool prefixRequired = lookups.prefixes != null && lookups.prefixes.Length > 0;
            bool postfixRequired = lookups.postfixes != null && lookups.postfixes.Length > 0;
            bool hasPrefix = prefixRequired && MatchesPrefix(node, lookups.prefixes);
            bool hasPostfix = postfixRequired && MatchesPostfix(node, lookups.postfixes);
            if (prefixRequired && postfixRequired)
            {
                if (!hasPrefix && !hasPostfix)
                {
                    return 0;
                }
            }
            else
            {
                if (prefixRequired && !hasPrefix)
                {
                    return 0;
                }

                if (postfixRequired && !hasPostfix)
                {
                    return 0;
                }
            }

            int matchCount = 0;
            int bestSpecificity = 0;
            int exactTokenHits = 0;
            foreach (var query in lookups.queries)
            {
                if (string.IsNullOrEmpty(query)) continue;
                string queryNormalized = NormalizeName(query);
                if (string.IsNullOrEmpty(queryNormalized)) continue;
                string queryCompact = queryNormalized.Replace(" ", string.Empty);
                if (string.IsNullOrEmpty(queryCompact)) continue;
                if (!ContainsSearchQuery(node, queryCompact)) continue;
                matchCount++;
                bestSpecificity = Math.Max(bestSpecificity, queryCompact.Length);
                if (HasExactToken(node.tokens, queryCompact))
                {
                    exactTokenHits++;
                }
            }

            if (matchCount == 0)
            {
                return 0;
            }

            int score = matchCount * 10 + bestSpecificity + exactTokenHits * 5;
            if (hasPrefix)
            {
                score += 5;
            }

            if (hasPostfix)
            {
                score += 5;
            }

            return score;
        }

        private static bool IsAvoided(RigNode node, RetargetBoneNameLookups lookups)
        {
            if (lookups.avoidQueries == null || lookups.avoidQueries.Length == 0)
            {
                return false;
            }

            foreach (var avoid in lookups.avoidQueries)
            {
                if (string.IsNullOrEmpty(avoid)) continue;
                string avoidNormalized = NormalizeName(avoid);
                if (string.IsNullOrEmpty(avoidNormalized)) continue;
                string avoidCompact = avoidNormalized.Replace(" ", string.Empty);
                if (string.IsNullOrEmpty(avoidCompact)) continue;
                if (ContainsSearchQuery(node, avoidCompact))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesPrefix(RigNode node, string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (string.IsNullOrEmpty(prefix)) continue;
                string lowerPrefix = prefix.ToLowerInvariant();
                if (node.rawLower.StartsWith(lowerPrefix))
                {
                    return true;
                }

                string normalizedPrefix = NormalizeName(prefix);
                if (string.IsNullOrEmpty(normalizedPrefix)) continue;
                string compactPrefix = normalizedPrefix.Replace(" ", string.Empty);
                if (string.IsNullOrEmpty(compactPrefix)) continue;
                if (node.compact.StartsWith(compactPrefix))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesPostfix(RigNode node, string[] postfixes)
        {
            foreach (var postfix in postfixes)
            {
                if (string.IsNullOrEmpty(postfix)) continue;
                string lowerPostfix = postfix.ToLowerInvariant();
                if (node.rawLower.EndsWith(lowerPostfix))
                {
                    return true;
                }

                string normalizedPostfix = NormalizeName(postfix);
                if (string.IsNullOrEmpty(normalizedPostfix)) continue;
                string compactPostfix = normalizedPostfix.Replace(" ", string.Empty);
                if (string.IsNullOrEmpty(compactPostfix)) continue;
                if (MatchesCompactPostfix(node, compactPostfix))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasExactToken(string[] tokens, string queryCompact)
        {
            if (tokens == null || tokens.Length == 0) return false;
            foreach (var token in tokens)
            {
                if (token == queryCompact) return true;
            }

            return false;
        }

        private static bool ContainsSearchQuery(RigNode node, string queryCompact)
        {
            if (string.IsNullOrEmpty(queryCompact))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(node.searchCompact))
            {
                return node.searchCompact.Contains(queryCompact);
            }

            return node.compact.Contains(queryCompact);
        }

        private static string BuildSearchCompact(string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrEmpty(token) || IsStandaloneSideToken(token))
                {
                    continue;
                }

                builder.Append(token);
            }

            return builder.ToString();
        }

        private static bool MatchesCompactPostfix(RigNode node, string compactPostfix)
        {
            if (node.tokens == null || node.tokens.Length == 0 || string.IsNullOrEmpty(compactPostfix))
            {
                return false;
            }

            string lastToken = node.tokens[node.tokens.Length - 1];
            return lastToken == compactPostfix;
        }

        private static bool IsStandaloneSideToken(string token)
        {
            return token == "l" || token == "r" || token == "left" || token == "right" || token == "lf" ||
                   token == "rf" || token == "lb" || token == "rb" || token == "fl" || token == "fr" || token == "bl" ||
                   token == "br";
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            StringBuilder builder = new StringBuilder(name.Length);
            char prev = '\0';
            bool prevIsLetterOrDigit = false;
            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];
                if (IsSeparator(current))
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    {
                        builder.Append(' ');
                    }

                    prev = current;
                    prevIsLetterOrDigit = false;
                    continue;
                }

                bool isLetterOrDigit = char.IsLetterOrDigit(current);
                bool addSplit = false;
                if (isLetterOrDigit && prevIsLetterOrDigit)
                {
                    if (char.IsDigit(current) && char.IsLetter(prev))
                    {
                        addSplit = true;
                    }
                    else if (char.IsLetter(current) && char.IsDigit(prev))
                    {
                        addSplit = true;
                    }
                    else if (char.IsUpper(current) && char.IsLower(prev))
                    {
                        addSplit = true;
                    }
                }

                if (addSplit && builder.Length > 0 && builder[builder.Length - 1] != ' ')
                {
                    builder.Append(' ');
                }

                if (isLetterOrDigit)
                {
                    builder.Append(char.ToLowerInvariant(current));
                    prevIsLetterOrDigit = true;
                }
                else
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    {
                        builder.Append(' ');
                    }

                    prevIsLetterOrDigit = false;
                }

                prev = current;
            }

            return builder.ToString().Trim();
        }

        private static bool IsSeparator(char character)
        {
            return character == ' ' || character == '_' || character == ':' || character == '.' || character == '-' ||
                   character == '/' || character == '\\' || character == '|';
        }
    }
}