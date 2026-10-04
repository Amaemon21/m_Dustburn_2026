// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct AimAtModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private AimAtModifierSettings _settings;
        private ModifierJobData _jobData;

        private PropertyStreamHandle _turnAngleHandle;
        private AimAtComponent _aimAt;

        private Vector2 _aimAtInput;

        private Vector3 _aimAtTarget;
        private bool _enableAimAt;

        private NativeArray<LookLayerAtom> _aimBones;
        private TransformStreamHandle _aimBoneHandle;
        private Quaternion _aimBoneRotation;
        
        public bool IsAimAtEnabled => _enableAimAt;
        public Quaternion AimBoneRotation => _aimBoneRotation;

#if UNITY_EDITOR
        private bool _aimingAtTarget;
        private Transform _aimBoneTransform;
#endif
        
        public Quaternion CalculateBoneRotation(KTransform root, KTransform bone)
        {
            Vector3 direction = _aimAtTarget - bone.position;
            if (direction.magnitude > _settings.maxAimDistance) return Quaternion.identity;

            Vector3 localDir = root.InverseTransformVector(direction, false);

            float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            if (yaw < -_settings.maxAimYawAngle || yaw > _settings.maxAimYawAngle) return Quaternion.identity;
            
            float horizontalDistance = new Vector2(localDir.x, localDir.z).magnitude;
            float pitch = -Mathf.Atan2(localDir.y, horizontalDistance) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -_settings.maxAimPitchAngle, _settings.maxAimPitchAngle);
            
            Quaternion localRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 clampedLocalDir = localRotation * Vector3.forward;

            Vector3 worldDir = root.TransformVector(clampedLocalDir, false);

            Vector3 currentBoneForwardWorld = bone.TransformVector(_settings.forwardAxis, false);
            Quaternion rotationDifference = Quaternion.FromToRotation(currentBoneForwardWorld, worldDir);

            return rotationDifference;
        }

        private Vector2 CalculateAimAtInput(KTransform root, Vector3 sourceBonePos, Vector3 targetPosition, Vector3 currentForwardDir)
        {
            Vector3 directionToTarget = targetPosition - sourceBonePos;
            if (directionToTarget.sqrMagnitude < 0.01f || directionToTarget.magnitude > _settings.maxAimDistance)
            {
                return Vector2.zero;
            }

            Vector3 localTargetDir = root.InverseTransformVector(directionToTarget, false);
            Vector3 localAimDir = root.InverseTransformVector(currentForwardDir, false);

            float targetYaw = Mathf.Atan2(localTargetDir.x, localTargetDir.z) * Mathf.Rad2Deg;
            float currentYaw = Mathf.Atan2(localAimDir.x, localAimDir.z) * Mathf.Rad2Deg;
            float deltaYaw = Mathf.DeltaAngle(currentYaw, targetYaw);

            if (deltaYaw < -_settings.maxAimYawAngle || deltaYaw > _settings.maxAimYawAngle)
            {
                return Vector2.zero;
            }

            float targetDistance = new Vector2(localTargetDir.x, localTargetDir.z).magnitude;
            float targetPitch = -Mathf.Atan2(localTargetDir.y, targetDistance) * Mathf.Rad2Deg;

            float currentDistance = new Vector2(localAimDir.x, localAimDir.z).magnitude;
            float currentPitch = -Mathf.Atan2(localAimDir.y, currentDistance) * Mathf.Rad2Deg;

            float deltaPitch = Mathf.DeltaAngle(currentPitch, targetPitch);
            deltaPitch = Mathf.Clamp(deltaPitch, -_settings.maxAimPitchAngle, _settings.maxAimPitchAngle);

            return new Vector2(deltaYaw, deltaPitch);
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight) || !_aimBoneHandle.IsValid(stream)) return;

            float turnAngle = 0f;
            if (_turnAngleHandle.IsValid(stream)) turnAngle = _turnAngleHandle.GetFloat(stream);

            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            rootTransform.rotation = Quaternion.Euler(0f, turnAngle, 0f);
            rootTransform.rotation = _jobData.rootHandle.GetRotation(stream) * rootTransform.rotation;
            
            KTransform aimBoneTransform = KAnimationMath.GetTransform(stream, _aimBoneHandle);
            Vector3 currentAimForward = aimBoneTransform.TransformVector(_settings.forwardAxis, false);

            Vector2 targetAimInput = Vector2.zero;
            if (_enableAimAt)
            {
                targetAimInput = CalculateAimAtInput(rootTransform, aimBoneTransform.position, _aimAtTarget, currentAimForward);
            }

            float smoothAlpha = KMath.ExpDecayAlpha(_settings.aimAtSmoothing, stream.deltaTime);
            _aimAtInput = Vector2.Lerp(_aimAtInput, targetAimInput, smoothAlpha);

