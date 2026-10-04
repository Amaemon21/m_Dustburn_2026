// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct LookLayerAtom
    {
        public TransformStreamHandle handle;
        public Vector2 clampedAngle;
    }
    
    public struct LookModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private LookModifierSettings _settings;
        private ModifierJobData _jobData;
        
        // Look properties.
        private Vector3 _lookInput;
        private NativeArray<LookLayerAtom> _rollElements;
        private NativeArray<LookLayerAtom> _pitchElements;
        private NativeArray<LookLayerAtom> _yawElements;
        private NativeArray<TransformStreamHandle> _bonesToPreserve;

        private BindableProperty<float> _pitchInput;
        private BindableProperty<float> _yawInput;
        private BindableProperty<float> _rollInput;
        
        private float _prevYawInput;
        private Quaternion _yawRotation;
        
        // Turn properties.
        private TransformStreamHandle _modelRootHandle;
        private Quaternion _prevRootRotation;
        
        private float _turnPlayback;
        private float _turnAngle;
        private float _cachedTurnAngle;
        
        private bool _isTurning;

        private int _turnRightHash;
        private int _turnLeftHash;

        private Vector3 _prevPosition;
        private float _turnSmoothWeight;

        private bool _isInitialized;
        private PropertyStreamHandle _turnAngleHandle;
        
        public Quaternion YawRotation => _yawRotation;
        public float TurnAngle => _turnAngle;
        public bool IsTurning => _isTurning;
        
#if UNITY_EDITOR
        private List<Transform> _lookBones;
