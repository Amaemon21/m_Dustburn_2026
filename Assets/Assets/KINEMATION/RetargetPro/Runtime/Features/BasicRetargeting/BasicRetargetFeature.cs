// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting
{
    [ScriptableComponentGroup("Retargeting", "Basic")]
    public class BasicRetargetFeature : RetargetFeature, IDynamicRetarget
    {
        private const string DefaultChainName = "None";

        [Header("Chains")]
        [RigAssetSelector("sourceRig"), CustomElementChainDrawer(true, true)]
        [Tooltip("Source bone chain sampled from the source rig.")]
        public KRigElementChain sourceChain = CreateDefaultChain();
        
        [RigAssetSelector("targetRig"), CustomElementChainDrawer(true, true)]
        [Tooltip("Target bone chain driven on the target rig.")]
        public KRigElementChain targetChain = CreateDefaultChain();
        
        [Header("Retarget Settings")]
        [Tooltip("Controls how much model scale differences are taken into account during retargeting.")]
        [Range(0f, 1f)] public float scaleWeight = 1f;
        [Tooltip("Controls how much translation is copied in addition to rotation. Useful for root, pelvis, and IK chains.")]
        [Range(0f, 1f)] public float translationWeight;
        
        [Header("Offsets")]
        [Tooltip("Adds a local translation offset after retargeting. Useful for root and pelvis alignment.")]
        public Vector3 offset = Vector3.zero;

        private void OnEnable()
        {
            EnsureChainDefaults();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureChainDefaults();
            ValidateChainName(sourceChain);
            ValidateChainName(targetChain);
        }
#endif

        protected void EnsureChainDefaults()
        {
            sourceChain = NormalizeChain(sourceChain);
            targetChain = NormalizeChain(targetChain);
        }

        private static KRigElementChain CreateDefaultChain()
        {
            return new KRigElementChain { chainName = DefaultChainName };
        }

        private static KRigElementChain NormalizeChain(KRigElementChain chain)
        {
            chain ??= CreateDefaultChain();

            if (string.IsNullOrEmpty(chain.chainName))
            {
                chain.chainName = DefaultChainName;
            }

            chain.elementChain ??= new List<KRigElement>();
            return chain;
        }

#if UNITY_EDITOR
        private static void ValidateChainName(KRigElementChain chain)
        {
            if (chain.elementChain.Count == 0)
            {
                chain.chainName = DefaultChainName;
            }
            else if (chain.chainName == DefaultChainName)
            {
                chain.chainName = chain.elementChain[0].name;
            }
        }
#endif

        protected bool HasSourceChain()
        {
            return sourceChain is { elementChain: { Count: > 0 } };
        }

        protected bool HasTargetChain()
        {
            return targetChain is { elementChain: { Count: > 0 } };
        }

        public virtual float GetTranslationBlend()
        {
            return translationWeight;
        }
        
        public override RetargetFeatureState CreateFeatureState()
        {
            return new BasicRetargetFeatureState();
        }
        
        public virtual IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable)
        {
            BasicRetargetJob job = new BasicRetargetJob();
            playable = AnimationScriptPlayable.Create(graph, job);
            return job;
        }
        
#if UNITY_EDITOR
        public override string GetDisplayName()
        {
            return "Basic Retarget";
        }

        public override void MapChains()
        {
            EnsureChainDefaults();

            foreach (var fromChain in sourceRig.rigElementChains)
            {
                if (!RetargetUtility.IsNameMatching(fromChain.chainName, targetChain.chainName)) continue;
                
                sourceChain = new KRigElementChain
                {
                    chainName = fromChain.chainName
                };
                foreach (var element in fromChain.elementChain) sourceChain.elementChain.Add(element);

                break;
            }
        }

        public override bool GetStatus()
        {
            return HasSourceChain() && HasTargetChain();
        }

        public override string GetErrorMessage()
        {
            if (!HasSourceChain() && !HasTargetChain())
            {
                return "Source and target chains are empty. Press the Refresh icon in Actions or assign the chains manually.";
            }

            if (!HasSourceChain())
            {
                return "Source chain is empty. Press the Refresh icon in Actions or assign the chain manually.";
            }

            return "Target chain is empty. Press the Refresh icon in Actions or assign the chain manually.";
        }

        public override void CollectInitializationMessages(RetargetFeatureState state,
            List<RetargetFeatureInitializationMessage> messages)
        {
            base.CollectInitializationMessages(state, messages);

            if (!GetStatus() || state is not BasicRetargetFeatureState basicState || basicState.HasMatchingChainSizes)
            {
                return;
            }

            int sourceCount = basicState.SourceChainCount;
            int targetCount = basicState.TargetChainCount;
            if (sourceCount <= 0 || targetCount <= 0)
            {
                return;
            }

            AddInitializationNotificationMessage(messages,
                $"Source chain has {sourceCount} bone(s) and target chain has {targetCount}. " +
                "Rotations will be distributed across the target chain.");
        }
#endif
    }
}
