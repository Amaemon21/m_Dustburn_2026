// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct PivotModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private PivotModifierSettings _settings;

        private ModifierJobData _jobData;

        private float _playback;
        private float _length;
        private Quaternion _lastRotation;
        private Vector3 _lastVelocity;
        private Vector3 _lastPosition;

        private Vector2 _rotationPivot;
        private Vector2 _positionPivot;

        private bool _wasMoving;

        private TransformStreamHandle _pelvisHandle;
        private TransformStreamHandle _ikFootRightHandle;
        private TransformStreamHandle _ikFootLeftHandle;
        
        private NativeArray<LookLayerAtom> _spineBones;

        private MotionWarpingComponent _motionWarping;
        private bool _isEnabled;

        private BindableProperty<Vector2> _moveInputProp;
         
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight)) return;
            
            float fraction = _rotationPivot.x / 90f;
            bool sign = fraction > 0f;
            
            foreach (var element in _spineBones)
            {
                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;
                
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, element.handle, 
                    Quaternion.Euler(0f, 0f, angle * fraction), _jobData.weight);
            }
            
            fraction = _rotationPivot.y / 90f;
            sign = fraction > 0f;

            foreach (var element in _spineBones)
            {
                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;

                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, element.handle, 
                    Quaternion.Euler(angle * fraction, 0f, 0f), _jobData.weight);
            }
            
            KTransform rightFootCache = KTransform.Identity, leftFootCache = KTransform.Identity;
            bool ikFeetValid = _ikFootRightHandle.IsValid(stream) && _ikFootLeftHandle.IsValid(stream);

            if (ikFeetValid)
            {
                rightFootCache = KAnimationMath.GetTransform(stream, _ikFootRightHandle);
                leftFootCache = KAnimationMath.GetTransform(stream, _ikFootLeftHandle);
            }

            Vector3 offset = new Vector3(_positionPivot.x, 0f, _positionPivot.y);
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _pelvisHandle, offset, _jobData.weight);

            if (ikFeetValid)
            {
                _ikFootRightHandle.SetPosition(stream, rightFootCache.position);
                _ikFootRightHandle.SetRotation(stream, rightFootCache.rotation);
                
                _ikFootLeftHandle.SetPosition(stream, leftFootCache.position);
                _ikFootLeftHandle.SetRotation(stream, leftFootCache.rotation);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _settings = settings as PivotModifierSettings;
            _jobData = jobData;

            _lastPosition = jobData.animator.transform.position;
            
            _length = Mathf.Max(_settings.rotationCurve.keys[^1].time, _settings.translationCurve.keys[^1].time);
            _playback = _length + 1f;
            _pelvisHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.pelvis);

            _ikFootRightHandle =
                AnimationModifierUtility.GetHandle(in jobData, new KRigElement(-1, CasNames.Bone_IkFootRight));
            _ikFootLeftHandle =
                AnimationModifierUtility.GetHandle(in jobData, new KRigElement(-1, CasNames.Bone_IkFootLeft));
            
            LookModifierJob.SetupChain(ref _spineBones, in jobData, _settings.spineBones);

            _motionWarping = jobData.animator.transform.root.GetComponentInChildren<MotionWarpingComponent>();

            _moveInputProp = _settings.moveInput.CreateProperty(_jobData.animator.gameObject);
            _isEnabled = true;
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
            _jobData.weight = weight;
            Transform root = _jobData.animator.transform;
            
            Quaternion rotation = root.rotation;
            Vector3 position = root.position;
            
            Vector3 velocity = (position - _lastPosition) / Time.deltaTime;
            velocity.y = 0f;
            _lastPosition = position;

            bool isMoving = _moveInputProp.IsValid()
                ? _moveInputProp.GetValue().magnitude > 0f
                : velocity.magnitude > 0.001f;
            
            Vector2 rotationPivot = Vector2.zero;
            Vector2 positionPivot = Vector2.zero;
            
            if (isMoving)
            {
                Quaternion deltaRotation = Quaternion.Inverse(_lastRotation) * rotation;
                float yawDelta = KMath.NormalizeEulerAngle(deltaRotation.eulerAngles.y);
                rotationPivot.x = -yawDelta * _settings.leanIntensity;
            }

            _lastRotation = rotation;
            
            if (isMoving != _wasMoving)
            {
                _playback = 0f;
            }
            
            if (_motionWarping != null)
            {
                bool hasActiveWarping = _motionWarping.IsActive();
                
                if (hasActiveWarping && _isEnabled) _isEnabled = false;
                if (!hasActiveWarping && !_isEnabled)
                {
                    isMoving = _wasMoving = false;
                    _playback = _length + 1f;
                    _isEnabled = true;
                }
            }
            
            bool isPlaying = _playback < _length;
            if (isPlaying)
            {
                float rotationValue = _settings.rotationCurve.Evaluate(_playback);
                float positionValue = _settings.translationCurve.Evaluate(_playback);
                float localYaw = 0f;

                if (_lastVelocity.normalized.magnitude > 0f)
                {
                    Vector3 localVelocity = Quaternion.Inverse(rotation) * _lastVelocity.normalized;
                    localVelocity.y = 0f;
                    localYaw = KMath.NormalizeEulerAngle(Quaternion.LookRotation(localVelocity).eulerAngles.y);
                }
                
                float alpha = Mathf.InverseLerp(45f, 135f, Mathf.Abs(localYaw));
                alpha = Mathf.Cos(alpha * Mathf.PI);
                
                positionPivot.y = alpha * positionValue;
                rotationPivot.y = Mathf.Lerp(0f, alpha, rotationValue) * _settings.maxLeanAngle;
                
                alpha = -Mathf.Sin(localYaw * Mathf.Deg2Rad);
                positionPivot.x = alpha * positionValue;
                rotationPivot.x += Mathf.Lerp(0f, alpha, rotationValue) * _settings.maxLeanAngle;
                
                _playback += Time.deltaTime * _settings.playbackSpeed;
            }
            
            rotationPivot.x = Mathf.Clamp(rotationPivot.x, -_settings.maxLeanAngle, _settings.maxLeanAngle);
            rotationPivot.y = Mathf.Clamp(rotationPivot.y, -_settings.maxLeanAngle, _settings.maxLeanAngle);

            _rotationPivot.x = KMath.FloatInterp(_rotationPivot.x, rotationPivot.x, 
                _settings.rotationSmoothing, Time.deltaTime);
            _rotationPivot.y = KMath.FloatInterp(_rotationPivot.y, rotationPivot.y , 
                _settings.rotationSmoothing, Time.deltaTime);
            
            _positionPivot.x = KMath.FloatInterp(_positionPivot.x, -positionPivot.x, 
                _settings.positionSmoothing, Time.deltaTime);
            _positionPivot.y = KMath.FloatInterp(_positionPivot.y, positionPivot.y, 
                _settings.positionSmoothing, Time.deltaTime);

            if(isMoving) _lastVelocity = velocity;
            _wasMoving = isMoving;
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            if (_spineBones.IsCreated) _spineBones.Dispose();
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}