#if UNITY_EDITOR
            _aimingAtTarget = !Mathf.Approximately(targetAimInput.magnitude, 0f);
#endif

            if (Mathf.Approximately(_aimAtInput.magnitude, 0f))
            {
                return;
            }
            
            // Compute yaw delta look rotation.
            float yawInput = _aimAtInput.x;

            float fraction = yawInput / 90f;
            bool sign = fraction > 0f;
            
            foreach (var element in _aimBones)
            {
                if (!element.handle.IsValid(stream)) continue;

                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, element.handle,
                    Quaternion.Euler(0f, angle * fraction, 0f), _jobData.weight);
            }

            fraction = _aimAtInput.y / 90f;
            sign = fraction > 0f;

            // Add the yaw input to the root orientation.
            Quaternion space = rootTransform.rotation * Quaternion.Euler(0f, yawInput, 0f);
            
            foreach (var element in _aimBones)
            {
                if (!element.handle.IsValid(stream)) continue;

                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;

                Quaternion rotation = element.handle.GetRotation(stream);
                rotation = KAnimationMath.RotateInSpace(space, rotation,
                    Quaternion.Euler(angle * fraction, 0f, 0f), _jobData.weight);
                element.handle.SetRotation(stream, rotation);
            }
            
            aimBoneTransform = KAnimationMath.GetTransform(stream, _aimBoneHandle);

            _aimBoneRotation = Quaternion.Slerp(_aimBoneRotation,
                CalculateBoneRotation(rootTransform, aimBoneTransform), smoothAlpha);
            
            _aimBoneHandle.SetRotation(stream, _aimBoneRotation * aimBoneTransform.rotation);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _settings = settings as AimAtModifierSettings;
            _jobData = jobData;

            _aimAtInput = Vector2.zero;
            _aimAt = jobData.animator.transform.root.GetComponentInChildren<AimAtComponent>();

            jobData.customPropHandles.TryGetValue("TurnAngle", out _turnAngleHandle);
            LookModifierJob.SetupChain(ref _aimBones, _jobData, _settings.supportAimBones);
            
            _aimBoneHandle = AnimationModifierUtility.GetHandle(jobData, _settings.aimBone);

#if UNITY_EDITOR
            _aimBoneTransform = jobData.skeleton.GetBoneTransform(_settings.aimBone);
#endif
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
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
            var job = playable.GetJobData<AimAtModifierJob>();

            _aimBoneRotation = job._aimBoneRotation;
            _aimAtInput = job._aimAtInput;

            if (_aimAt != null)
            {
                _enableAimAt = _aimAt.enableAimAt;
                _aimAtTarget = _aimAt.GetAimAtTarget();
            }

            _jobData.weight = weight;
            
#if UNITY_EDITOR
            _aimingAtTarget = job._aimingAtTarget;
#endif
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            if (_aimBones.IsCreated) _aimBones.Dispose();
        }

        public void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (_aimBoneTransform == null) return;
            
            Vector3 aimVector = _aimAtTarget - _aimBoneTransform.position;
            float aimLength = aimVector.magnitude;

            Vector3 start = _aimBoneTransform.position;
            Vector3 target = start + _aimBoneTransform.rotation * _settings.forwardAxis * aimLength;
            
            var color = Gizmos.color;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(start, 0.04f);

            if (_aimingAtTarget)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(target, 0.04f);
                Gizmos.DrawLine(start, target);
                Handles.Label(_aimAtTarget, "Aim At Target");
            }
            
            Handles.Label(start, _aimBoneTransform.name);
            Gizmos.color = color;
#endif
        }

        public void OnSceneGUI()
        {
        }
    }
}