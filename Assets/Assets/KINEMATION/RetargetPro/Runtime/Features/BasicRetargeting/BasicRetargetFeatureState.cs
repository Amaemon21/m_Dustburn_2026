// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting
{
    public class BasicRetargetFeatureState : RetargetFeatureState
    {
        private BasicRetargetFeature _asset;
        
        protected KTransformChain sourceChain;
        protected KTransformChain targetChain;
        
        protected float chainScale = 1f;
        protected float featureWeight = 1f;
        protected bool _chainSizeMatch;

        private List<Vector3> _localTargetPositions;
        private List<Quaternion> _localTargetRotations;
         
        protected void RetargetBone(int sourceIndex, int targetIndex)
        {
            RetargetBone((float) sourceIndex, targetIndex);
        }

        private KTransform GetSourceComponentPose(int sourceIndex)
        {
            Transform sourceRoot = GetSourceRoot();
            Transform sourceBone = sourceChain.transformChain[sourceIndex];
            return new KTransform(
                sourceRoot.InverseTransformPoint(sourceBone.position),
                Quaternion.Inverse(sourceRoot.rotation) * sourceBone.rotation,
                Vector3.one);
        }

        private void RetargetBone(float sourcePosition, int targetIndex)
        {
            int lastSourceIndex = sourceChain.transformChain.Count - 1;
            sourcePosition = Mathf.Clamp(sourcePosition, 0f, lastSourceIndex);
            int sourceIndex = Mathf.FloorToInt(sourcePosition);
            int nextSourceIndex = Mathf.Min(sourceIndex + 1, lastSourceIndex);
            float sourceBlend = sourcePosition - sourceIndex;

            KTransform cachedSourcePose = KTransform.Lerp(sourceChain.cachedTransforms[sourceIndex],
                sourceChain.cachedTransforms[nextSourceIndex], sourceBlend);
            KTransform sourcePose = KTransform.Lerp(GetSourceComponentPose(sourceIndex),
                GetSourceComponentPose(nextSourceIndex), sourceBlend);
            var cachedTargetPose = targetChain.cachedTransforms[targetIndex];
            Transform targetBone = targetChain.transformChain[targetIndex];
            Transform targetRoot = GetTargetRoot();
            
            float scale = Mathf.Lerp(1f, chainScale, _asset.scaleWeight);
            
            // Compute the delta dynamically, as bone sizes might differ.
            Quaternion orientationDelta = Quaternion.Inverse(cachedSourcePose.rotation) * cachedTargetPose.rotation;
            Quaternion targetRotation = targetRoot.rotation * sourcePose.rotation * orientationDelta;

            targetBone.rotation = targetRotation;
            
            targetBone.localRotation = Quaternion.Slerp(_localTargetRotations[targetIndex], 
                targetBone.localRotation, featureWeight);
            
            // Apply translation.
            Vector3 sourceLocal = sourcePose.position - cachedSourcePose.position;
            sourceLocal *= scale;

            // Apply component space additive animation.
            Vector3 targetLocal = targetChain.cachedTransforms[targetIndex].position;
            targetLocal = targetRoot.TransformPoint(targetLocal + sourceLocal);
            targetBone.position = targetLocal;

            KAnimationMath.MoveInSpace(targetRoot, targetBone, _asset.offset, 1f);
            
            // Blend with the default pose.
            targetBone.localPosition = Vector3.Lerp(_localTargetPositions[targetIndex], targetBone.localPosition,
                _asset.GetTranslationBlend() * featureWeight);
        }
        
        protected override void Initialize(RetargetFeature newAsset)
        {
            _asset = newAsset as BasicRetargetFeature;
            
            if (_asset == null) return;

            // Initialize chain references.
            sourceChain = RetargetUtility.GetTransformChain(sourceRigComponent, _asset.sourceRig, _asset.sourceChain);
            targetChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, _asset.targetChain);

            if (sourceChain == null || targetChain == null || !sourceChain.IsValid() || !targetChain.IsValid())
            {
                return;
            }

            _chainSizeMatch = sourceChain.transformChain.Count == targetChain.transformChain.Count;

            // Cache the current pose for both characters.
            sourceChain.CacheTransforms(ESpaceType.ComponentSpace, GetSourceRoot());
            targetChain.CacheTransforms(ESpaceType.ComponentSpace, GetTargetRoot());
            
            _localTargetPositions = new List<Vector3>();
            _localTargetRotations = new List<Quaternion>();
            
            float sourceChainLength = 0f;
            float targetChainLength = 0f;
            
            int count = targetChain.transformChain.Count;
            for (int i = 0; i < count; i++)
            {
                Transform targetBone = targetChain.transformChain[i];
                _localTargetPositions.Add(targetBone.localPosition);
                _localTargetRotations.Add(targetBone.localRotation);
                
                // If we have only one bone in the chain, use mesh space delta.
                if (count == 1)
                {
                    Vector3 targetMS = GetTargetRoot().InverseTransformPoint(targetBone.position);
                    targetChainLength = targetMS.magnitude;
                }
                else if (i > 0)
                {
                    targetChainLength += (targetBone.position - targetChain.transformChain[i - 1].position).magnitude;
                }
            }

            count = sourceChain.transformChain.Count;
            for (int i = 0; i < count; i++)
            {
                Transform sourceBone = sourceChain.transformChain[i];
                
                if (count == 1)
                {
                    Vector3 sourceMS = GetSourceRoot().InverseTransformPoint(sourceBone.position);
                    sourceChainLength = sourceMS.magnitude;
                }
                else if (i > 0)
                {
                    sourceChainLength += (sourceBone.position - sourceChain.transformChain[i - 1].position).magnitude;
                }
            }

            if (Mathf.Approximately(sourceChainLength, 0f))
            {
                chainScale = 1f;
                return;
            }

            chainScale = targetChainLength / sourceChainLength;
        }

        public override bool IsValid()
        {
            return sourceRigComponent != null &&
                   targetRigComponent != null &&
                   sourceChain != null &&
                   targetChain != null &&
                   sourceChain.IsValid() &&
                   targetChain.IsValid();
        }

        public int SourceChainCount => sourceChain?.transformChain?.Count ?? 0;
        public int TargetChainCount => targetChain?.transformChain?.Count ?? 0;
        public bool HasMatchingChainSizes => _chainSizeMatch;

        // Used when source and target chain size is matched.
        protected void RetargetMatchedBones()
        {
            int count = targetChain.transformChain.Count;
            // Perform a simple rotation retargeting.
            for (int i = 0; i < count; i++) RetargetBone(i, i);
        }

        // Used when source and target chain sizes do not match.
        protected void RetargetDistributedBones()
        {
            int sourceCount = sourceChain.transformChain.Count;
            int targetCount = targetChain.transformChain.Count;

            for (int i = 0; i < targetCount; i++)
            {
                float normalizedIndex = targetCount == 1 ? 0f : (float) i / (targetCount - 1);
                RetargetBone((sourceCount - 1) * normalizedIndex, i);
            }
        }

        public override void Retarget(float time = 0f)
        {
            featureWeight = _asset.GetFeatureWeight(time);

            if (_chainSizeMatch)
            {
                RetargetMatchedBones();
                return;
            }

            RetargetDistributedBones();
        }
        
#if UNITY_EDITOR
        protected override void OnBoneRenderingSceneGUI()
        {
            RenderSourceAndTargetBoneChains(sourceChain, targetChain, TargetBoneChainColor);
        }
#endif
    }
}
