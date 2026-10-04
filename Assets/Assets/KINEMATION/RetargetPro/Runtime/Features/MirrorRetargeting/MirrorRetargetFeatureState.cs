// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features.MirrorRetargeting
{
    public class MirrorRetargetFeatureState : RetargetFeatureState
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
            public Transform sourceBone;
            public Transform targetBone;
            public Quaternion sourceParentRefRotation;
            public Quaternion sourceBoneRefRotation;
            public Quaternion targetParentRefRotation;
            public Quaternion targetBoneRefRotation;
        }

        private MirrorRetargetFeature _asset;
        private KTransformChain _targetChain;
        private readonly List<MirrorBoneMapping> _boneMappings = new List<MirrorBoneMapping>();

        private readonly Dictionary<Transform, Quaternion> _componentSpaceRefRotations =
            new Dictionary<Transform, Quaternion>();

        private readonly Dictionary<Transform, LocalPoseSnapshot> _poseSnapshots =
            new Dictionary<Transform, LocalPoseSnapshot>();

        protected override void Initialize(RetargetFeature newAsset)
        {
            _asset = newAsset as MirrorRetargetFeature;
            if (_asset == null)
            {
                return;
            }

            _targetChain =
                RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, _asset.targetBoneChain);
            if (_targetChain == null || !_targetChain.IsValid())
            {
                return;
            }

            CacheReferenceRotations();
            BuildBoneMappings();
        }

        public override bool IsValid()
        {
            return _asset != null && _targetChain != null && _targetChain.IsValid() && _boneMappings.Count > 0;
        }

        public override void Retarget(float time = 0f)
        {
            float weight = _asset.GetFeatureWeight(time);
            if (!IsValid() || weight <= 0f)
            {
                return;
            }

            Vector3 mirrorAxis = MirrorRetargetFeature.NormalizeAxis(_asset.axis);
            CaptureCurrentPose();
            int count = _boneMappings.Count;
            for (int i = 0; i < count; i++)
            {
                MirrorBoneMapping mapping = _boneMappings[i];
                ApplyMirror(mapping, mirrorAxis, weight);
            }
        }

#if UNITY_EDITOR
        protected override void OnBoneRenderingSceneGUI()
        {
            RenderBoneChain(_targetChain, Color.green);
        }
#endif

        private void CacheReferenceRotations()
        {
            _componentSpaceRefRotations.Clear();
            Transform targetRoot = GetTargetRoot();
            Quaternion inverseRootRotation = Quaternion.Inverse(targetRoot.rotation);
            Transform[] rigTransforms = targetRigComponent.GetRigTransforms();
            int count = rigTransforms.Length;
            for (int i = 0; i < count; i++)
            {
                Transform bone = rigTransforms[i];
                if (bone == null)
                {
                    continue;
                }

                _componentSpaceRefRotations[bone] = inverseRootRotation * bone.rotation;
            }
        }

        private void BuildBoneMappings()
        {
            _boneMappings.Clear();
            int count = _targetChain.transformChain.Count;
            for (int i = 0; i < count; i++)
            {
                Transform targetBone = _targetChain.transformChain[i];
                if (targetBone == null)
                {
                    continue;
                }

                Transform sourceBone = ResolveSourceBone(targetBone);
                if (sourceBone == null)
                {
                    continue;
                }

                _boneMappings.Add(new MirrorBoneMapping
                {
                    sourceBone = sourceBone,
                    targetBone = targetBone,
                    sourceParentRefRotation = GetComponentSpaceRefRotation(sourceBone.parent),
                    sourceBoneRefRotation = GetComponentSpaceRefRotation(sourceBone),
                    targetParentRefRotation = GetComponentSpaceRefRotation(targetBone.parent),
                    targetBoneRefRotation = GetComponentSpaceRefRotation(targetBone)
                });
            }
        }

        private Transform ResolveSourceBone(Transform targetBone)
        {
            string mirroredBoneName = MirrorRetargetFeature.GetMirroredBoneName(targetBone.name);
            if (!string.IsNullOrEmpty(mirroredBoneName))
            {
                Transform mirroredBone = targetRigComponent.GetRigTransform(mirroredBoneName);
                if (mirroredBone != null)
                {
                    return mirroredBone;
                }
            }

            return targetBone;
        }

        private Quaternion GetComponentSpaceRefRotation(Transform bone)
        {
            if (bone == null)
            {
                return Quaternion.identity;
            }

            if (_componentSpaceRefRotations.TryGetValue(bone, out Quaternion refRotation))
            {
                return refRotation;
            }

            Transform targetRoot = GetTargetRoot();
            refRotation = Quaternion.Inverse(targetRoot.rotation) * bone.rotation;
            _componentSpaceRefRotations[bone] = refRotation;
            return refRotation;
        }

        private void CaptureCurrentPose()
        {
            _poseSnapshots.Clear();
            int count = _boneMappings.Count;
            for (int i = 0; i < count; i++)
            {
                MirrorBoneMapping mapping = _boneMappings[i];
                CacheCurrentPose(mapping.sourceBone);
                CacheCurrentPose(mapping.targetBone);
            }
        }

        private void CacheCurrentPose(Transform bone)
        {
            if (bone == null || _poseSnapshots.ContainsKey(bone))
            {
                return;
            }

            _poseSnapshots[bone] = new LocalPoseSnapshot
            {
                position = bone.localPosition, rotation = bone.localRotation
            };
        }

        private void ApplyMirror(MirrorBoneMapping mapping, Vector3 mirrorAxis, float weight)
        {
            if (!_poseSnapshots.TryGetValue(mapping.sourceBone, out LocalPoseSnapshot sourcePose) ||
                !_poseSnapshots.TryGetValue(mapping.targetBone, out LocalPoseSnapshot targetPose))
            {
                return;
            }

            LocalPoseSnapshot mirroredPose = MirrorTransform(sourcePose, mapping, mirrorAxis);
            Transform targetBone = mapping.targetBone;
            targetBone.localPosition = Vector3.Lerp(targetPose.position, mirroredPose.position, weight);
            targetBone.localRotation = Quaternion.Slerp(targetPose.rotation, mirroredPose.rotation, weight);
        }

        private static LocalPoseSnapshot MirrorTransform(LocalPoseSnapshot sourcePose, MirrorBoneMapping mapping,
            Vector3 mirrorAxis)
        {
            MirrorAxisType axisType = ResolveAxisType(mirrorAxis);
            Vector3 translation = mapping.sourceParentRefRotation * sourcePose.position;
            translation = MirrorVector(translation, axisType);
            translation = Quaternion.Inverse(mapping.targetParentRefRotation) * translation;
            Quaternion rotation = mapping.sourceParentRefRotation * sourcePose.rotation;
            rotation = MirrorQuat(rotation, axisType);
            rotation *= Quaternion.Inverse(MirrorQuat(mapping.sourceBoneRefRotation, axisType)) *
                        mapping.targetBoneRefRotation;
            rotation = Quaternion.Inverse(mapping.targetParentRefRotation) * rotation;
            rotation = KMath.NormalizeSafe(rotation);
            return new LocalPoseSnapshot { position = translation, rotation = rotation };
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
