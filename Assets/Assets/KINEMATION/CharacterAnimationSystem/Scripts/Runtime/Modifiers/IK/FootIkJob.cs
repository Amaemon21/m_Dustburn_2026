// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct FootTraceResult
    {
        public RaycastHit hitResult;
        public bool isHit;
        public float targetHeight;
        public Quaternion targetRotation;
    }
    
    public struct FootIkJob : IAnimationJob, IAnimationModifierJob
    {
        private FootIkSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _pelvisHandle;
        private TransformStreamHandle _rightFootHandle;
        private TransformStreamHandle _leftFootHandle;

        private Vector3 _pelvisPosition;
        private KTransform _rightFootPose;
        private KTransform _leftFootPose;

        private Vector3 _offset;

        private Quaternion _rightFootRotation;
        private Quaternion _leftFootRotation;
        private bool _isValidSetup;

        private CharacterController _controller;
        private CapsuleCollider _capsuleCollider;
        private BindableProperty<bool> _isGroundedProp;

        private float _smoothRootY;
        
#if UNITY_EDITOR
        private Transform _pelvis;
        private Transform _rightFootIk;
        private Transform _leftFootIk;
#endif
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_isValidSetup || !KAnimationMath.IsWeightRelevant(_jobData.weight)) return;
            if (!_pelvisHandle.IsValid(stream) || !_rightFootHandle.IsValid(stream) || !_leftFootHandle.IsValid(stream))
            {
                return;
            }
            
            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);

            Vector3 smoothOffset = new Vector3(0f, _smoothRootY - rootTransform.position.y, 0f);
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _pelvisHandle, smoothOffset, _jobData.weight);
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _rightFootHandle, smoothOffset, _jobData.weight);
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _leftFootHandle, smoothOffset, _jobData.weight);
            
            _pelvisPosition = rootTransform.InverseTransformPoint(_pelvisHandle.GetPosition(stream), false);

            var footTransform = KAnimationMath.GetTransform(stream, _rightFootHandle);
            _rightFootPose = rootTransform.GetRelativeTransform(footTransform, false);
            
            footTransform = KAnimationMath.GetTransform(stream, _leftFootHandle);
            _leftFootPose = rootTransform.GetRelativeTransform(footTransform, false);
            
            _rightFootPose.rotation = _rightFootHandle.GetRotation(stream);
            _leftFootPose.rotation = _leftFootHandle.GetRotation(stream);
            
            _offset.z = Mathf.Min(Mathf.Min(_offset.x, _offset.y), 0f);
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _pelvisHandle,
                new Vector3(0f, _offset.z, 0f), _jobData.weight);
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _rightFootHandle, 
                new Vector3(0f, _offset.x, 0f), _jobData.weight);
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _leftFootHandle, 
                new Vector3(0f, _offset.y, 0f), _jobData.weight);
            
            _rightFootHandle.SetRotation(stream, Quaternion.Slerp(_rightFootPose.rotation, 
                _rightFootRotation * _rightFootPose.rotation, _jobData.weight));
            _leftFootHandle.SetRotation(stream, Quaternion.Slerp(_leftFootPose.rotation, 
                _leftFootRotation * _leftFootPose.rotation, _jobData.weight));
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = settings as FootIkSettings;
            _isValidSetup = false;
            
            if (_settings == null)
            {
                return;
            }
             
            _pelvisHandle = AnimationModifierUtility.GetHandle(in _jobData, _settings.pelvis);
            _rightFootHandle = AnimationModifierUtility.GetHandle(in _jobData, _settings.rightFootIk);
            _leftFootHandle = AnimationModifierUtility.GetHandle(in _jobData, _settings.leftFootIk);

            Transform root = _jobData.animator.transform.root;
            _smoothRootY = root.position.y;
            
            _controller = root.GetComponentInChildren<CharacterController>();
            _capsuleCollider = root.GetComponentInChildren<CapsuleCollider>();
            _isGroundedProp = _settings.isGrounded.CreateProperty(_jobData.animator.gameObject);

            Transform pelvis = _jobData.skeleton?.GetBoneTransform(_settings.pelvis.name);
            Transform rightFoot = _jobData.skeleton?.GetBoneTransform(_settings.rightFootIk.name);
            Transform leftFoot = _jobData.skeleton?.GetBoneTransform(_settings.leftFootIk.name);

            _isValidSetup = pelvis != null && rightFoot != null && leftFoot != null;
            if (!_isValidSetup)
            {
                Debug.LogWarning($"{nameof(FootIkJob)}: Missing pelvis/foot IK references. Modifier will be ignored.");
            }
             
#if UNITY_EDITOR
            _pelvis = pelvis;
            _rightFootIk = rightFoot;
            _leftFootIk = leftFoot;
