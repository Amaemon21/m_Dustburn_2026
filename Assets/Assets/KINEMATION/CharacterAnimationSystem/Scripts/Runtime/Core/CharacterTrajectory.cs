// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [AddComponentMenu("KINEMATION/CAS/Character Trajectory")]
    [DisallowMultipleComponent]
    public class CharacterTrajectory : MonoBehaviour
    {
        [Header("Trajectory")]
        [SerializeField]
        [Tooltip("Number of trajectory samples stored behind the current root position.")]
        [Min(0)]
        private int pastTrajectoryPoints = 6;
        [SerializeField]
        [Tooltip("Number of predicted trajectory samples stored ahead of the current root position.")]
        [Min(0)]
        private int futureTrajectoryPoints = 6;
        [SerializeField]
        [Tooltip("Time interval, in seconds, between trajectory samples.")]
        [Min(0.01f)]
        private float trajectoryTimeDelta = 0.1f;
        [SerializeField]
        [Tooltip("Draw trajectory gizmos while the game is running.")]
        private bool drawGizmos = false;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Drive the trajectory from the debug velocity settings instead of transform motion.")]
        private bool useDebugVelocity;
        [SerializeField]
        [Tooltip("Debug velocity in root space. Root rotation is applied every frame so turning produces curved trajectories.")]
        private Vector3 debugVelocity = Vector3.zero;
        [SerializeField]
        [Tooltip("Synthetic turn rate in degrees per second for debug trajectory generation.")]
        private float debugAngularVelocity;

        private Vector3[] _pastSamples;
        private Vector3[] _trajectoryOffsets;
        private Vector3 _lastRootPosition;
        private Vector3 _velocity;
        private Vector3 _trajectoryDirection;
        private float _planarSpeed;
        private float _angularVelocity;
        private float _trajectorySampleTimer;

        public int RootIndex => _pastSamples == null ? Mathf.Max(0, pastTrajectoryPoints) : _pastSamples.Length;
        public Vector3[] TrajectoryOffsets => _trajectoryOffsets;
        public Vector3 Velocity => _velocity;

        private Vector3 GetFallbackForward(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return forward.sqrMagnitude < KMath.SqrEpsilon ? Vector3.forward : forward.normalized;
        }

        private void PushPastSample(Vector3 sample)
        {
            int count = _pastSamples == null ? 0 : _pastSamples.Length;
            if (count == 0)
            {
                return;
            }

            for (int i = 1; i < count; i++)
            {
                _pastSamples[i - 1] = _pastSamples[i];
            }

            _pastSamples[count - 1] = sample;
        }

        private void InitializeTrajectoryState()
        {
            int pastCount = Mathf.Max(0, pastTrajectoryPoints);
            int totalCount = pastCount + Mathf.Max(0, futureTrajectoryPoints) + 1;
            Vector3 rootPosition = transform.position;
            Quaternion rootRotation = transform.rotation;

            _pastSamples = new Vector3[pastCount];
            _trajectoryOffsets = new Vector3[totalCount];

            for (int i = 0; i < _pastSamples.Length; i++)
            {
                _pastSamples[i] = rootPosition;
            }

            for (int i = 0; i < _trajectoryOffsets.Length; i++)
            {
                _trajectoryOffsets[i] = Vector3.zero;
            }

            _lastRootPosition = rootPosition;
            _velocity = Vector3.zero;
            _trajectoryDirection = GetFallbackForward(rootRotation);
            _planarSpeed = 0f;
            _angularVelocity = 0f;
            _trajectorySampleTimer = 0f;

            BuildTrajectoryOffsets(rootPosition, rootRotation);
        }

        private void BuildTrajectoryOffsets(Vector3 rootPosition, Quaternion rootRotation)
        {
            int pastCount = _pastSamples == null ? 0 : _pastSamples.Length;

            for (int i = 0; i < pastCount; i++)
            {
                _trajectoryOffsets[i] = _pastSamples[i] - rootPosition;
                _trajectoryOffsets[i].y = 0f;
            }

            _trajectoryOffsets[pastCount] = Vector3.zero;

            int futureCount = Mathf.Max(0, futureTrajectoryPoints);
            if (futureCount == 0)
            {
                return;
            }

            float stepTime = Mathf.Max(trajectoryTimeDelta, 0.01f);
            float stepAngle = _angularVelocity * stepTime;
            float stepDistance = _planarSpeed * stepTime;

            Vector3 predictedDirection = _trajectoryDirection;
            if (predictedDirection.sqrMagnitude < KMath.SqrEpsilon)
            {
                predictedDirection = GetFallbackForward(rootRotation);
            }

            Vector3 predictedPosition = rootPosition;
            Quaternion stepRotation = Quaternion.AngleAxis(stepAngle, Vector3.up);

            for (int i = 0; i < futureCount; i++)
            {
                predictedDirection = stepRotation * predictedDirection;

                if (predictedDirection.sqrMagnitude < KMath.SqrEpsilon)
                {
                    predictedDirection = GetFallbackForward(rootRotation);
                }

                predictedDirection.Normalize();
                predictedPosition += predictedDirection * stepDistance;

                _trajectoryOffsets[pastCount + i + 1] = predictedPosition - rootPosition;
                _trajectoryOffsets[pastCount + i + 1].y = 0f;
            }
        }

        private void DrawTrajectoryGizmos()
        {
            if (_trajectoryOffsets == null || _trajectoryOffsets.Length == 0)
            {
                return;
            }

            Vector3 rootPosition = transform.position;
            Quaternion rootRotation = transform.rotation;
            int rootIndex = _pastSamples == null ? 0 : _pastSamples.Length;
            Vector3 previousPoint = rootPosition + _trajectoryOffsets[0];

            for (int i = 0; i < _trajectoryOffsets.Length; i++)
            {
                Vector3 point = rootPosition + _trajectoryOffsets[i];

                if (i > 0)
                {
                    Gizmos.color = i <= rootIndex
                        ? new Color(0.3f, 0.8f, 1f, 1f)
                        : new Color(1f, 0.75f, 0.2f, 1f);
                    Gizmos.DrawLine(previousPoint, point);
                }

                Gizmos.color = i == rootIndex ? Color.white : (i < rootIndex ? Color.cyan : Color.yellow);
                Gizmos.DrawWireSphere(point, i == rootIndex ? 0.04f : 0.025f);
                previousPoint = point;
            }

            Gizmos.color = Color.green;
            Gizmos.DrawRay(rootPosition, rootRotation * Vector3.forward * 0.35f);
        }

        private void UpdateTrajectoryData()
        {
            Vector3 rootPosition = transform.position;
            Quaternion rootRotation = transform.rotation;
            
            float deltaTime = Time.deltaTime;
            Vector3 previousDirection = _trajectoryDirection;
            float safeDeltaTime = Mathf.Max(deltaTime, 0.0001f);
            float sampleStep = Mathf.Max(trajectoryTimeDelta, 0.01f);

            if (useDebugVelocity)
            {
                _lastRootPosition = rootPosition;
                _velocity = rootRotation * debugVelocity;

                Vector3 planarVelocity = Vector3.ProjectOnPlane(_velocity, Vector3.up);
                _planarSpeed = planarVelocity.magnitude;

                Vector3 nextDirection = _planarSpeed > 0.001f
                    ? planarVelocity / _planarSpeed
                    : _trajectoryDirection;

                if (nextDirection.sqrMagnitude < KMath.SqrEpsilon)
                {
                    nextDirection = GetFallbackForward(rootRotation);
                }

                _angularVelocity = debugAngularVelocity;
                _trajectoryDirection = nextDirection;
                _trajectorySampleTimer = 0f;

                int pastCount = _pastSamples == null ? 0 : _pastSamples.Length;
                Vector3 samplePosition = rootPosition;
                Vector3 sampleDirection = _trajectoryDirection;
                Quaternion reverseStepRotation =
                    Quaternion.AngleAxis(-_angularVelocity * sampleStep, Vector3.up);

                if (sampleDirection.sqrMagnitude < KMath.SqrEpsilon)
                {
                    sampleDirection = GetFallbackForward(rootRotation);
                }

                sampleDirection.y = 0f;
                if (sampleDirection.sqrMagnitude < KMath.SqrEpsilon)
                {
                    sampleDirection = Vector3.forward;
                }

                sampleDirection.Normalize();

                for (int i = pastCount - 1; i >= 0; i--)
                {
                    samplePosition -= sampleDirection * (_planarSpeed * sampleStep);
                    _pastSamples[i] = samplePosition;
                    sampleDirection = reverseStepRotation * sampleDirection;

                    sampleDirection.y = 0f;
                    if (sampleDirection.sqrMagnitude < KMath.SqrEpsilon)
                    {
                        sampleDirection = Vector3.forward;
                    }
                    else
                    {
                        sampleDirection.Normalize();
                    }
                }
            }
            else
            {
                Vector3 previousRootPosition = _lastRootPosition;
                Vector3 delta = rootPosition - previousRootPosition;

                _lastRootPosition = rootPosition;
                _velocity = delta / safeDeltaTime;

                Vector3 planarVelocity = Vector3.ProjectOnPlane(_velocity, Vector3.up);
                _planarSpeed = planarVelocity.magnitude;

                Vector3 nextDirection = _planarSpeed > 0.001f
                    ? planarVelocity / _planarSpeed
                    : _trajectoryDirection;
                
                if (nextDirection.sqrMagnitude < KMath.SqrEpsilon)
                {
                    nextDirection = GetFallbackForward(rootRotation);
                }

                _angularVelocity = previousDirection.sqrMagnitude >= KMath.SqrEpsilon &&
                                   nextDirection.sqrMagnitude >= KMath.SqrEpsilon
                    ? Vector3.SignedAngle(previousDirection, nextDirection, Vector3.up) / safeDeltaTime
                    : 0f;

                _trajectoryDirection = nextDirection;
                _trajectorySampleTimer += deltaTime;

                while (_trajectorySampleTimer >= sampleStep)
                {
                    float alpha = 1f - (_trajectorySampleTimer - sampleStep) / safeDeltaTime;
                    alpha = Mathf.Clamp01(alpha);

                    PushPastSample(Vector3.Lerp(previousRootPosition, rootPosition, alpha));
                    _trajectorySampleTimer -= sampleStep;
                }
            }

            BuildTrajectoryOffsets(rootPosition, rootRotation);
        }

        private void Start()
        {
            InitializeTrajectoryState();
        }

        private void Update()
        {
            UpdateTrajectoryData();
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || !drawGizmos)
            {
                return;
            }

            DrawTrajectoryGizmos();
#endif
        }
    }
}