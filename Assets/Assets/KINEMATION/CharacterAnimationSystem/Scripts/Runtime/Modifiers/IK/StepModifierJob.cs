// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct StepAnimation
    {
        public Vector3 leadFoot;
        public Vector3 followFoot;
        public Vector3 pelvis;

        public Vector3 leadLastFrame;
        public Vector3 followLastFrame;
        public Vector3 pelvisLastFrame;
    }
    
    public struct StepModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private StepModifierSettings _defaultSettings;
        private StepModifierSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _rightFootHandle;
        private TransformStreamHandle _leftFootHandle;
        private TransformStreamHandle _pelvisHandle;
        
        private Transform _rightFoot;
        private Transform _leftFoot;
        private Transform _pelvis;

        private StepAnimation _stepAnimation;

        private KTransform _lockedLeadFoot;
        private KTransform _lockedFollowFoot;
        private KTransform _lockedPelvis;
        private KTransform _lockedRoot;

        private Vector3 _accumLeadFootMotion;
        private Vector3 _accumFollowFootMotion;
        private Vector3 _accumPelvisMotion;
        
        private bool _isRightFootLeading;
        
        private bool _wantsToStep;
        private float _playback;

        private Vector3 _previousPosition;
        private Vector3 _velocity;

        private bool _isMoving;
        private float _moveStartTime;

        private float _length;
        private float _cooldownTime;

        private AnimationScriptPlayable _playable;
        
        private Vector3 _rightFootAxisUp;
        private Vector3 _leftFootAxisUp;
        
        private float _legLength;
        private float _footHeight;

        private KTransform _lockedFollowFootTarget;
        private KTransform _lockedPelvisTarget;
        private bool _useGroundAlignment;

        private bool _hasProcessedInitialPose;
        private bool _isValidSetup;
        
#if UNITY_EDITOR
        private Vector3 _leadFootTarget;
        private Vector3 _followFootTarget;
        private Vector3 _pelvisTarget;
