// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.RetargetPro.Runtime.Features.RootPelvisRetargeting
{
    public struct RootPelvisRetargetJob : IAnimationJob, IRetargetJob
    {
        public NativeArray<RetargetSceneAtom> sourceChain;
        public NativeArray<RetargetStreamAtom> targetChain;

        public BasicRetargetData basicData;

        public void Setup(RetargetFeature featureAsset, Animator animator, KRigComponent source, KRigComponent target)
        {
            var feature = featureAsset as RootPelvisRetargetFeature;
            if (feature == null) return;
            
            Transform sourceRootTransform = source.transform;
            Transform targetRootTransform = target.transform;

            basicData.sourceRootPose = new KTransform(sourceRootTransform);
            basicData.targetRoot = animator.BindSceneTransform(targetRootTransform);

            KTransformChain sourceChainT = RetargetUtility.GetTransformChain(source, feature.sourceRig,
                feature.sourceChain);
            KTransformChain targetChainT = RetargetUtility.GetTransformChain(target, feature.targetRig,
                feature.targetChain);

            if (!sourceChainT.IsValid() || !targetChainT.IsValid())
            {
                Debug.LogError("RootPelvisRetargetJob: Source or Target chains are NULL!");
                return;
            }

            RetargetJobUtility.SetupSceneAtomChain(animator, ref sourceChain, sourceChainT.transformChain.ToArray(),
                sourceRootTransform);
            RetargetJobUtility.SetupStreamAtomChain(animator, ref targetChain, targetChainT.transformChain.ToArray(),
                targetRootTransform);

            float scale = RetargetJobUtility.ComputeBodyScale(source.GetRigTransforms(), sourceRootTransform,
                target.GetRigTransforms(), targetRootTransform, 1f);

            basicData.scale = scale;
        }

        public void UpdateSourceRootPose(KTransform sourceRootPose)
        {
            basicData.sourceRootPose = sourceRootPose;
        }

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature featureAsset)
        {
            var feature = featureAsset as RootPelvisRetargetFeature;
            if (feature == null) return;

            basicData.featureWeight = feature.featureWeight;
            basicData.scaleWeight = feature.scaleWeight;
            basicData.translationWeight = feature.GetTranslationBlend();
            basicData.offset = feature.offset;

            playable.SetJobData(this);
        }

        public void Dispose()
        {
            if (sourceChain.IsCreated) sourceChain.Dispose();
            if (targetChain.IsCreated) targetChain.Dispose();
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            RetargetJobUtility.BasicRetarget(stream, sourceChain, targetChain, basicData);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
}
