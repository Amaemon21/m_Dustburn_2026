// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;

using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.RetargetPro.Runtime.Features.IKRetargeting
{
    public struct IKRetargetJob : IRetargetJob, IAnimationJob
    {
        public NativeArray<RetargetSceneAtom> sourceChain;
        public NativeArray<IKRetargetStreamAtom> targetChain;
        public NativeArray<Vector3> jointForwardAxes;
        public NativeArray<Vector3> jointUpAxes;
        
        public IKRetargetData ikData;
        
        public void Setup(RetargetFeature feature, Animator animator, KRigComponent source, KRigComponent target)
        {
            var ikFeature = feature as IKRetargetFeature;
            if (ikFeature == null) return;

            Transform sourceRootTransform = source.transform;
            Transform targetRootTransform = target.transform;
            
            ikData.basicData.sourceRootPose = new KTransform(sourceRootTransform);
            ikData.basicData.targetRoot = animator.BindSceneTransform(targetRootTransform);

            KTransformChain sourceChainT = RetargetUtility.GetTransformChain(source, ikFeature.sourceRig, 
                ikFeature.sourceChain);
            KTransformChain targetChainT =  RetargetUtility.GetTransformChain(target, ikFeature.targetRig, 
                ikFeature.targetChain);
            
            if(!sourceChainT.IsValid() || !targetChainT.IsValid())
            {
                Debug.LogError("IKRetargetJob: Source or Target chains are NULL!");
                return;
            }
            
            RetargetJobUtility.SetupSceneAtomChain(animator, ref sourceChain, sourceChainT.transformChain.ToArray(), 
                sourceRootTransform);
            RetargetJobUtility.SetupStreamIkAtomChain(animator, ref targetChain, targetChainT.transformChain.ToArray(),
                targetRootTransform);

            int targetCount = targetChainT.transformChain.Count;
            jointForwardAxes = new NativeArray<Vector3>(targetCount, Allocator.Persistent);
            jointUpAxes = new NativeArray<Vector3>(targetCount, Allocator.Persistent);
            for (int i = 0; i < targetCount; i++)
            {
                Transform joint = targetChainT.transformChain[i];
                jointForwardAxes[i] = RetargetJobUtility.DetectClosestLocalAxis(joint.rotation,
                    targetRootTransform.forward);
                jointUpAxes[i] = RetargetJobUtility.DetectClosestLocalAxis(joint.rotation,
                    targetRootTransform.up);
            }

            float sourceLength = sourceChainT.GetLength(sourceRootTransform);
            float targetLength = targetChainT.GetLength(targetRootTransform);
            
            if (Mathf.Approximately(sourceLength, 0f))
            {
                ikData.basicData.scale = 1f;
                return;
            }
            
            ikData.basicData.scale = targetLength / sourceLength;
        }

        public void UpdateSourceRootPose(KTransform sourceRootPose)
        {
            ikData.basicData.sourceRootPose = sourceRootPose;
        }

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature feature)
        {
            var ikFeature = feature as IKRetargetFeature;
            if (ikFeature == null) return;

            ikData.basicData.featureWeight = ikFeature.featureWeight;
            ikData.basicData.scaleWeight = ikFeature.scaleWeight;
            ikData.basicData.translationWeight = ikFeature.GetTranslationBlend();
            ikData.basicData.offset = ikFeature.offset;

            ikData.effectorOffset = ikFeature.effectorOffset;
            ikData.effectorSpace = ikFeature.effectorSpace;
            ikData.jointOffset = ikFeature.jointOffset;
            ikData.ikWeight = ikFeature.ikWeight;
            ikData.poleWeight = ikFeature.poleWeight;
            ikData.jointIndex = targetChain.IsCreated && targetChain.Length > 0
                ? Mathf.Clamp(ikFeature.ResolveJointBoneIndex(), 0, targetChain.Length - 1)
                : -1;
            if (ikData.jointIndex >= 0 && jointForwardAxes.IsCreated && jointUpAxes.IsCreated)
            {
                ikData.localJointForward = jointForwardAxes[ikData.jointIndex];
                ikData.localJointUp = jointUpAxes[ikData.jointIndex];
            }
            ikData.useChainIk = ikFeature.useChainIk ? (byte) 1 : (byte) 0;
            ikData.reachMultiplier = ikFeature.GetReachMultiplier();

            ikData.maxIterations = 16;
            ikData.tolerance = 0.001f;
            
            playable.SetJobData(this);
        }

        public void Dispose()
        {
            if (sourceChain.IsCreated) sourceChain.Dispose();
            if (targetChain.IsCreated) targetChain.Dispose();
            if (jointForwardAxes.IsCreated) jointForwardAxes.Dispose();
            if (jointUpAxes.IsCreated) jointUpAxes.Dispose();
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            RetargetJobUtility.BasicRetarget(stream, sourceChain, targetChain, ikData.basicData);

            if (Mathf.Approximately(ikData.ikWeight, 0f) || targetChain.Length < 3)
            {
                return;
            }

            if (targetChain.Length == 3 || ikData.useChainIk == 0)
            {
                RetargetJobUtility.SolveTwoBoneIK(stream, sourceChain, targetChain, ikData);
                return;
            }
            
            RetargetJobUtility.SolveChainIK(stream, sourceChain, targetChain, ikData);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
}
