// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public enum EFootPinningPivot
    {
        None,
        LeftFoot,
        RightFoot
    }

    public struct FootPinningModifierJob : IAnimationJob, IAnimationModifierJob,
        IStrideJobComponent<FootPinningModifierJob>
    {
        private IFootPinningModifier _settings;
        private FootPinningData _data;
        private ModifierJobData _jobData;

        private TransformStreamHandle _leftFootHandle;
        private TransformStreamHandle _rightFootHandle;
        private TransformStreamHandle _leftFootIkHandle;
        private TransformStreamHandle _rightFootIkHandle;
        private TransformStreamHandle _leftLegRootHandle;
        private TransformStreamHandle _rightLegRootHandle;

        private EFootPinningPivot _activePivot;
        private Vector3 _pinnedRightFoot;
        private Vector3 _pinnedLeftFoot;
        private Vector3 _rightFootWorld;
        private Vector3 _leftFootWorld;

        private float _rightFootPinWeight;
        private float _leftFootPinWeight;

        private float _defaultLeftLegLength;
        private float _defaultRightLegLength;
        private bool _isValidSetup;
        private bool _forceUnpinLeftFoot;
        private bool _forceUnpinRightFoot;

        private float _footHeight;
        private Vector3 _leftFootForwardAxis;
        private Vector3 _rightFootForwardAxis;
        private Vector3 _prevRootPosition;
        private Transform _rightFootTransform;
        private Transform _leftFootTransform;

        private static readonly int AnimatorStrideWeight = Animator.StringToHash("StrideWeight");
        private float _strideWeight;
        private bool _hasStrideWeight;

        private bool IsFootPitchAligned(AnimationStream stream, TransformStreamHandle footHandle,
            Vector3 footForwardAxis, in KTransform root)
        {
            Vector3 footForward = footHandle.GetRotation(stream) * footForwardAxis;
            if (footForward.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            Vector3 footForwardInRootSpace = root.InverseTransformVector(footForward, false);
            if (footForwardInRootSpace.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            footForwardInRootSpace = footForwardInRootSpace.normalized;
            float horizontalMagnitude = new Vector2(footForwardInRootSpace.x, footForwardInRootSpace.z).magnitude;
            float pitchAngle = Mathf.Abs(Mathf.Atan2(footForwardInRootSpace.y, horizontalMagnitude) * Mathf.Rad2Deg);
            return pitchAngle <= Mathf.Max(0f, _data.angularPinThreshold);
        }

        private void GetPinRequests(AnimationStream stream, in KTransform root, out bool leftShouldPin,
            out bool rightShouldPin)
        {
            Vector3 leftFootPosition = _leftFootIkHandle.GetPosition(stream);
            Vector3 rightFootPosition = _rightFootIkHandle.GetPosition(stream);

            float leftHeight = root.InverseTransformPoint(leftFootPosition, false).y;
            float rightHeight = root.InverseTransformPoint(rightFootPosition, false).y;
            
            bool leftHeightMatched = leftHeight <= _footHeight + _data.pinHeightOffset;
            bool rightHeightMatched = rightHeight <= _footHeight + _data.pinHeightOffset;

            leftShouldPin = IsFootPitchAligned(stream, _leftFootIkHandle, _leftFootForwardAxis, root) &&
                            leftHeightMatched;

            rightShouldPin = IsFootPitchAligned(stream, _rightFootIkHandle, _rightFootForwardAxis, root) &&
                             rightHeightMatched;
        }

        private void LockPivot(EFootPinningPivot pivot)
        {
            _activePivot = pivot;
            if (pivot == EFootPinningPivot.LeftFoot)
            {
                _forceUnpinLeftFoot = false;
                _pinnedLeftFoot = _leftFootWorld;
                return;
            }

            _forceUnpinRightFoot = false;
            _pinnedRightFoot = _rightFootWorld;
        }

        private void UnlockPivot()
        {
            _activePivot = EFootPinningPivot.None;
        }

        private void UpdateForceUnpinMask(AnimationStream stream)
        {
            if (_activePivot == EFootPinningPivot.None)
            {
                return;
            }

            if (!_leftLegRootHandle.IsValid(stream) || !_rightLegRootHandle.IsValid(stream) ||
                !_leftFootIkHandle.IsValid(stream) || !_rightFootIkHandle.IsValid(stream))
            {
                return;
            }

            Vector3 rightHip = _rightLegRootHandle.GetPosition(stream);
            Vector3 leftHip = _leftLegRootHandle.GetPosition(stream);
            Vector3 leftFoot = _leftFootIkHandle.GetPosition(stream);
            Vector3 rightFoot = _rightFootIkHandle.GetPosition(stream);

            Vector3 hipAxis = leftHip - rightHip;
            Vector3 footAxisBeforePinning = leftFoot - rightFoot;
            if (hipAxis.sqrMagnitude <= Mathf.Epsilon || footAxisBeforePinning.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            float angleBeforePinning = Vector3.Angle(hipAxis, footAxisBeforePinning);

            if (_activePivot == EFootPinningPivot.LeftFoot)
            {
                leftFoot = _pinnedLeftFoot;
            }
            else
            {
                rightFoot = _pinnedRightFoot;
            }

            Vector3 footAxisAfterPinning = leftFoot - rightFoot;
            if (footAxisAfterPinning.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            float angleAfterPinning = Vector3.Angle(hipAxis, footAxisAfterPinning);
            float deltaAngle = Mathf.Abs(angleAfterPinning - angleBeforePinning);

            float threshold = Mathf.Max(0f, _data.pinYawThreshold);
            if (deltaAngle <= threshold)
            {
                return;
            }

            if (_activePivot == EFootPinningPivot.LeftFoot)
            {
                _forceUnpinLeftFoot = true;
                return;
            }

            _forceUnpinRightFoot = true;
        }

        private Vector3 SolveFootIkTarget(AnimationStream stream, TransformStreamHandle ikHandle, Vector3 pinnedTarget,
            float pinAlpha, TransformStreamHandle legRootHandle, float defaultLegLength)
        {
            if (!legRootHandle.IsValid(stream) || defaultLegLength <= Mathf.Epsilon)
            {
                return Vector3.Lerp(ikHandle.GetPosition(stream), pinnedTarget, pinAlpha);
            }

            Vector3 legRootPosition = legRootHandle.GetPosition(stream);
            Vector3 toTarget = pinnedTarget - legRootPosition;
            float currentLegLength = toTarget.magnitude;
            float maxLegLength = defaultLegLength * Mathf.Max(0f, _data.legStretchFactor);
            if (currentLegLength > maxLegLength && currentLegLength > Mathf.Epsilon)
            {
                pinnedTarget = legRootPosition + toTarget.normalized * maxLegLength;
            }

            return Vector3.Lerp(ikHandle.GetPosition(stream), pinnedTarget, pinAlpha);
        }

        private void UpdatePivotState(AnimationStream stream, in KTransform root)
        {
            GetPinRequests(stream, root, out bool leftInContact, out bool rightInContact);
            bool pivotIsRightFoot = _activePivot == EFootPinningPivot.RightFoot;

            if (_activePivot != EFootPinningPivot.None)
            {
                bool activeFootStillContacted = pivotIsRightFoot ? rightInContact : leftInContact;
                if (!activeFootStillContacted)
                {
                    UnlockPivot();
                }
            }

            if (leftInContact && rightInContact)
            {
                UnlockPivot();
                return;
            }

            if (_activePivot != EFootPinningPivot.None)
            {
                return;
            }

            if (leftInContact)
            {
                LockPivot(EFootPinningPivot.LeftFoot);
                _leftFootPinWeight = 1f;
            }
            else if (rightInContact)
            {
                LockPivot(EFootPinningPivot.RightFoot);
                _rightFootPinWeight = 1f;
            }
        }

        private void UpdateFootLockingState(AnimationStream stream)
        {
            KTransform root = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            UpdatePivotState(stream, root);
            UpdateForceUnpinMask(stream);

            float multiplier = _strideWeight > 0.95f ? 1f : 0f;
            
            float rightMask = _forceUnpinRightFoot ? 0f : 1f;
            float target = _activePivot == EFootPinningPivot.RightFoot ? rightMask : 0f;
            _rightFootPinWeight = KMath.FloatInterp(_rightFootPinWeight, target, _data.pinSmoothing,
                stream.deltaTime) * multiplier;

            float leftMask = _forceUnpinLeftFoot ? 0f : 1f;
            target = _activePivot == EFootPinningPivot.LeftFoot ? leftMask : 0f;
            _leftFootPinWeight = KMath.FloatInterp(_leftFootPinWeight, target, _data.pinSmoothing,
                stream.deltaTime) * multiplier;
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_isValidSetup || !_data.isEnabled || !KAnimationMath.IsWeightRelevant(_jobData.weight))
            {
                return;
            }
            
            if (!_jobData.rootHandle.IsValid(stream) || !_leftFootHandle.IsValid(stream) ||
                !_rightFootHandle.IsValid(stream) || !_leftFootIkHandle.IsValid(stream) ||
                !_rightFootIkHandle.IsValid(stream))
            {
                return;
            }

            UpdateFootLockingState(stream);

            float rightPinAlpha = _rightFootPinWeight * _jobData.weight;
            float leftPinAlpha = _leftFootPinWeight * _jobData.weight;

            Vector3 rightTarget = SolveFootIkTarget(stream, _rightFootIkHandle, _pinnedRightFoot, rightPinAlpha,
                _rightLegRootHandle, _defaultRightLegLength);
            Vector3 leftTarget = SolveFootIkTarget(stream, _leftFootIkHandle, _pinnedLeftFoot, leftPinAlpha,
                _leftLegRootHandle, _defaultLeftLegLength);

            _rightFootIkHandle.SetPosition(stream, rightTarget);
            _leftFootIkHandle.SetPosition(stream, leftTarget);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (IFootPinningModifier) settings;
            _data = _settings.GetFootPinningData();
            _isValidSetup = false;
            _defaultLeftLegLength = 0f;
            _defaultRightLegLength = 0f;
            _leftFootForwardAxis = Vector3.forward;
            _rightFootForwardAxis = Vector3.forward;

            if (_jobData.skeleton == null || _jobData.animator == null)
            {
                return;
            }

            foreach (var parameter in _jobData.animator.parameters)
            {
                if (parameter.nameHash == AnimatorStrideWeight)
                {
                    _hasStrideWeight = true;
                    break;
                }
            }

            LowerBodyBones bones = _data.bones;
            _leftFootHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.leftFoot);
            _rightFootHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.rightFoot);
            _leftFootIkHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.leftFootIkTarget);
            _rightFootIkHandle = AnimationModifierUtility.GetHandle(in _jobData, bones.rightFootIkTarget);
            
            var leftFoot = _jobData.skeleton.GetSkeletonBone(bones.leftFoot);
            var rightFoot = _jobData.skeleton.GetSkeletonBone(bones.rightFoot);
            _leftFootTransform = leftFoot.transform;
            _rightFootTransform = rightFoot.transform;
            
            _isValidSetup = _leftFootTransform != null && _rightFootTransform != null;
            if (!_isValidSetup)
            {
                Debug.LogWarning($"{nameof(FootPinningModifierJob)}: Missing foot references. Modifier will be disabled.");
                return;
            }

            _leftFootWorld = _leftFootTransform.position;
            _rightFootWorld = _rightFootTransform.position;
            
            _footHeight = Mathf.Max(rightFoot.meshPose.position.y, leftFoot.meshPose.position.y);

            KTransform root = new KTransform(_jobData.animator.transform);
            KTransform rightFootWorld = root.GetWorldTransform(rightFoot.meshPose, false);
            KTransform leftFootWorld = root.GetWorldTransform(leftFoot.meshPose, false);
            
            _leftFootForwardAxis = leftFootWorld.InverseTransformVector(root.forward, false);
            _rightFootForwardAxis = rightFootWorld.InverseTransformVector(root.forward, false);
            
            Transform leftLegRoot = _leftFootTransform.parent?.parent;
            Transform rightLegRoot = _rightFootTransform.parent?.parent;

            if (rightLegRoot != null)
            {
                var rightLegPose = _jobData.skeleton.GetSkeletonBone(rightLegRoot.name).meshPose;
                _rightLegRootHandle = _jobData.animator.BindStreamTransform(rightLegRoot);
                _defaultRightLegLength = Vector3.Distance(rightLegPose.position, rightFoot.meshPose.position);
            }

            if (leftLegRoot != null)
            {
                var leftLegPose = _jobData.skeleton.GetSkeletonBone(leftLegRoot.name).meshPose;
                _leftLegRootHandle = _jobData.animator.BindStreamTransform(leftLegRoot);
                _defaultLeftLegLength = Vector3.Distance(leftLegPose.position, leftFoot.meshPose.position);
            }

            _prevRootPosition = root.position;
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
            var job = playable.GetJobData<FootPinningModifierJob>();
            UpdateJobData(job, weight);
            playable.SetJobData(this);
        }

        public void UpdateJobData(FootPinningModifierJob job, float weight)
        {
            _activePivot = job._activePivot;
            _pinnedRightFoot = job._pinnedRightFoot;
            _pinnedLeftFoot = job._pinnedLeftFoot;
            _rightFootPinWeight = job._rightFootPinWeight;
            _leftFootPinWeight = job._leftFootPinWeight;
            _forceUnpinLeftFoot = job._forceUnpinLeftFoot;
            _forceUnpinRightFoot = job._forceUnpinRightFoot;
            _prevRootPosition = job._prevRootPosition;

            _jobData.weight = weight;
            _data = _settings.GetFootPinningData();
            
            if (_leftFootTransform != null) _leftFootWorld = _leftFootTransform.position;
            if (_rightFootTransform != null) _rightFootWorld = _rightFootTransform.position;
            if (_hasStrideWeight) _strideWeight = _jobData.animator.GetFloat(AnimatorStrideWeight);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}