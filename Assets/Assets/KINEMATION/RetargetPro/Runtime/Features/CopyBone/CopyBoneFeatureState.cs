// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.CopyBone
{
    public class CopyBoneFeatureState : RetargetFeatureState
    {
        protected KTransformChain _copyToChain;
        protected KTransformChain _copyFromChain;
        protected CopyBoneFeatureSettings _asset;

        public override bool IsValid()
        {
            return _copyToChain != null && _copyToChain.IsValid() &&
                   _copyFromChain != null && _copyFromChain.IsValid() &&
                   _copyToChain.transformChain.Count == _copyFromChain.transformChain.Count;
        }
        
        protected override void Initialize(RetargetFeature newAsset)
        {
            _asset = newAsset as CopyBoneFeatureSettings;
            if (_asset == null) return;
            
            _copyFromChain = RetargetUtility.GetTransformChain(sourceRigComponent, _asset.sourceRig, 
                _asset.copyFrom);
            
            _copyToChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, 
                _asset.copyTo);
        }

        public override void Retarget(float time = 0f)
        {
            if (!IsValid())
            {
                return;
            }

            float weight = _asset.GetFeatureWeight(time);
            if (weight <= 0f)
            {
                return;
            }

            int count = _copyToChain.transformChain.Count;
            Transform sourceRoot = GetSourceRoot();
            Transform targetRoot = GetTargetRoot();

            for (int i = 0; i < count; i++)
            {
                var copyFrom = _copyFromChain.transformChain[i];
                var copyTo = _copyToChain.transformChain[i];

                Vector3 position = targetRoot.TransformPoint(sourceRoot.InverseTransformPoint(copyFrom.position));
                Quaternion rotation = targetRoot.rotation * Quaternion.Inverse(sourceRoot.rotation) * copyFrom.rotation;

                copyTo.position = Vector3.Lerp(copyTo.position, position, weight);
                copyTo.rotation = Quaternion.Slerp(copyTo.rotation, rotation, weight);
                copyTo.localScale = Vector3.Lerp(copyTo.localScale, copyFrom.localScale, weight);
            }
        }

#if UNITY_EDITOR
        protected override void OnBoneRenderingSceneGUI()
        {
            RenderSourceAndTargetBoneChains(_copyFromChain, _copyToChain, TargetBoneChainColor);
        }
#endif
    }
}
