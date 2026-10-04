// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera
{
    [Serializable]
    public struct CameraPose
    {
        public Vector3 offset;
        public Vector3 pivot;

        public CameraPose(Vector3 offset, Vector3 pivot)
        {
            this.offset = offset;
            this.pivot = pivot;
        }

        public static CameraPose Lerp(CameraPose a, CameraPose b, float alpha)
        {
            return new CameraPose(Vector3.Lerp(a.offset, b.offset, alpha), 
                Vector3.Lerp(a.pivot, b.pivot, alpha));
        }
    }
    
    public class CharacterCamera : MonoBehaviour
    {
        public float ViewWeight => _viewWeight;
        public float DefaultFov => _defaultFov;
        
        [Header("Inputs")]
        
        [Tooltip("Horizontal rotation.")]
        public float yawInput = 0f;
        [Tooltip("Vertical rotation.")]
        public float pitchInput = 0f;
        
        [Tooltip("If player is aiming or not.")]
        public bool isAiming = false;
        
        [Tooltip("If player is crouched.")]
        public bool isCrouching;
        
        [Tooltip("If to use first-person perspective.")]
        public bool isFirstPerson = false;
        
        [Tooltip("Position the camera relative to the right or left shoulder.")]
        public bool useRightShoulder = true;
        
        [Tooltip("Interpolation speed for camera movement.")]
        [SerializeField, Min(0f)] protected float viewSmoothing = 5.8f;
        [Tooltip("Interpolation speed for third-person pivot movement.")]
        [SerializeField, Min(0f)] protected float pivotSmoothing = 4f;
        [Tooltip("Maximum distance from desired pivot position. Set to 0 to disable clamping.")]
        [SerializeField, Min(0f)] protected float pivotMaxDistance = 0.2f;
        
        [Header("Transforms")]
        [Tooltip("First-person view target point.")]
        public Transform firstPersonSocket;
        
        [Tooltip("Animated camera transform (camera bone).")]
        [SerializeField] protected Transform cameraAnimationSource;

        [Header("Offsets")]
        [Tooltip("Standing camera pose.")]
        [SerializeField] protected CameraPose thirdPersonNormal = new CameraPose()
        {
            offset = new (0.25f, 1.2f, -1.7f),
            pivot = new (0f, 1.1f, 0f)
        };
        
        [Tooltip("Standing camera pose when aiming.")]
        [SerializeField] protected CameraPose thirdPersonAim = new CameraPose()
        {
            offset = new (0.25f, 1.6f, -0.6f),
            pivot = new (0f, 1.1f, 0f)
        };
        
        [Tooltip("Crouch camera pose.")]
        [SerializeField] protected CameraPose thirdPersonCrouch = new CameraPose()
        {
            offset = new (0.25f, 0.9f, -1.5f),
            pivot = new (0f, 0.7f, 0f)
        };
        
        [Tooltip("Crouch camera pose when aiming.")]
        [SerializeField] protected CameraPose thirdPersonCrouchAim = new CameraPose()
        {
            offset = new (0.25f, 1.15f, -0.6f),
            pivot = new (0f, 0.7f, 0f)
        };

        [Header("Camera Collision")]
        
        [Tooltip("Sphere trace radius.")]
        [Min(0f)] [SerializeField] protected float traceRadius = 0.1f;
        [Tooltip("Camera smoothing when colliding.")]
        [Min(0f)] [SerializeField] protected float traceInterpSpeed = 18f;
        [Tooltip("What collision layers to use.")]
        [SerializeField] protected LayerMask traceMask;

#if UNITY_EDITOR
        [Header("Debug")]
        [Tooltip("Toggles camera debug gizmos.")]
        [SerializeField] protected bool drawGizmos;
#endif
        
        protected UnityEngine.Camera _camera;
        
        protected CharacterCameraShake _activeShake;
        protected float _cameraShakePlayback;
        protected Vector3 _cameraShakeTarget;
        protected Vector3 _cameraShake;

        protected float _viewWeight = 0f;
        protected float _shoulderWeight = 0f;
        protected float _aimingWeight;
        protected float _crouchWeight;

        protected Vector3 _pivotPoint;
        protected RaycastHit _hit;
        protected readonly RaycastHit[] _traceHits = new RaycastHit[16];
        
        protected float _defaultFov;
        protected float _targetFov;
        protected float _fovSmoothing;

        protected float _springLength = 1f;
        protected bool _pivotInitialized;

        public virtual void UpdateTargetFOV(float newFov, float interpSpeed = 0f)
        {
            _targetFov = Mathf.Approximately(newFov, 0f) ? _defaultFov : newFov;
            _fovSmoothing = interpSpeed;
        }

        public virtual void PlayCameraShake(CharacterCameraShake newShake)
        {
            if (newShake == null) return;
            
            _activeShake = newShake;
            _cameraShakePlayback = 0f;

            _cameraShakeTarget.x = CharacterCameraShake.GetTarget(_activeShake.pitch);
            _cameraShakeTarget.y = CharacterCameraShake.GetTarget(_activeShake.yaw);
            _cameraShakeTarget.z = CharacterCameraShake.GetTarget(_activeShake.roll);
        }

        protected void Start()
        {
            _camera = GetComponent<UnityEngine.Camera>();

            if (_camera != null) _targetFov = _defaultFov = _camera.fieldOfView;
            if (firstPersonSocket == null) firstPersonSocket = transform.root;
        }

        protected virtual bool TraceCameraCollision(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            if (distance <= KMath.FloatMin)
            {
                return false;
            }

            int hitCount = traceRadius > KMath.FloatMin
                ? Physics.SphereCastNonAlloc(origin, traceRadius, direction, _traceHits, distance, traceMask,
                    QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, direction, _traceHits, distance, traceMask,
                    QueryTriggerInteraction.Ignore);

            Transform characterRoot = transform.root;
            float closestDistance = float.MaxValue;
            bool hasHit = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidateHit = _traceHits[i];
                if (candidateHit.distance < 0f || candidateHit.transform == null ||
                    candidateHit.transform.root == characterRoot)
                {
                    continue;
                }

                if (candidateHit.distance < closestDistance)
                {
                    closestDistance = candidateHit.distance;
                    hit = candidateHit;
                    hasHit = true;
                }
            }

            return hasHit;
        }

        protected virtual void UpdateCameraPosition()
        {
            _shoulderWeight = KMath.FloatInterp(_shoulderWeight, useRightShoulder ? 0f : 1f,
                viewSmoothing, Time.deltaTime);

            KTransform root = new KTransform(transform.root)
            {
                rotation = Quaternion.identity
            };

            _aimingWeight = KMath.FloatInterp(_aimingWeight, isAiming ? 1f : 0f, viewSmoothing,
                Time.deltaTime);

            _crouchWeight = KMath.FloatInterp(_crouchWeight, isCrouching ? 1f : 0f, viewSmoothing, 
                Time.deltaTime);

            CameraPose pose = CameraPose.Lerp(thirdPersonNormal, thirdPersonAim, _aimingWeight);
            CameraPose crouchPose = CameraPose.Lerp(thirdPersonCrouch, thirdPersonCrouchAim, _aimingWeight);
            pose = CameraPose.Lerp(pose, crouchPose, _crouchWeight);
            
            Vector3 rightShoulderSocket = root.TransformPoint(pose.offset, false);
            Vector3 leftShoulderSocket = root.InverseTransformPoint(rightShoulderSocket, false);
            leftShoulderSocket.x *= -1f;
            leftShoulderSocket = root.TransformPoint(leftShoulderSocket, false);

            Vector3 targetPivotPoint = root.TransformPoint(pose.pivot, false);
            if (!_pivotInitialized)
            {
                _pivotPoint = targetPivotPoint;
                _pivotInitialized = true;
            }
            else
            {
                float pivotAlpha = KMath.ExpDecayAlpha(pivotSmoothing, Time.deltaTime);
                _pivotPoint = pivotSmoothing > 0f
                    ? Vector3.Lerp(_pivotPoint, targetPivotPoint, pivotAlpha)
                    : targetPivotPoint;
            }

            if (pivotMaxDistance > 0f)
            {
                Vector3 pivotDelta = _pivotPoint - targetPivotPoint;
                Vector2 pivotDeltaHorizontal = new Vector2(pivotDelta.x, pivotDelta.z);
                pivotDeltaHorizontal = Vector2.ClampMagnitude(pivotDeltaHorizontal, pivotMaxDistance);
                _pivotPoint = targetPivotPoint + new Vector3(pivotDeltaHorizontal.x, pivotDelta.y, pivotDeltaHorizontal.y);
            }

            Vector3 rightShoulderOffset = rightShoulderSocket - targetPivotPoint;
            Vector3 leftShoulderOffset = leftShoulderSocket - targetPivotPoint;
            Vector3 thirdPersonOffset = Vector3.Lerp(rightShoulderOffset, leftShoulderOffset, _shoulderWeight);
            Vector3 traceDirection = transform.rotation * thirdPersonOffset;
            float desiredLength = traceDirection.magnitude;
            float targetLength = desiredLength;
            Vector3 traceDirectionNormalized = Vector3.zero;
            bool isObstructed = false;
            _hit = default;

            if (desiredLength > KMath.FloatMin)
            {
                traceDirectionNormalized = traceDirection / desiredLength;

                if (TraceCameraCollision(_pivotPoint, traceDirectionNormalized, targetLength, out _hit))
                {
                    targetLength = _hit.distance;
                    isObstructed = true;
                }
            }

            _springLength = isObstructed && targetLength < _springLength
                ? targetLength
                : KMath.FloatInterp(_springLength, targetLength, traceInterpSpeed, Time.deltaTime);

            Vector3 thirdPersonPose = _pivotPoint + traceDirectionNormalized * _springLength;
            
            float target = isFirstPerson ? 0f : 1f;
            _viewWeight = KMath.FloatInterp(_viewWeight, target, viewSmoothing, Time.deltaTime);
            transform.position = Vector3.Lerp(firstPersonSocket.position, thirdPersonPose, _viewWeight);
        }

        protected virtual void UpdateCameraInput()
        {
            Quaternion yaw = Quaternion.Euler(0f, yawInput, 0f);
            
            Quaternion cameraRotation = Quaternion.Euler(pitchInput, 0f, 0f);
            cameraRotation = KAnimationMath.RotateInSpace(Quaternion.identity, cameraRotation, yaw, 1f);
            
            transform.rotation = cameraRotation;
        }

        protected virtual void UpdateCameraAnimation()
        {
            Quaternion animatedPose = Quaternion.identity;
            if (cameraAnimationSource != null) animatedPose = cameraAnimationSource.localRotation;
            transform.rotation *= animatedPose;
        }
        
        protected virtual void UpdateCameraShake()
        {
            if (_activeShake == null) return;

            float length = _activeShake.shakeCurve.GetCurveLength();
            _cameraShakePlayback += Time.deltaTime * _activeShake.playRate;
            _cameraShakePlayback = Mathf.Clamp(_cameraShakePlayback, 0f, length);

            float alpha = KMath.ExpDecayAlpha(_activeShake.smoothSpeed, Time.deltaTime);
            if (!KAnimationMath.IsWeightRelevant(alpha)) alpha = 1f;
            
            Vector3 target = _activeShake.shakeCurve.GetValue(_cameraShakePlayback);
            target.x *= _cameraShakeTarget.x;
            target.y *= _cameraShakeTarget.y;
            target.z *= _cameraShakeTarget.z;
            
            _cameraShake = Vector3.Lerp(_cameraShake, target, alpha);
            transform.rotation *= Quaternion.Euler(_cameraShake);
        }

        protected virtual void UpdateFOV()
        {
            if (_camera == null) return;

            _camera.fieldOfView = KMath.FloatInterp(_camera.fieldOfView, _targetFov, _fovSmoothing,
                Time.deltaTime);
        }

        protected void LateUpdate()
        {
            UpdateCameraInput();
            UpdateCameraPosition();
            UpdateCameraAnimation();
            UpdateCameraShake();
            UpdateFOV();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            
            Color prevColor = Gizmos.color;
            Gizmos.color = Color.green;
            Handles.Label(_pivotPoint, "Camera Pivot Point");
            Gizmos.DrawWireSphere(_pivotPoint, 0.05f);
            Gizmos.color = prevColor;

            if (_hit.transform != null)
            {
                prevColor = Gizmos.color;
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_pivotPoint, 0.02f);
                Gizmos.color = prevColor;
            }
        }
#endif
    }
}