#endif
        
        private void GroundFoot(Transform footTransform, Vector3 localUp, ref KTransform foot, ref KTransform pelvis)
        {
            if (footTransform == null) return;

            Transform root = _jobData.animator.transform;
            Vector3 rootPos = root.position;
            Vector3 footPos = footTransform.position;

            // 1. Snap Y to Root Y + Foot Height (Ground Plane Offset)
            footPos.y = rootPos.y + _footHeight;

            // 2. Apply Stretch Constraint (Cylindrical Clamp)
            // Instead of a rigid sphere intersection (which snaps to center if hip is too high),
            // we simply clamp the horizontal distance to the leg length.
            // This prevents the foot from sliding too far away while avoiding singularities.
            Vector3 pivot = _pelvis.position;
            
            // Calculate horizontal vector from pivot to foot
            Vector3 toFootXZ = footPos - pivot;

            // Clamp to leg length
            float currentLength = toFootXZ.magnitude + _defaultSettings.legStretchFactor;
            
            if (currentLength > _legLength)
            {
                float stretchLength = currentLength - _legLength;
                
                // Clamp position
                toFootXZ = toFootXZ.normalized * _legLength;
                footPos.x = pivot.x + toFootXZ.x;
                footPos.z = pivot.z + toFootXZ.z;
                
                pelvis.position += toFootXZ.normalized * stretchLength;
            }

            // 3. Align Rotation to Ground using cached axis
            // Ground Normal is Up
            Quaternion footRot = AlignRotationWithPlane(footTransform.rotation, localUp, Vector3.up);
            foot = new KTransform(footPos, footRot);
        }
        
        private static Quaternion AlignRotationWithPlane(Quaternion rotation, Vector3 localUp, Vector3 normal)
        {
            // 1. Calculate the current Up vector in world space
            Vector3 currentUp = rotation * localUp;
            
            // 2. Compute the alignment rotation
            // FromToRotation creates the shortest rotation to align currentUp to normal.
            // This ensures the foot surface (defined by localUp) is parallel to the ground plane (normal),
            // while preserving the original heading (Yaw) as much as possible, preventing jitter.
            Quaternion alignment = Quaternion.FromToRotation(currentUp, normal);
            
            // 3. Apply alignment
            return alignment * rotation;
        }
        
        private KTransform GetClampedLockedPose(KTransform locked, KTransform target)
        {
            if (Mathf.Approximately(_settings.maxAllowedStride, 0f)) return target;
            
            Vector3 lockedZX = locked.position;
            Vector3 targetZX = target.position;
            lockedZX.y = targetZX.y = 0f;
            
            Vector3 stride = targetZX - lockedZX;
            if (stride.magnitude > _settings.maxAllowedStride)
            {
                locked.position += stride.normalized * (stride.magnitude - _settings.maxAllowedStride);
            }
            
            return locked;
        }
        
        private bool MirrorPelvis()
        {
            return _settings.animatedLeadFoot == AnimatedLeadFoot.RightFoot && !_isRightFootLeading
                || _settings.animatedLeadFoot == AnimatedLeadFoot.LeftFoot && _isRightFootLeading;
        }

        private Vector3 MultiplyVectors(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        private float SafeDivide(float a, float b)
        {
            return b > 0f ? a / b : 0f;
        }

        private Vector3 GetWarpedVector(Vector3 vector, Vector3 accumulated, Vector3 total)
        {
            vector.x *= total.x > 0f ? accumulated.x / total.x : 1f;
            vector.y *= total.y > 0f ? accumulated.y / total.y : 1f;
            vector.z *= total.z > 0f ? accumulated.z / total.z : 1f;
            return vector;
        }
        
        private void UpdateMotion(out Vector3 motion, ref Vector3 totalMotion, VectorCurve curves, 
            float previousPlayback)
        {
            Vector3 previousMotion = curves.GetValue(previousPlayback);
            Vector3 currentMotion = curves.GetValue(_playback);
            
            Vector3 delta = currentMotion - previousMotion;
            delta.x = Mathf.Abs(delta.x);
            delta.y = Mathf.Abs(delta.y);
            delta.z = Mathf.Abs(delta.z);

            totalMotion += delta;
            motion = currentMotion;
        }

        private bool IsMoving()
        {
            return Time.realtimeSinceStartup - _moveStartTime > 0.3f;
        }

        private bool IsRightFootLeading()
        {
            if (_settings.forceLeadFoot)
            {
                return _settings.animatedLeadFoot == AnimatedLeadFoot.RightFoot;
            }
            
            if (_velocity != Vector3.zero && !IsMoving())
            {
                KTransform origin = new KTransform(_jobData.animator.transform.position,
                    Quaternion.LookRotation(_velocity));

                Vector3 rightFoot = origin.InverseTransformPoint(_rightFoot.position, false);
                Vector3 leftFoot = origin.InverseTransformPoint(_leftFoot.position, false);
                
                return rightFoot.z > leftFoot.z;
            }
            
            float rightFootY = _rightFoot.position.y;
            float leftFootY = _leftFoot.position.y;
             
            if (Mathf.Approximately(rightFootY, leftFootY))
            {
                return Random.Range(0, 2) == 0;
            }

            return rightFootY > leftFootY;
        }
        
        private Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Quaternion rotation)
        {
            return pivot + rotation * (point - pivot);
        }

        private void ClampFeetRotation(in Quaternion rootRotation)
        {
            float yawDelta = (Quaternion.Inverse(_lockedRoot.rotation) * rootRotation).eulerAngles.y;
            yawDelta = KMath.NormalizeEulerAngle(yawDelta);
            if (Mathf.Abs(yawDelta) > _settings.maxAllowedOffsetAngle)
            {
                float sign = yawDelta > 0f ? 1f : -1f;

                yawDelta = (Mathf.Abs(yawDelta) - _settings.maxAllowedOffsetAngle) * sign;
                Quaternion orientationDelta = Quaternion.Euler(0f, yawDelta, 0f);

                _lockedRoot.rotation *= orientationDelta;
                
                Vector3 pivot = _lockedPelvis.position;
                _lockedLeadFoot.position = RotatePointAroundPivot(_lockedLeadFoot.position, pivot,
                    orientationDelta);
                _lockedFollowFootTarget.position = RotatePointAroundPivot(_lockedFollowFootTarget.position, pivot,
                    orientationDelta);

                _lockedLeadFoot.rotation = KAnimationMath.RotateInSpace(rootRotation,
                    _lockedLeadFoot.rotation, orientationDelta, 1f);
                _lockedFollowFootTarget.rotation = KAnimationMath.RotateInSpace(rootRotation,
                    _lockedFollowFootTarget.rotation, orientationDelta, 1f);
            }
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_isValidSetup || !_wantsToStep || Mathf.Approximately(_jobData.weight, 0f)) return;
            if (!_pelvisHandle.IsValid(stream) || !_rightFootHandle.IsValid(stream) || !_leftFootHandle.IsValid(stream))
            {
                return;
            }
             
            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            
            float lockedAlpha = KMath.ExpDecayAlpha(24f, stream.deltaTime);
            _lockedPelvis = KTransform.Lerp(_lockedPelvis, _lockedPelvisTarget, lockedAlpha);
            ClampFeetRotation(rootTransform.rotation);
            _lockedFollowFoot = KTransform.Lerp(_lockedFollowFoot, _lockedFollowFootTarget, lockedAlpha);
            
            var leadFootMotion = MultiplyVectors(_stepAnimation.leadFoot, _settings.leadFootScale);
            var followFootMotion = MultiplyVectors(_stepAnimation.followFoot, _settings.followFootScale);
            var pelvisMotion = MultiplyVectors(_stepAnimation.pelvis, _settings.pelvisScale);
            
            var leadFootTarget = MultiplyVectors(_stepAnimation.leadLastFrame, _settings.leadFootScale);
            var followFootTarget = MultiplyVectors(_stepAnimation.followLastFrame, _settings.followFootScale);
            var pelvisTarget = MultiplyVectors(_stepAnimation.pelvisLastFrame, _settings.pelvisScale);
            
            TransformStreamHandle leadFootHandle = _isRightFootLeading ? _rightFootHandle : _leftFootHandle;
            TransformStreamHandle followFootHandle = _isRightFootLeading ? _leftFootHandle : _rightFootHandle;

            KTransform defaultLeadFoot = KAnimationMath.GetTransform(stream, leadFootHandle);
            KTransform defaultFollowFoot = KAnimationMath.GetTransform(stream, followFootHandle);
            KTransform defaultPelvis = KAnimationMath.GetTransform(stream, _pelvisHandle);
            