#endif
        
        public static void SetupChain(ref NativeArray<LookLayerAtom> chain, in ModifierJobData jobData, 
            List<LookLayerElement> elements)
        {
            int count = elements.Count;

            if (!chain.IsCreated || chain.Length != elements.Count)
            {
                if (chain.IsCreated) chain.Dispose(); 
                chain = new NativeArray<LookLayerAtom>(count, Allocator.Persistent);
            }
            
            for (int i = 0; i < count; i++)
            {
                chain[i] = new LookLayerAtom()
                {
                    handle = AnimationModifierUtility.GetHandle(in jobData, elements[i].rigElement),
                    clampedAngle = elements[i].clampedAngle
                };
            }
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            float globalWeight = _jobData.weight;
            if (!KAnimationMath.IsWeightRelevant(globalWeight)) return;
            
            if(_turnAngleHandle.IsValid(stream)) _turnAngleHandle.SetFloat(stream, -_turnAngle);
            
            if (_settings.enableTurnInPlace)
            {
                Quaternion offset = Quaternion.Euler(0f, -_turnAngle, 0f);
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _modelRootHandle, offset, 1f);

                KTransform root = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
                Vector3 modelRootPosition = _modelRootHandle.GetPosition(stream);

                modelRootPosition = root.InverseTransformPoint(modelRootPosition, false);
                modelRootPosition = offset * modelRootPosition - modelRootPosition;

                KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _modelRootHandle, modelRootPosition, 1f);

                // Preserve bone poses because of Humanoids resetting the root bone transform each call.
                if (stream.isHumanStream && _bonesToPreserve.IsCreated)
                {
                    for (int i = 0; i < _bonesToPreserve.Length; i++)
                    {
                        var handle = _bonesToPreserve[i];
                        if (!handle.IsValid(stream)) continue;
                        
                        Vector3 bonePosition = handle.GetPosition(stream);
                        bonePosition = root.InverseTransformPoint(bonePosition, false);
                        bonePosition = offset * bonePosition - bonePosition;
                        
                        KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, handle, offset, 1f);
                        KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, handle, bonePosition, 1f);
                    }
                }
            }

            // Compute root rotation based on the turn in place value.
            Quaternion rootRotation = Quaternion.Euler(0f, -_turnAngle, 0f);
            rootRotation = _jobData.rootHandle.GetRotation(stream) * rootRotation;
            
            float fraction = _lookInput.z * globalWeight / 90f;
            bool sign = fraction > 0f;
            
            foreach (var element in _rollElements)
            {
                if (!element.handle.IsValid(stream)) continue;
                
                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;

                var rotation = KAnimationMath.RotateInSpace(rootRotation, element.handle.GetRotation(stream),
                    Quaternion.Euler(0f, 0f, angle * fraction), 1f);
                element.handle.SetRotation(stream, rotation);
            }
            
            // Compute yaw delta look rotation.
            float yawInput = KMath.NormalizeEulerAngle((Quaternion.Inverse(rootRotation) * _yawRotation).eulerAngles.y); ;
            yawInput *= globalWeight;
            
            fraction = yawInput / 90f;
            sign = fraction > 0f;
            
            foreach (var element in _yawElements)
            {
                if (!element.handle.IsValid(stream)) continue;
                
                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, element.handle, 
                    Quaternion.Euler(0f, angle * fraction, 0f), 1f);
            }
            
            fraction = _lookInput.y / 90f;
            sign = fraction > 0f;
            
            // Add the yaw input to the root orientation.
            rootRotation *= Quaternion.Euler(0f, yawInput, 0f);

            foreach (var element in _pitchElements)
            {
                if (!element.handle.IsValid(stream)) continue;
                
                float angle = sign ? element.clampedAngle.x : element.clampedAngle.y;

                Quaternion rotation = element.handle.GetRotation(stream);
                rotation = KAnimationMath.RotateInSpace(rootRotation, rotation, 
                    Quaternion.Euler(angle * fraction, 0f, 0f), globalWeight);
                element.handle.SetRotation(stream, rotation);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (LookModifierSettings) settings;
            
            SetupChain(ref _rollElements, in jobData, _settings.rollOffsetElements);
            SetupChain(ref _pitchElements, in jobData, _settings.pitchOffsetElements);
            SetupChain(ref _yawElements, in jobData, _settings.yawOffsetElements);

            if (jobData.animator.isHuman)
            {
                if (!_bonesToPreserve.IsCreated || _bonesToPreserve.Length != _settings.bonesToPreserve.Count)
                {
                    if (_bonesToPreserve.IsCreated) _bonesToPreserve.Dispose();
                    _bonesToPreserve = new NativeArray<TransformStreamHandle>(_settings.bonesToPreserve.Count, 
                            Allocator.Persistent);
                }

                for (int i = 0; i < _settings.bonesToPreserve.Count; i++)
                {
                    _bonesToPreserve[i] = AnimationModifierUtility.GetHandle(_jobData,
                        _settings.bonesToPreserve.elementChain[i]);
                }
            }

            _pitchInput = _settings.pitchInput.CreateProperty(_jobData.animator.gameObject);
            _yawInput = _settings.yawInput.CreateProperty(_jobData.animator.gameObject);
            _rollInput = _settings.rollInput.CreateProperty(_jobData.animator.gameObject);
            
            if (!_isInitialized)
            {
                _yawRotation = _jobData.animator.transform.rotation;
                _isInitialized = true;
            }
            
            _modelRootHandle = 
                AnimationModifierUtility.GetHandle(in _jobData, jobData.skeleton.GetRigElement(0));

            _turnAngle = _cachedTurnAngle = 0f;
            _turnSmoothWeight = 1f;
            _turnPlayback = 1f;
            _turnAngle = _cachedTurnAngle = 0f;

            _turnRightHash = Animator.StringToHash(_settings.turnRightState);
            _turnLeftHash = Animator.StringToHash(_settings.turnLeftState);

            if (!_jobData.customPropHandles.TryGetValue("TurnAngle", out _turnAngleHandle))
            {
                _turnAngleHandle =
                    _jobData.animator.BindCustomStreamProperty("TurnAngle", CustomStreamPropertyType.Float);
            }
            
#if UNITY_EDITOR
            var boneSet = new HashSet<LookLayerElement>();
            boneSet.UnionWith(_settings.rollOffsetElements);
            boneSet.UnionWith(_settings.pitchOffsetElements);
            boneSet.UnionWith(_settings.yawOffsetElements);

            var orderedBones = boneSet.OrderBy(bone => bone.rigElement.index).ToList();

            _lookBones = new List<Transform>();
            foreach (var bone in orderedBones)
            {
                _lookBones.Add(jobData.skeleton.GetBoneTransform(bone.rigElement.name));
            }
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
            _lookInput.y = _pitchInput.GetValue();
            _lookInput.x = _yawInput.GetValue();
            _lookInput.z = _rollInput.GetValue();
            
            if (_settings.useYawAsDelta)
            {
                _yawRotation *= Quaternion.Euler(0f, _lookInput.x, 0f);
            }
            else
            {
                _yawRotation = Quaternion.Euler(0f, _lookInput.x, 0f);
            }
            
            _yawRotation.Normalize();
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            _jobData.weight = weight;

            if (_settings.enableTurnInPlace)
            {
                Vector3 position = _jobData.animator.transform.position;
                bool isMoving = (position - _prevPosition).magnitude > 0.001f;
                _prevPosition = position;
                
                _turnSmoothWeight = KMath.FloatInterp(_turnSmoothWeight, isMoving ? 0f : 1f, 10f, 
                    Time.deltaTime);
                
                Quaternion rootRotation = _jobData.animator.transform.rotation;
                Quaternion delta = Quaternion.Inverse(_prevRootRotation) * rootRotation;
                _prevRootRotation = rootRotation;

                _turnAngle += KMath.NormalizeEulerAngle(delta.eulerAngles.y);

                if (!_isTurning && Mathf.Abs(_turnAngle) > _settings.angleThreshold)
                {
                    _jobData.animator.CrossFade(_turnAngle > 0f ? _turnRightHash : _turnLeftHash,
                        0f, -1, 0f, 0f);
                    _cachedTurnAngle = _turnAngle;
                    _isTurning = true;
                    _turnPlayback = 0f;
                }

                if (_isTurning)
                {
                    _turnPlayback = Mathf.Clamp01(_turnPlayback + Time.deltaTime * _settings.turnSpeed);
                    float alpha = _settings.turnCurve.Evaluate(_turnPlayback);

                    _turnAngle = Mathf.Lerp(_cachedTurnAngle, 0f, alpha);
                    if (Mathf.Approximately(_turnPlayback, 1f)) _isTurning = false;
                }

                _turnAngle *= _turnSmoothWeight;
            }
            
            playable.SetJobData(this);
        }
        
        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            if (_rollElements.IsCreated) _rollElements.Dispose();
            if (_pitchElements.IsCreated) _pitchElements.Dispose();
            if (_yawElements.IsCreated) _yawElements.Dispose();
            if (_bonesToPreserve.IsCreated) _bonesToPreserve.Dispose();
        }

        public void OnDrawGizmos()
        {
#if UNITY_EDITOR
            int count = _lookBones.Count;

            Vector3 size = new Vector3(0.03f, 0.03f, 0.03f);
            
            for (int i = 1; i < count; i++)
            {
                Transform from = _lookBones[i - 1];
                Transform to = _lookBones[i];
                AnimationModifierUtility.DrawPyramid(to.position, from.position, size, Color.blue);
            }
            
            Transform character = _jobData.animator.transform;
            
            Vector3 position = character.position;
            Quaternion rotation = _yawRotation;
            AnimationModifierUtility.DrawArrow(position, rotation, 0.5f, 0.03f, Color.blue);
            
            rotation *= Quaternion.Euler(0f, -_turnAngle, 0f);
            AnimationModifierUtility.DrawArrow(position, rotation, 0.5f, 0.03f, Color.red);
#endif
        }
        
        public void OnSceneGUI()
        {
        }
    }
}