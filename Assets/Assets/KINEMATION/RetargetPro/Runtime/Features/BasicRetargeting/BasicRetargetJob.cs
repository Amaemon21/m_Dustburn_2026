// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting
{
    public struct BasicRetargetJob : IAnimationJob, IRetargetJob
    {
        public NativeArray<RetargetSceneAtom> sourceChain;
        public NativeArray<RetargetStreamAtom> targetChain;
        
        public BasicRetargetData basicData;
        
        public void Setup(RetargetFeature feature, Animator animator, KRigComponent source, KRigComponent target)
        {
            var basicFeature = feature as BasicRetargetFeature;
            if (basicFeature == null) return;
            
            Transform sourceRootTransform = source.transform;
            Transform targetRootTransform = target.transform;
            
            basicData.sourceRootPose = new KTransform(sourceRootTransform);
            basicData.targetRoot = animator.BindSceneTransform(targetRootTransform);
            
            KTransformChain sourceChainT = RetargetUtility.GetTransformChain(source, basicFeature.sourceRig, 
                basicFeature.sourceChain);
            KTransformChain targetChainT =  RetargetUtility.GetTransformChain(target, basicFeature.targetRig, 
                basicFeature.targetChain);
            
            if(!sourceChainT.IsValid() || !targetChainT.IsValid())
            {
                Debug.LogError("IKRetargetJob: Source or Target chains are NULL!");
                return;
            }
            
            RetargetJobUtility.SetupSceneAtomChain(animator, ref sourceChain, sourceChainT.transformChain.ToArray(), 
                sourceRootTransform);
            RetargetJobUtility.SetupStreamAtomChain(animator, ref targetChain, targetChainT.transformChain.ToArray(),
                targetRootTransform);

            float sourceLength = sourceChainT.GetLength(sourceRootTransform);
            float targetLength = targetChainT.GetLength(targetRootTransform);
            
            if (Mathf.Approximately(sourceLength, 0f))
            {
                basicData.scale = 1f;
                return;
            }
            
            basicData.scale = targetLength / sourceLength;
        }

        public void UpdateSourceRootPose(KTransform sourceRootPose)
        {
            basicData.sourceRootPose = sourceRootPose;
        }

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature feature)
        {
            var basicFeature = feature as BasicRetargetFeature;
            if (basicFeature == null) return;

            basicData.featureWeight = basicFeature.featureWeight;
            basicData.scaleWeight = basicFeature.scaleWeight;
            basicData.translationWeight = basicFeature.GetTranslationBlend();
            basicData.offset = basicFeature.offset;
            
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