#if UNITY_EDITOR
            _leadFootTarget = defaultLeadFoot.position;
            _followFootTarget = defaultFollowFoot.position;
            _pelvisTarget = defaultPelvis.position;
#endif
            
            var lockedLeadFoot = GetClampedLockedPose(_lockedLeadFoot, defaultLeadFoot);
            var lockedFollowFoot = GetClampedLockedPose(_lockedFollowFoot, defaultFollowFoot);
            var lockedPelvis = GetClampedLockedPose(_lockedPelvis, defaultPelvis);
            
            _pelvisHandle.SetPosition(stream,
                Vector3.Lerp(defaultPelvis.position, lockedPelvis.position, _jobData.weight));
            leadFootHandle.SetPosition(stream, 
                Vector3.Lerp(defaultLeadFoot.position, lockedLeadFoot.position, _jobData.weight));
            followFootHandle.SetPosition(stream, 
                Vector3.Lerp(defaultFollowFoot.position, lockedFollowFoot.position, _jobData.weight));
            
            Vector3 progress = GetWarpedVector(Vector3.one, _accumLeadFootMotion, _settings.totalLeadFootMotion);
            float weight = (progress.x + progress.y + progress.z) / 3f;
            leadFootHandle.SetRotation(stream, 
                Quaternion.Slerp(defaultLeadFoot.rotation, lockedLeadFoot.rotation, 
                    _jobData.weight * (1f - weight)));
            
            progress = GetWarpedVector(Vector3.one, _accumFollowFootMotion, _settings.totalFollowFootMotion);
            weight = (progress.x + progress.y + progress.z) / 3f;
            
            followFootHandle.SetRotation(stream, 
                Quaternion.Slerp(defaultFollowFoot.rotation, lockedFollowFoot.rotation, 
                    _jobData.weight * (1f - weight)));
            
            defaultLeadFoot = rootTransform.GetRelativeTransform(defaultLeadFoot, false);
            defaultFollowFoot = rootTransform.GetRelativeTransform(defaultFollowFoot, false);
            defaultPelvis = rootTransform.GetRelativeTransform(defaultPelvis, false);
            
            lockedLeadFoot = KAnimationMath.GetTransform(stream, leadFootHandle);
            lockedFollowFoot = KAnimationMath.GetTransform(stream, followFootHandle);
            lockedPelvis = KAnimationMath.GetTransform(stream, _pelvisHandle);

            lockedLeadFoot = rootTransform.GetRelativeTransform(lockedLeadFoot, false);
            lockedFollowFoot = rootTransform.GetRelativeTransform(lockedFollowFoot, false);
            lockedPelvis = rootTransform.GetRelativeTransform(lockedPelvis, false);

            lockedLeadFoot.position += leadFootTarget;
            lockedFollowFoot.position += followFootTarget;
            lockedPelvis.position += pelvisTarget;

            Vector3 leadDelta = defaultLeadFoot.position - lockedLeadFoot.position;
            leadDelta = GetWarpedVector(leadDelta, _accumLeadFootMotion, _settings.totalLeadFootMotion);
            
            Vector3 followDelta = defaultFollowFoot.position - lockedFollowFoot.position;
            followDelta = GetWarpedVector(followDelta, _accumFollowFootMotion, _settings.totalFollowFootMotion);
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, leadFootHandle, 
                leadFootMotion + leadDelta, _jobData.weight);
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, followFootHandle, 
                followFootMotion + followDelta, _jobData.weight);

            defaultLeadFoot = KAnimationMath.GetTransform(stream, leadFootHandle);
            defaultFollowFoot = KAnimationMath.GetTransform(stream, followFootHandle);

            Vector3 pelvisDelta = defaultPelvis.position - lockedPelvis.position;
            pelvisDelta = GetWarpedVector(pelvisDelta, _accumPelvisMotion, _settings.totalPelvisMotion);
            pelvisMotion.x *= MirrorPelvis() ? -1f : 1f;
            pelvisMotion += pelvisDelta;
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _pelvisHandle, pelvisMotion, _jobData.weight);
            
            leadFootHandle.SetPosition(stream, defaultLeadFoot.position);
            leadFootHandle.SetRotation(stream, defaultLeadFoot.rotation);
            
            followFootHandle.SetPosition(stream, defaultFollowFoot.position);
            followFootHandle.SetRotation(stream, defaultFollowFoot.rotation);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _defaultSettings = settings as StepModifierSettings;
            _jobData = jobData;
            _isValidSetup = false;
            
            if (_defaultSettings == null || _jobData.skeleton == null)
            {
                return;
            }

            _rightFoot = jobData.skeleton.GetBoneTransform(_defaultSettings.rightFootIkTarget.name);
            _leftFoot = jobData.skeleton.GetBoneTransform(_defaultSettings.leftFootIkTarget.name);
            _pelvis = jobData.skeleton.GetBoneTransform(_defaultSettings.pelvis.name);
            
            _rightFootHandle = AnimationModifierUtility.GetHandle(in jobData, _defaultSettings.rightFootIkTarget);
            _leftFootHandle = AnimationModifierUtility.GetHandle(in jobData, _defaultSettings.leftFootIkTarget);
            _pelvisHandle = AnimationModifierUtility.GetHandle(in jobData, _defaultSettings.pelvis);
            
            _isValidSetup = _rightFoot != null && _leftFoot != null && _pelvis != null;
            if (!_isValidSetup)
            {
                Debug.LogWarning($"{nameof(StepModifierJob)}: Missing pelvis/foot references. Modifier will be ignored.");
                return;
            }

            if (_hasProcessedInitialPose) return;
            
            Transform root = jobData.animator.transform;
            Vector3 up = root.up;

            var rightFoot = jobData.skeleton.GetBoneTransform(_defaultSettings.rightFoot.name);
            var leftFoot = jobData.skeleton.GetBoneTransform(_defaultSettings.leftFoot.name);

            _useGroundAlignment = rightFoot != null && leftFoot != null;
            
            if (_useGroundAlignment)
            {
                _footHeight = rightFoot.position.y - root.position.y;
                
                float distRight = Vector3.Distance(_pelvis.position, rightFoot.position);
                float distLeft = Vector3.Distance(_pelvis.position, leftFoot.position);
                
                _rightFootAxisUp = Quaternion.Inverse(rightFoot.rotation) * up;
                _leftFootAxisUp = Quaternion.Inverse(leftFoot.rotation) * up;
                _legLength = Mathf.Max(distRight, distLeft) * 1.01f;
            }
            
            _hasProcessedInitialPose = true;
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            if (newSettings == null || !_isValidSetup || !_playable.IsValid()) return;
            
            if (_settings != null)
            {
                // Only lock root if cooldown is ready.
                float cooldownDelta = (Time.time - _cooldownTime) * _settings.playRate;
                if (cooldownDelta <= _settings.stepCooldownRate * _length) return;
            }
            
            _settings = newSettings as StepModifierSettings;
            
            _isRightFootLeading = IsRightFootLeading();
            _wantsToStep = true;
            _playback = 0f;
            
            Transform leadFoot = _isRightFootLeading ? _rightFoot : _leftFoot;
            Transform followFoot = _isRightFootLeading ? _leftFoot : _rightFoot;

            var job = _playable.GetJobData<StepModifierJob>();
            job._lockedLeadFoot = new KTransform(leadFoot.transform);
            job._lockedFollowFoot = job._lockedFollowFootTarget = new KTransform(followFoot.transform);
            
            Vector3 followAxisUp = _isRightFootLeading ? _leftFootAxisUp : _rightFootAxisUp;

            job._lockedPelvis = job._lockedPelvisTarget = new KTransform(_pelvis);

            if (_useGroundAlignment)
            {
                GroundFoot(followFoot, followAxisUp, ref job._lockedFollowFootTarget, ref job._lockedPelvisTarget);
            }
            
            job._lockedRoot = new KTransform(_jobData.animator.transform);
            _playable.SetJobData(job);
            
            _accumFollowFootMotion = _accumLeadFootMotion = _accumPelvisMotion = Vector3.zero;

            _stepAnimation.leadLastFrame = _settings.leadFootMotion.GetLastValue();
            _stepAnimation.followLastFrame = _settings.followFootMotion.GetLastValue();
            _stepAnimation.pelvisLastFrame = _settings.pelvisMotion.GetLastValue();
            if (MirrorPelvis()) _stepAnimation.pelvisLastFrame.x *= -1f;

            _length = Mathf.Max(_settings.leadFootMotion.GetCurveLength(),
                _settings.followFootMotion.GetCurveLength());
            _length = Mathf.Max(_length, _settings.pelvisMotion.GetCurveLength());
            _cooldownTime = Time.time;
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            _playable = AnimationScriptPlayable.Create(graph, this);
            return _playable;
        }

        public void PreUpdateJobData()
        {
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            if (_settings == null) return;
            
            Transform root = _jobData.animator.transform;
            Vector3 rootPosition = root.position;
            _velocity = rootPosition - _previousPosition;
            _previousPosition = rootPosition;

            // Cache the start moving time and lock the movement state.
            if (!_isMoving && _velocity.magnitude > 0.001f)
            {
                _moveStartTime = Time.time;
                _isMoving = true;
            }

            // Clear the movement state lock, ready to cache time now.
            if (_isMoving && Mathf.Approximately(_velocity.magnitude, 0f))
            {
                _isMoving = false;
            }
            
            _jobData.weight = weight;
            if (_wantsToStep && Mathf.Approximately(_jobData.weight, 0f))
            {
                _wantsToStep = false;
                _cooldownTime = 0f;
                _stepAnimation.leadFoot = _stepAnimation.followFoot = _stepAnimation.pelvis = Vector3.zero;
            }
            
            if (_wantsToStep)
            {
                float prevPlayback = _playback;
                _playback += Time.deltaTime * _settings.playRate;
                
                if (_playback > _length)
                {
                    _wantsToStep = false;
                }
                else
                {
                    UpdateMotion(out _stepAnimation.leadFoot, ref _accumLeadFootMotion, _settings.leadFootMotion,
                        prevPlayback);
                    UpdateMotion(out _stepAnimation.followFoot, ref _accumFollowFootMotion, _settings.followFootMotion,
                        prevPlayback);
                    UpdateMotion(out _stepAnimation.pelvis, ref _accumPelvisMotion, _settings.pelvisMotion,
                        prevPlayback);
                }
            }

            var job = playable.GetJobData<StepModifierJob>();

            _lockedLeadFoot = job._lockedLeadFoot;
            _lockedFollowFoot = job._lockedFollowFoot;
            _lockedPelvis = job._lockedPelvis;
            _lockedRoot = job._lockedRoot;

            _lockedFollowFootTarget = job._lockedFollowFootTarget;
            _lockedPelvisTarget = job._lockedPelvisTarget;
            
#if UNITY_EDITOR
            _leadFootTarget = job._leadFootTarget;
            _followFootTarget = job._followFootTarget;
            _pelvisTarget = job._pelvisTarget;
#endif
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!_wantsToStep) return;
            
            var color = Gizmos.color;
            
            Gizmos.color = Color.red;
            
            Handles.Label(_lockedLeadFoot.position, $"{(_isRightFootLeading ? "Right" : "Left")} Foot");
            Gizmos.DrawWireSphere(_lockedLeadFoot.position, 0.03f);
            
            Handles.Label(_lockedFollowFoot.position, $"{(_isRightFootLeading ? "Right" : "Left")} Foot");
            Gizmos.DrawWireSphere(_lockedFollowFoot.position, 0.03f);
            
            Handles.Label(_lockedPelvis.position, "Pelvis");
            Gizmos.DrawWireSphere(_lockedPelvis.position, 0.03f);

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(_rightFoot.position, 0.03f);
            Gizmos.DrawWireSphere(_leftFoot.position, 0.03f);

            Gizmos.color = color;
#endif
        }

        public void OnSceneGUI()
        {
        }
    }
}