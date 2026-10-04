// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.RetargetPro.Runtime.Features.TransformRetargeting
{
    public struct TransformRetargetJob : IAnimationJob, IRetargetJob
    {
        private TransformStreamHandle _targetRoot;
        private NativeArray<TransformStreamHandle> _targetChain;

        private float _featureWeight;
        private Vector3 _position;
        private ESpaceType _positionSpace;
        private EModifyMode _positionMode;
        private Quaternion _rotation;
        private ESpaceType _rotationSpace;
        private EModifyMode _rotationMode;
        private Vector3 _scale;
        private EModifyMode _scaleMode;

        public void Setup(RetargetFeature feature, Animator animator, KRigComponent source, KRigComponent target)
        {
            var transformFeature = feature as TransformRetargetFeature;
            if (transformFeature == null)
            {
                return;
            }
            
            KTransformChain targetChain = RetargetUtility.GetTransformChain(target, transformFeature.targetRig,
                transformFeature.targetChain);
            if (targetChain == null || !targetChain.IsValid())
            {
                return;
            }

            _targetRoot = animator.BindStreamTransform(target.transform);
            int chainCount = targetChain.transformChain.Count;
            _targetChain = new NativeArray<TransformStreamHandle>(chainCount, Allocator.Persistent);

            for (int i = 0; i < chainCount; i++)
            {
                _targetChain[i] = animator.BindStreamTransform(targetChain.transformChain[i]);
            }
        }

        public void UpdateSourceRootPose(KTransform sourceRootPose)
        {
        }

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature feature)
        {
            var transformFeature = feature as TransformRetargetFeature;
            if (transformFeature == null)
            {
                return;
            }

            _featureWeight = transformFeature.featureWeight;
            _position = transformFeature.position;
            _positionSpace = transformFeature.positionSpace;
            _positionMode = transformFeature.positionMode;
            _rotation = transformFeature.rotation;
            _rotationSpace = transformFeature.rotationSpace;
            _rotationMode = transformFeature.rotationMode;
            _scale = transformFeature.scale;
            _scaleMode = transformFeature.scaleMode;

            playable.SetJobData(this);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (_featureWeight <= 0f || !_targetChain.IsCreated)
            {
                return;
            }

            int chainCount = _targetChain.Length;
            for (int i = 0; i < chainCount; i++)
            {
                TransformStreamHandle targetBone = _targetChain[i];
                if (!targetBone.IsValid(stream))
                {
                    continue;
                }

                ApplyPosition(stream, targetBone, _featureWeight);
                ApplyRotation(stream, targetBone, _featureWeight);
                ApplyScale(stream, targetBone, _featureWeight);
            }
        }

        public void Dispose()
        {
            if (_targetChain.IsCreated) _targetChain.Dispose();
        }

        private void ApplyPosition(AnimationStream stream, TransformStreamHandle targetBone, float weight)
        {
            if (_positionMode == EModifyMode.Ignore)
            {
                return;
            }

            Vector3 position = _position;
            if (_positionMode == EModifyMode.Add)
            {
                if (_positionSpace == ESpaceType.BoneSpace)
                {
                    KTransform bone = GetWorldPose(stream, targetBone);
                    targetBone.SetPosition(stream, bone.position + GetTransformVector(bone, position) * weight);
                    return;
                }

                if (_positionSpace == ESpaceType.ParentBoneSpace)
                {
                    Vector3 localPosition = targetBone.GetLocalPosition(stream);
                    targetBone.SetLocalPosition(stream, Vector3.Lerp(localPosition, localPosition + position, weight));
                    return;
                }

                if (_positionSpace == ESpaceType.ComponentSpace)
                {
                    KTransform root = GetWorldPose(stream, _targetRoot);
                    targetBone.SetPosition(stream,
                        targetBone.GetPosition(stream) + GetTransformVector(root, position) * weight);
                    return;
                }

                Vector3 worldPosition = targetBone.GetPosition(stream);
                targetBone.SetPosition(stream, Vector3.Lerp(worldPosition, worldPosition + position, weight));
                return;
            }

            if (_positionSpace is ESpaceType.BoneSpace or ESpaceType.ParentBoneSpace)
            {
                targetBone.SetLocalPosition(stream, Vector3.Lerp(targetBone.GetLocalPosition(stream), position, weight));
                return;
            }

            if (_positionSpace == ESpaceType.ComponentSpace)
            {
                position = GetWorldPose(stream, _targetRoot).TransformPoint(position, true);
            }

            targetBone.SetPosition(stream, Vector3.Lerp(targetBone.GetPosition(stream), position, weight));
        }

        private void ApplyRotation(AnimationStream stream, TransformStreamHandle targetBone, float weight)
        {
            if (_rotationMode == EModifyMode.Ignore)
            {
                return;
            }

            Quaternion rotation = _rotation;
            if (_rotationMode == EModifyMode.Add)
            {
                if (_rotationSpace == ESpaceType.BoneSpace)
                {
                    Quaternion current = targetBone.GetRotation(stream);
                    targetBone.SetRotation(stream, Quaternion.Slerp(current, current * rotation, weight));
                    return;
                }

                if (_rotationSpace == ESpaceType.ParentBoneSpace)
                {
                    Quaternion localRotation = targetBone.GetLocalRotation(stream);
                    targetBone.SetLocalRotation(stream,
                        Quaternion.Slerp(localRotation, localRotation * rotation, weight));
                    return;
                }

                if (_rotationSpace == ESpaceType.ComponentSpace)
                {
                    Quaternion rootRotation = _targetRoot.GetRotation(stream);
                    Quaternion current = targetBone.GetRotation(stream);
                    Quaternion targetRotation = rootRotation * rotation * (Quaternion.Inverse(rootRotation) * current);
                    targetBone.SetRotation(stream, Quaternion.Slerp(current, targetRotation, weight));
                    return;
                }

                Quaternion worldRotation = targetBone.GetRotation(stream);
                targetBone.SetRotation(stream, Quaternion.Slerp(worldRotation, worldRotation * rotation, weight));
                return;
            }

            if (_rotationSpace is ESpaceType.BoneSpace or ESpaceType.ParentBoneSpace)
            {
                targetBone.SetLocalRotation(stream,
                    Quaternion.Slerp(targetBone.GetLocalRotation(stream), rotation, weight));
                return;
            }

            if (_rotationSpace == ESpaceType.ComponentSpace)
            {
                rotation = _targetRoot.GetRotation(stream) * rotation;
            }

            targetBone.SetRotation(stream, Quaternion.Slerp(targetBone.GetRotation(stream), rotation, weight));
        }

        private void ApplyScale(AnimationStream stream, TransformStreamHandle targetBone, float weight)
        {
            if (_scaleMode == EModifyMode.Ignore)
            {
                return;
            }

            Vector3 currentScale = targetBone.GetLocalScale(stream);
            if (_scaleMode == EModifyMode.Add)
            {
                targetBone.SetLocalScale(stream,
                    Vector3.Lerp(currentScale, Vector3.Scale(currentScale, _scale), weight));
                return;
            }

            targetBone.SetLocalScale(stream, Vector3.Lerp(currentScale, _scale, weight));
        }

        private static KTransform GetWorldPose(AnimationStream stream, TransformStreamHandle handle)
        {
            return new KTransform
            {
                position = handle.GetPosition(stream),
                rotation = handle.GetRotation(stream),
                scale = handle.GetLocalScale(stream)
            };
        }

        private static Vector3 GetTransformVector(KTransform space, Vector3 localPosition)
        {
            return space.TransformPoint(localPosition, true) - space.position;
        }
    }
}
