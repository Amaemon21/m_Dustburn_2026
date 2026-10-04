// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.TransformRetargeting
{
    public class TransformRetargetFeatureState : RetargetFeatureState
    {
        private TransformRetargetFeature _asset;
        private KTransformChain _targetChain;

        protected override void Initialize(RetargetFeature newAsset)
        {
            _asset = newAsset as TransformRetargetFeature;
            if (_asset == null)
            {
                return;
            }
            
            _targetChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig,
                _asset.targetChain);
        }

        public override bool IsValid()
        {
            return _asset != null && _targetChain != null && _targetChain.IsValid();
        }

        public override void Retarget(float time = 0f)
        {
            float weight = _asset.GetFeatureWeight(time);
            
            if (!IsValid() || weight <= 0f)
            {
                return;
            }
            
            foreach (Transform targetBone in _targetChain.transformChain)
            {
                ApplyPosition(targetBone, weight);
                ApplyRotation(targetBone, weight);
                ApplyScale(targetBone, weight);
            }
        }

        private void ApplyPosition(Transform targetBone, float weight)
        {
            if (_asset.positionMode == EModifyMode.Ignore)
            {
                return;
            }

            Vector3 position = _asset.position;

            if (_asset.positionMode == EModifyMode.Add)
            {
                if (_asset.positionSpace == ESpaceType.BoneSpace)
                {
                    targetBone.position += (targetBone.TransformPoint(position) - targetBone.position) * weight;
                    return;
                }

                if (_asset.positionSpace == ESpaceType.ParentBoneSpace)
                {
                    targetBone.localPosition = Vector3.Lerp(targetBone.localPosition,
                        targetBone.localPosition + position, weight);
                    return;
                }

                if (_asset.positionSpace == ESpaceType.ComponentSpace)
                {
                    Transform root = GetTargetRoot();
                    targetBone.position += (root.TransformPoint(position) - root.position) * weight;
                    return;
                }

                targetBone.position = Vector3.Lerp(targetBone.position, targetBone.position + position, weight);
                return;
            }

            if (_asset.positionSpace is ESpaceType.BoneSpace or ESpaceType.ParentBoneSpace)
            {
                targetBone.localPosition = Vector3.Lerp(targetBone.localPosition, position, weight);
                return;
            }

            if (_asset.positionSpace == ESpaceType.ComponentSpace)
            {
                position = GetTargetRoot().TransformPoint(position);
            }

            targetBone.position = Vector3.Lerp(targetBone.position, position, weight);
        }

        private void ApplyRotation(Transform targetBone, float weight)
        {
            if (_asset.rotationMode == EModifyMode.Ignore)
            {
                return;
            }

            Quaternion rotation = _asset.rotation;

            if (_asset.rotationMode == EModifyMode.Add)
            {
                if (_asset.rotationSpace == ESpaceType.BoneSpace)
                {
                    targetBone.rotation = Quaternion.Slerp(targetBone.rotation, targetBone.rotation * rotation,
                        weight);
                    return;
                }

                if (_asset.rotationSpace == ESpaceType.ParentBoneSpace)
                {
                    targetBone.localRotation = Quaternion.Slerp(targetBone.localRotation,
                        targetBone.localRotation * rotation, weight);
                    return;
                }

                if (_asset.rotationSpace == ESpaceType.ComponentSpace)
                {
                    Quaternion rootRotation = GetTargetRoot().rotation;
                    Quaternion targetRotation = rootRotation * rotation * (Quaternion.Inverse(rootRotation) *
                                                                             targetBone.rotation);
                    targetBone.rotation = Quaternion.Slerp(targetBone.rotation, targetRotation, weight);
                    return;
                }

                targetBone.rotation = Quaternion.Slerp(targetBone.rotation, targetBone.rotation * rotation, weight);
                return;
            }

            if (_asset.rotationSpace is ESpaceType.BoneSpace or ESpaceType.ParentBoneSpace)
            {
                targetBone.localRotation = Quaternion.Slerp(targetBone.localRotation, rotation, weight);
                return;
            }

            if (_asset.rotationSpace == ESpaceType.ComponentSpace)
            {
                rotation = GetTargetRoot().rotation * rotation;
            }

            targetBone.rotation = Quaternion.Slerp(targetBone.rotation, rotation, weight);
        }

        private void ApplyScale(Transform targetBone, float weight)
        {
            if (_asset.scaleMode == EModifyMode.Ignore)
            {
                return;
            }

            if (_asset.scaleMode == EModifyMode.Add)
            {
                Vector3 currentScale = targetBone.localScale;
                targetBone.localScale = Vector3.Lerp(currentScale, Vector3.Scale(currentScale, _asset.scale), weight);
                return;
            }

            targetBone.localScale = Vector3.Lerp(targetBone.localScale, _asset.scale, weight);
        }

#if UNITY_EDITOR
        protected override void OnBoneRenderingSceneGUI()
        {
            RenderBoneChain(_targetChain, Color.yellow);
        }
#endif
    }
}
