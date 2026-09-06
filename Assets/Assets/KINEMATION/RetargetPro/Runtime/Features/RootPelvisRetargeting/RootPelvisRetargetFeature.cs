// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.RootPelvisRetargeting
{
    [ScriptableComponentGroup("Retargeting", "Root/Pelvis")]
    public class RootPelvisRetargetFeature : BasicRetargetFeature
    {
        private void OnEnable()
        {
            EnsureChainDefaults();

            // Root and pelvis translation should be enabled by default.
            if (Mathf.Approximately(translationWeight, 0f))
            {
                translationWeight = 1f;
            }

            if (Mathf.Approximately(scaleWeight, 0f))
            {
                scaleWeight = 1f;
            }
        }

        public override RetargetFeatureState CreateFeatureState()
        {
            return new RootPelvisRetargetFeatureState();
        }

        public override IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable)
        {
            RootPelvisRetargetJob job = new RootPelvisRetargetJob();
            playable = AnimationScriptPlayable.Create(graph, job);
            return job;
        }

#if UNITY_EDITOR
        public override string GetDisplayName()
        {
            return "Root/Pelvis Retarget";
        }
#endif
    }
}