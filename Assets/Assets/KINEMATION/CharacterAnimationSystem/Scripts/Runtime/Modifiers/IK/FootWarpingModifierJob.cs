// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct FootWarpingModifierJob : IAnimationJob, IAnimationModifierJob,
        IStrideJobComponent<FootWarpingModifierJob>
    {
        private static readonly int AnimatorStrideSpeed = Animator.StringToHash("StrideSpeed");
        private static readonly int AnimatorStrideWeight = Animator.StringToHash("StrideWeight");
        private static readonly int AnimatorStrideX = Animator.StringToHash("StrideX");
        private static readonly int AnimatorStrideY = Animator.StringToHash("StrideY");

        private struct FootWarpData
        {
            public Vector3 componentPosition;
            public KTransform offset;
        }

        private ModifierJobData _jobData;
        private IFootWarpingModifier _settings;
        private FootWarpingData _data;

        private bool _hasStrideSpeed;
        private bool _hasStrideWeight;
        private bool _hasStrideX;
        private bool _hasStrideY;
        private float _strideSpeed;
        private float _strideWeight;
        private Vector3 _strideDirection;
        private Quaternion _strideRotation;

        private Vector3 _prevRootPosition;
        private float _rightFootStride;
        private float _leftFootStride;
        private Vector3[] _trajectoryOffsets;
        private int _trajectoryRootIndex;
        private bool _isTrajectoryValid;
        private float _pelvisRoll;
        private float _rightFootYaw;
        private float _leftFootYaw;
        private float _leanYawAngle;

        private TransformStreamHandle _pelvisHandle;
        private TransformStreamHandle _leftFootIkHandle;
        private TransformStreamHandle _rightFootIkHandle;
        private TransformStreamHandle _leftHipHandle;
        private TransformStreamHandle _rightHipHandle;

        private Vector3 _velocity;

        private CharacterTrajectory _trajectoryComponent;

        private static Vector3 ComputeVelocity(Vector3 current, Vector3 previous, float deltaTime)
        {
            if (deltaTime <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            return (current - previous) / deltaTime;
        }

        private static float ComputeLegPelvisYOffset(Vector3 targetPelvis, Vector3 footComponent,
            float cachedLegLength)
        {
            float postWarpLength = Vector3.Distance(targetPelvis, footComponent);
            if (postWarpLength <= cachedLegLength + 0.0001f)
            {
                return 0f;
            }

            float currentHeight = targetPelvis.y - footComponent.y;
            if (currentHeight <= 0f)
            {
                return 0f;
            }

            float xDelta = footComponent.x - targetPelvis.x;
            float zDelta = footComponent.z - targetPelvis.z;
            float planarSqr = xDelta * xDelta + zDelta * zDelta;
            float compensatedHeight = Mathf.Sqrt(Mathf.Max(0f, cachedLegLength * cachedLegLength - planarSqr));
            return Mathf.Min(0f, compensatedHeight - currentHeight);
        }

        private void BindAnimatorParameters()
        {
            _hasStrideSpeed = false;
            _hasStrideWeight = false;
            _hasStrideX = false;
            _hasStrideY = false;

            Animator animator = _jobData.animator;
            if (animator != null)
            {
                foreach (var parameter in animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Float)
                    {
                        continue;
                    }

                    if (!_hasStrideSpeed && parameter.nameHash == AnimatorStrideSpeed) _hasStrideSpeed = true;
                    else if (!_hasStrideWeight && parameter.nameHash == AnimatorStrideWeight) _hasStrideWeight = true;
                    else if (!_hasStrideX && parameter.nameHash == AnimatorStrideX) _hasStrideX = true;
                    else if (!_hasStrideY && parameter.nameHash == AnimatorStrideY) _hasStrideY = true;
                }
            }

            _strideSpeed = 0f;
            _strideWeight = 1f;
            _strideDirection = Vector3.zero;
            _strideRotation = Quaternion.identity;
        }

        private void BindHandles()
        {
            LowerBodyBones bones = _data.bones;
            _pelvisHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.pelvis);
            _leftFootIkHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.leftFootIkTarget);
            _rightFootIkHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.rightFootIkTarget);

            Transform leftFoot = _jobData.skeleton?.GetBoneTransform(bones.leftFoot.name);
            Transform rightFoot = _jobData.skeleton?.GetBoneTransform(bones.rightFoot.name);
            
            Transform leftHip = leftFoot?.parent?.parent;
            if (leftHip != null && _jobData.animator != null)
            {
                _leftHipHandle = _jobData.animator.BindStreamTransform(leftHip);
            }

            Transform rightHip = rightFoot?.parent?.parent;
            if (rightHip != null && _jobData.animator != null)
            {
                _rightHipHandle = _jobData.animator.BindStreamTransform(rightHip);
            }
        }

        private void UpdateTrajectoryCache()
        {
            if (_trajectoryComponent == null || !_trajectoryComponent.isActiveAndEnabled)
            {
                _trajectoryRootIndex = 0;
                _isTrajectoryValid = false;
                return;
            }

            Vector3[] sourceOffsets = _trajectoryComponent.TrajectoryOffsets;
            if (sourceOffsets == null || sourceOffsets.Length == 0)
            {
                _trajectoryRootIndex = 0;
                _isTrajectoryValid = false;
                return;
            }

            if (_trajectoryOffsets == null || _trajectoryOffsets.Length != sourceOffsets.Length)
            {
                _trajectoryOffsets = new Vector3[sourceOffsets.Length];
            }

            System.Array.Copy(sourceOffsets, _trajectoryOffsets, sourceOffsets.Length);
            _trajectoryRootIndex = _trajectoryComponent.RootIndex;
            _isTrajectoryValid = true;
        }

        private void WarpFootTarget(AnimationStream stream, KTransform movementTransform,
            TransformStreamHandle footHandle, KTransform footTransform, float targetStride, float weight,
            ref float stride)
        {
            if (!footHandle.IsValid(stream))
            {
                return;
            }

            stride = KMath.FloatInterp(stride, Mathf.Min(targetStride, 1f), _data.strideSmoothing, stream.deltaTime);
            footTransform.position.z *= Mathf.Lerp(1f, stride, weight);
            footTransform = movementTransform.GetWorldTransform(footTransform, false);
            footHandle.SetPosition(stream, footTransform.position);
        }

        private void ProcessVelocityWarping(AnimationStream stream, in KTransform rootTransform)
        {
            KTransform movementTransform = rootTransform;
            movementTransform.rotation *= _strideRotation;

            KTransform rightFootWorld = KAnimationMath.GetTransform(stream, _rightFootIkHandle);
            KTransform leftFootWorld = KAnimationMath.GetTransform(stream, _leftFootIkHandle);

            KTransform rightFootMovement = movementTransform.GetRelativeTransform(rightFootWorld, false);
            KTransform leftFootMovement = movementTransform.GetRelativeTransform(leftFootWorld, false);

            float targetStride = 1f;
            if (_strideSpeed > float.Epsilon)
            {
                targetStride = _velocity.magnitude / _strideSpeed;
                targetStride = Mathf.Max(_data.minStrideScale, targetStride);
            }

            float warpingWeight = _jobData.weight * _strideWeight;

            WarpFootTarget(stream, movementTransform, _rightFootIkHandle, rightFootMovement,
                targetStride, warpingWeight, ref _rightFootStride);

            WarpFootTarget(stream, movementTransform, _leftFootIkHandle, leftFootMovement,
                targetStride, warpingWeight, ref _leftFootStride);

            _prevRootPosition = rootTransform.position;
        }

        private FootWarpData ComputeFootWarp(AnimationStream stream, in KTransform rootTransform,
            in TransformStreamHandle footHandle, ref float footYaw)
        {
            FootWarpData footWarp = new FootWarpData()
            {
                offset = KTransform.Identity
            };

            KTransform footTransform = KAnimationMath.GetTransform(stream, footHandle);
            footWarp.componentPosition = rootTransform.InverseTransformPoint(footTransform.position, false);

            KTransform velocitySpace = rootTransform;
            velocitySpace.rotation = rootTransform.rotation * _strideRotation;

            Vector3 localFoot = velocitySpace.InverseTransformPoint(footTransform.position, false);
            float targetDistance = Mathf.Abs(localFoot.z);

            Vector3[] trajectoryOffsets = _trajectoryOffsets;
            _trajectoryRootIndex = Mathf.Clamp(_trajectoryRootIndex, 0, trajectoryOffsets.Length - 1);
            Vector3 sampledPoint = rootTransform.position;
            float accumulated = 0f;

            bool isFuture = localFoot.z >= 0f;
            int step = isFuture ? 1 : -1;
            int endBound = isFuture ? trajectoryOffsets.Length - 1 : 0;

            for (int i = _trajectoryRootIndex; i != endBound; i += step)
            {
                int nextIndex = i + step;
                Vector3 a = rootTransform.position + trajectoryOffsets[i];
                Vector3 b = rootTransform.position + trajectoryOffsets[nextIndex];
                float segmentLength = Vector3.Distance(a, b);

                if (accumulated + segmentLength >= targetDistance || nextIndex == endBound)
                {
                    float t = segmentLength > 0.0001f
                        ? (targetDistance - accumulated) / segmentLength
                        : 0f;
                    sampledPoint = Vector3.Lerp(a, b, Mathf.Clamp01(t));
                    break;
                }

                accumulated += segmentLength;
            }

            Vector3 trajectoryVector = sampledPoint - rootTransform.position;
            trajectoryVector.y = 0f;

            Vector3 axis = isFuture ? velocitySpace.forward : -velocitySpace.forward;
            axis.y = 0f;

            if (trajectoryVector.sqrMagnitude < KMath.SqrEpsilon)
            {
                trajectoryVector = axis;
            }

            float yawAngle = Vector3.SignedAngle(axis, trajectoryVector, Vector3.up);
            yawAngle = Mathf.Clamp(yawAngle, -_data.maxYawAngle, _data.maxYawAngle);
            footYaw = KMath.FloatInterp(footYaw, yawAngle, _data.feetSmoothing, stream.deltaTime);

            Quaternion deltaYaw = Quaternion.Euler(0f, footYaw, 0f);
            footWarp.offset.rotation = deltaYaw;
            footWarp.offset.position = deltaYaw * footWarp.componentPosition - footWarp.componentPosition;
            footWarp.offset.position.y = 0f;

            return footWarp;
        }

        private void ProcessTrajectoryWarping(AnimationStream stream, in KTransform rootTransform)
        {
            if (!_isTrajectoryValid) return;

            float weight = _jobData.weight * _strideWeight;

            FootWarpData rightFootWarp = ComputeFootWarp(stream, rootTransform, _rightFootIkHandle,
                ref _rightFootYaw);

            FootWarpData leftFootWarp = ComputeFootWarp(stream, rootTransform, _leftFootIkHandle,
                ref _leftFootYaw);

            Vector3 rightFootComponent =
                rightFootWarp.componentPosition + rightFootWarp.offset.position * weight;
            Vector3 leftFootComponent =
                leftFootWarp.componentPosition + leftFootWarp.offset.position * weight;

            Quaternion strideRotation = rootTransform.rotation * _strideRotation;
            Vector3 strideVector = strideRotation * Vector3.forward;
            Vector3 velocityVector = _velocity.sqrMagnitude > Mathf.Epsilon ? _velocity : strideVector;
            velocityVector.y = 0f;

            float yawAngle = Vector3.SignedAngle(strideVector, velocityVector, rootTransform.up);
            yawAngle = Mathf.Clamp(yawAngle, -_data.maxYawAngle, _data.maxYawAngle);

            if (_pelvisHandle.IsValid(stream))
            {
                KTransform pelvisTransform = KAnimationMath.GetTransform(stream, _pelvisHandle);
                Vector3 pelvisComponent = rootTransform.InverseTransformPoint(pelvisTransform.position, false);

                _pelvisRoll = KMath.FloatInterp(_pelvisRoll, yawAngle * _data.pelvisRollScale * _velocity.magnitude,
                    _data.leanSmoothing, stream.deltaTime);

                Vector3 targetPelvis = Quaternion.AngleAxis(_pelvisRoll, Vector3.forward) * pelvisComponent;

                float rightLegLength = Vector3.Distance(pelvisComponent, rightFootWarp.componentPosition);
                float leftLegLength = Vector3.Distance(pelvisComponent, leftFootWarp.componentPosition);

                float pelvisYOffset = Mathf.Min(
                    ComputeLegPelvisYOffset(targetPelvis, rightFootComponent, rightLegLength),
                    ComputeLegPelvisYOffset(targetPelvis, leftFootComponent, leftLegLength));

                targetPelvis.y += pelvisYOffset;

                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _pelvisHandle,
                    Quaternion.Euler(0f, 0f, _pelvisRoll), weight);

                targetPelvis = Vector3.Lerp(pelvisComponent, targetPelvis, weight);
                _pelvisHandle.SetPosition(stream, rootTransform.TransformPoint(targetPelvis, false));
            }

            _rightFootIkHandle.SetPosition(stream, rootTransform.TransformPoint(rightFootComponent, false));
            _leftFootIkHandle.SetPosition(stream, rootTransform.TransformPoint(leftFootComponent, false));

            KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _rightFootIkHandle,
                rightFootWarp.offset.rotation, weight);

            KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _leftFootIkHandle,
                leftFootWarp.offset.rotation, weight);

            _leanYawAngle = KMath.FloatInterp(_leanYawAngle, yawAngle, _data.feetSmoothing,
                stream.deltaTime);
            Quaternion delta = Quaternion.Euler(0f, _leanYawAngle * _data.legHipRotation, 0f);
            
            if (_rightHipHandle.IsValid(stream))
            {
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _rightHipHandle, delta, weight);
            }

            if (_leftHipHandle.IsValid(stream))
            {
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _leftHipHandle, delta, weight);
            }
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_data.isEnabled || !KAnimationMath.IsWeightRelevant(_jobData.weight))
            {
                return;
            }

            if (!_jobData.rootHandle.IsValid(stream) || !_rightFootIkHandle.IsValid(stream) ||
                !_leftFootIkHandle.IsValid(stream))
            {
                return;
            }

            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            _velocity = ComputeVelocity(rootTransform.position, _prevRootPosition, stream.deltaTime);
            _velocity.y = 0f;
            _prevRootPosition = rootTransform.position;
            
            ProcessVelocityWarping(stream, rootTransform);
            ProcessTrajectoryWarping(stream, rootTransform);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (IFootWarpingModifier) settings;
            _data = _settings.GetFootWarpingData();
            _rightFootStride = 1f;
            _leftFootStride = 1f;

            BindAnimatorParameters();
            BindHandles();

            Transform root = _jobData.animator.transform.root;
            _prevRootPosition = root.position;
            _trajectoryComponent = root.GetComponentInChildren<CharacterTrajectory>(true);
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            Initialize(_jobData, newSettings);
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            return AnimationScriptPlayable.Create(graph, this);
        }

        public void PreUpdateJobData()
        {
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            var job = playable.GetJobData<FootWarpingModifierJob>();
            UpdateJobData(job, weight);
            playable.SetJobData(this);
        }

        public void UpdateJobData(FootWarpingModifierJob job, float weight)
        {
            _data = _settings.GetFootWarpingData();
            _jobData.weight = weight;
            
            _rightFootYaw = job._rightFootYaw;
            _leftFootYaw = job._leftFootYaw;
            _pelvisRoll = job._pelvisRoll;
            _leanYawAngle = job._leanYawAngle;
            
            _prevRootPosition = job._prevRootPosition;
            _rightFootStride = job._rightFootStride;
            _leftFootStride = job._leftFootStride;
            
            Animator animator = _jobData.animator;
            _strideSpeed = _hasStrideSpeed && animator != null ? animator.GetFloat(AnimatorStrideSpeed) : 0f;
            _strideWeight = _hasStrideWeight && animator != null ? animator.GetFloat(AnimatorStrideWeight) : 1f;

            _strideDirection = Vector3.zero;
            if (animator != null)
            {
                if (_hasStrideX) _strideDirection.x = animator.GetFloat(AnimatorStrideX);
                if (_hasStrideY) _strideDirection.z = animator.GetFloat(AnimatorStrideY);
            }
            
            _strideRotation = _strideDirection.magnitude > 0.001f
                ? Quaternion.LookRotation(_strideDirection.normalized, Vector3.up)
                : Quaternion.identity;
            
            UpdateTrajectoryCache();
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            _trajectoryOffsets = null;
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}