#endif
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

        private Quaternion ComputeFootRotationOffset(RaycastHit hit, float maxAngleDegrees = 30f)
        {
            float slopeAngle = Vector3.Angle(Vector3.up, hit.normal);

            Vector3 effectiveNormal = hit.normal;
            
            if (slopeAngle > maxAngleDegrees)
            {
                effectiveNormal = Vector3.RotateTowards(Vector3.up, hit.normal, 
                    maxAngleDegrees * Mathf.Deg2Rad, 0f
                );
            }

            Quaternion rootRotation = _jobData.animator.transform.rotation;

            Quaternion rootAligned =
                AnimationModifierUtility.AlignRotationWithPlane(rootRotation, Vector3.forward, effectiveNormal);
        
            Quaternion deltaWorld = rootAligned * Quaternion.Inverse(rootRotation);
            return deltaWorld;
        }

        private FootTraceResult TraceFoot(in KTransform footPose)
        {
            FootTraceResult result = new FootTraceResult();
            
            KTransform root = new KTransform(_jobData.animator.transform);
            Vector3 direction = -root.up;
            
            Vector3 origin = footPose.position;
            float distance = _pelvisPosition.y - origin.y + _settings.traceOffset;
            origin.y = _pelvisPosition.y;
            
            result.isHit = Physics.SphereCast(origin, _settings.traceRadius, direction, out result.hitResult, 
                distance, _settings.layerMask);

            // Ignore collisions with the character collider/objects.
            if (result.isHit && result.hitResult.transform.root == _jobData.animator.transform.root)
            {
                result.isHit = false;
            }

            Vector3 localHitPoint = root.InverseTransformPoint(result.hitResult.point, false);
            if (result.isHit && !Mathf.Approximately(localHitPoint.y, 0f))
            {
                result.targetHeight = localHitPoint.y;
                result.targetRotation = ComputeFootRotationOffset(result.hitResult);
            }
            else
            {
                result.targetHeight = 0f;
                result.targetRotation = Quaternion.identity;
            }

            return result;
        }
        
        private void ProcessFootIK()
        {
            var rightFootResult = TraceFoot(_rightFootPose);
            var leftFootResult = TraceFoot(_leftFootPose);
            
            bool isGrounded = false;
            if (_isGroundedProp.IsBound)
            {
                isGrounded = _isGroundedProp.GetValue();
            }
            else if (_controller != null)
            {
                isGrounded = _controller.isGrounded;
            }
            else if (_capsuleCollider != null)
            {
                Transform capsuleTransform = _capsuleCollider.transform;
                Vector3 start = capsuleTransform.position;
                Vector3 end = start + capsuleTransform.up * (_capsuleCollider.height * 0.5f);

                isGrounded = Physics.CheckCapsule(end, start, _capsuleCollider.radius);
            }
            
            // Disable foot trace when in air.
            if (!isGrounded)
            {
                rightFootResult.targetHeight = leftFootResult.targetHeight = 0f;
                rightFootResult.targetRotation = leftFootResult.targetRotation = Quaternion.identity;
                rightFootResult.isHit = leftFootResult.isHit = false;
                _smoothRootY = _jobData.animator.transform.position.y;
            }
            
            _rightFootRotation = KMath.SmoothSlerp(_rightFootRotation, rightFootResult.targetRotation, 
                _settings.interpSpeed, Time.deltaTime);
            _leftFootRotation = KMath.SmoothSlerp(_leftFootRotation, leftFootResult.targetRotation, 
                _settings.interpSpeed, Time.deltaTime);
            
            _offset.x = KMath.FloatInterp(_offset.x, rightFootResult.targetHeight,
                _settings.interpSpeed, Time.deltaTime);
            _offset.y = KMath.FloatInterp(_offset.y, leftFootResult.targetHeight,
                _settings.interpSpeed, Time.deltaTime);
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            _jobData.weight = weight;
            if (!_isValidSetup || _jobData.animator == null)
            {
                playable.SetJobData(this);
                return;
            }
            
            var job = playable.GetJobData<FootIkJob>();
            _pelvisPosition = job._pelvisPosition;
            _rightFootPose = job._rightFootPose;
            _leftFootPose = job._leftFootPose;
            
            Transform root = _jobData.animator.transform;
            _smoothRootY = KMath.FloatInterp(_smoothRootY, root.position.y, _settings.rootInterpSpeed, Time.deltaTime);
            
            _pelvisPosition = root.TransformPoint(_pelvisPosition);
            _rightFootPose.position = root.TransformPoint(_rightFootPose.position);
            _leftFootPose.position = root.TransformPoint(_leftFootPose.position);
            
            ProcessFootIK();
            
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
            if(_pelvis == null) return;

            if (_rightFootIk != null)
            {
                Vector3 origin = _rightFootIk.position;
                origin.y = _pelvis.position.y;
                
                AnimationModifierUtility.DrawCapsule(origin, _rightFootIk.position, _settings.traceRadius, 
                    Color.green);
            }
            
            if (_leftFootIk != null)
            {
                Vector3 origin = _leftFootIk.position;
                origin.y = _pelvis.position.y;
                
                AnimationModifierUtility.DrawCapsule(origin, _leftFootIk.position, _settings.traceRadius, 
                    Color.green);
            }
#endif 
        }
        
        public void OnSceneGUI()
        {
        }
    }
}