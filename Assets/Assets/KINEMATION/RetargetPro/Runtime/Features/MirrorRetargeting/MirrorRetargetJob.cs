// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.RetargetPro.Runtime.Features.MirrorRetargeting
{
    public struct MirrorRetargetJob : IAnimationJob, IRetargetJob
    {
        private enum MirrorAxisType
        {
            X,
            Y,
            Z
        }

        private struct LocalPoseSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        private struct MirrorBoneMapping
        {
            public TransformStreamHandle sourceBone;
            public TransformStreamHandle targetBone;
            public Quaternion sourceParentRefRotation;
            public Quaternion sourceBoneRefRotation;
            public Quaternion targetParentRefRotation;
            public Quaternion targetBoneRefRotation;
        }

        private NativeArray<MirrorBoneMapping> _boneMappings;
        private NativeArray<LocalPoseSnapshot> _sourcePoseSnapshots;
        private NativeArray<LocalPoseSnapshot> _targetPoseSnapshots;
        private Vector3 _axis;
        private float _featureWeight;

        public void Setup(RetargetFeature feature, Animator animator, KRigComponent source, KRigComponent target)
        {
            var mirrorFeature = feature as MirrorRetargetFeature;
            if (mirrorFeature == null)
            {
                return;
            }

            KTransformChain targetChain = RetargetUtility.GetTransformChain(target, mirrorFeature.targetRig,
                mirrorFeature.targetBoneChain);
            if (targetChain == null || !targetChain.IsValid())
            {
                return;
            }

            Transform targetRoot = target.transform;
            Quaternion inverseRootRotation = Quaternion.Inverse(targetRoot.rotation);
            int chainCount = targetChain.transformChain.Count;

            MirrorBoneMapping[] mappings = new MirrorBoneMapping[chainCount];
            int mappingCount = 0;
            for (int i = 0; i < chainCount; i++)
            {
                Transform targetBone = targetChain.transformChain[i];
                if (targetBone == null)
                {
                    continue;
                }

                Transform sourceBone = ResolveSourceBone(target, targetBone);
                if (sourceBone == null)
                {
                    continue;
                }

                mappings[mappingCount++] = new MirrorBoneMapping
                {
                    sourceBone = animator.BindStreamTransform(sourceBone),
                    targetBone = animator.BindStreamTransform(targetBone),
                    sourceParentRefRotation = GetComponentSpaceRefRotation(sourceBone.parent, inverseRootRotation),
                    sourceBoneRefRotation = GetComponentSpaceRefRotation(sourceBone, inverseRootRotation),
                    targetParentRefRotation = GetComponentSpaceRefRotation(targetBone.parent, inverseRootRotation),
                    targetBoneRefRotation = GetComponentSpaceRefRotation(targetBone, inverseRootRotation)
                };
            }

            if (mappingCount == 0)
            {
                return;
            }

            _boneMappings = new NativeArray<MirrorBoneMapping>(mappingCount, Allocator.Persistent);
            _sourcePoseSnapshots = new NativeArray<LocalPoseSnapshot>(mappingCount, Allocator.Persistent);
            _targetPoseSnapshots = new NativeArray<LocalPoseSnapshot>(mappingCount, Allocator.Persistent);
            for (int i = 0; i < mappingCount; i++)
            {
                _boneMappings[i] = mappings[i];
            }
        }

        public void UpdateSourceRootPose(KTransform sourceRootPose)
        {
        }

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature feature)
        {
            var mirrorFeature = feature as MirrorRetargetFeature;
            if (mirrorFeature == null)
            {
                return;
            }

            _axis = mirrorFeature.axis;
            _featureWeight = mirrorFeature.featureWeight;
            playable.SetJobData(this);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_boneMappings.IsCreated || _featureWeight <= 0f)
            {
                return;
            }

            MirrorAxisType axisType = ResolveAxisType(_axis);
            int count = _boneMappings.Length;

            for (int i = 0; i < count; i++)
            {
                MirrorBoneMapping mapping = _boneMappings[i];
                _sourcePoseSnapshots[i] = GetLocalPose(stream, mapping.sourceBone);
                _targetPoseSnapshots[i] = GetLocalPose(stream, mapping.targetBone);
            }

            for (int i = 0; i < count; i++)
            {
                MirrorBoneMapping mapping = _boneMappings[i];
                LocalPoseSnapshot sourcePose = _sourcePoseSnapshots[i];
                LocalPoseSnapshot targetPose = _targetPoseSnapshots[i];
                LocalPoseSnapshot mirroredPose = MirrorTransform(sourcePose, mapping, axisType);

                mapping.targetBone.SetLocalPosition(stream,
                    Vector3.Lerp(targetPose.position, mirroredPose.position, _featureWeight));
                mapping.targetBone.SetLocalRotation(stream,
                    Quaternion.Slerp(targetPose.rotation, mirroredPose.rotation, _featureWeight));
            }
        }

        public void Dispose()
        {
            if (_boneMappings.IsCreated) _boneMappings.Dispose();
            if (_sourcePoseSnapshots.IsCreated) _sourcePoseSnapshots.Dispose();
            if (_targetPoseSnapshots.IsCreated) _targetPoseSnapshots.Dispose();
        }

        private static Transform ResolveSourceBone(KRigComponent target, Transform targetBone)
        {
            string mirroredBoneName = MirrorRetargetFeature.GetMirroredBoneName(targetBone.name);
            if (!string.IsNullOrEmpty(mirroredBoneName))
            {
                Transform mirroredBone = target.GetRigTransform(mirroredBoneName);
                if (mirroredBone != null)
                {
                    return mirroredBone;
                }
            }

            return targetBone;
        }

        private static Quaternion GetComponentSpaceRefRotation(Transform bone, Quaternion inverseRootRotation)
        {
            return bone == null ? Quaternion.identity : inverseRootRotation * bone.rotation;
        }

        private static LocalPoseSnapshot GetLocalPose(AnimationStream stream, TransformStreamHandle bone)
        {
            return new LocalPoseSnapshot
            {
                position = bone.GetLocalPosition(stream),
                rotation = bone.GetLocalRotation(stream)
            };
        }

        private static LocalPoseSnapshot MirrorTransform(LocalPoseSnapshot sourcePose, MirrorBoneMapping mapping,
            MirrorAxisType axisType)
        {
            Vector3 translation = mapping.sourceParentRefRotation * sourcePose.position;
            translation = MirrorVector(translation, axisType);
            translation = Quaternion.Inverse(mapping.targetParentRefRotation) * translation;

            Quaternion rotation = mapping.sourceParentRefRotation * sourcePose.rotation;
            rotation = MirrorQuat(rotation, axisType);
            rotation *= Quaternion.Inverse(MirrorQuat(mapping.sourceBoneRefRotation, axisType)) *
                        mapping.targetBoneRefRotation;
            rotation = Quaternion.Inverse(mapping.targetParentRefRotation) * rotation;
            rotation = KMath.NormalizeSafe(rotation);

            return new LocalPoseSnapshot
            {
                position = translation,
                rotation = rotation
            };
        }

        private static MirrorAxisType ResolveAxisType(Vector3 axis)
        {
            axis = MirrorRetargetFeature.NormalizeAxis(axis);

            float absX = Mathf.Abs(axis.x);
            float absY = Mathf.Abs(axis.y);
            float absZ = Mathf.Abs(axis.z);

            if (absX >= absY && absX >= absZ)
            {
                return MirrorAxisType.X;
            }

            if (absZ >= absY)
            {
                return MirrorAxisType.Z;
            }

            return MirrorAxisType.Y;
        }

        private static Vector3 MirrorVector(Vector3 value, MirrorAxisType axisType)
        {
            switch (axisType)
            {
                case MirrorAxisType.X:
                    value.x = -value.x;
                    break;
                case MirrorAxisType.Y:
                    value.y = -value.y;
                    break;
                case MirrorAxisType.Z:
                    value.z = -value.z;
                    break;
            }

            return value;
        }

        private static Quaternion MirrorQuat(Quaternion rotation, MirrorAxisType axisType)
        {
            switch (axisType)
            {
                case MirrorAxisType.X:
                    rotation.y = -rotation.y;
                    rotation.z = -rotation.z;
                    break;
                case MirrorAxisType.Y:
                    rotation.x = -rotation.x;
                    rotation.z = -rotation.z;
                    break;
                case MirrorAxisType.Z:
                    rotation.x = -rotation.x;
                    rotation.y = -rotation.y;
                    break;
            }

            return KMath.NormalizeSafe(rotation);
        }
    }
}
