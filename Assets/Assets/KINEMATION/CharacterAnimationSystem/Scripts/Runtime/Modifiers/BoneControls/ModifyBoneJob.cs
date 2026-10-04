// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct ModifyBoneJob : IAnimationJob, IAnimationModifierJob
    {
        private ModifyBoneSettings _modifierSettings;
        private ModifierJobData _jobData;
        
        private TransformStreamHandle _boneHandle;
        private KTransform _transform;

        private BindableProperty<Vector3> _position;
        private BindableProperty<Quaternion> _rotation;
        private BindableProperty<Vector3> _scale;

#if UNITY_EDITOR
        private KTransform _boneTransform;
        private Transform _boneReference;
        private AnimationScriptPlayable _ownerPlayable;
        private Quaternion _initialOffset;
        private bool _isDragging;
#endif
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight) || !_boneHandle.IsValid(stream)) return;
            
            KAnimationMath.ModifyPosition(stream, _jobData.rootHandle, _boneHandle, _transform.position, 
                _modifierSettings.positionSpace, _modifierSettings.positionMode, _jobData.weight);
            
            KAnimationMath.ModifyRotation(stream, _jobData.rootHandle, _boneHandle, _transform.rotation, 
                _modifierSettings.rotationSpace, _modifierSettings.rotationMode, _jobData.weight);
            
            if (_modifierSettings.scaleMode != EModifyMode.Ignore)
            {
                Vector3 localScale = _boneHandle.GetLocalScale(stream);
                Vector3 targetScale = _modifierSettings.scaleMode == EModifyMode.Add
                    ? localScale + _transform.scale
                    : _transform.scale;

                targetScale = Vector3.Lerp(localScale, targetScale, _jobData.weight);
                _boneHandle.SetLocalScale(stream, targetScale);
            }
            
#if UNITY_EDITOR
            _boneTransform = KAnimationMath.GetTransform(stream, _boneHandle);
#endif
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
        
        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _modifierSettings = (ModifyBoneSettings) settings;
            
            _boneHandle = AnimationModifierUtility.GetHandle(in _jobData, _modifierSettings.boneToModify);
            
            _transform = KTransform.Identity;

            _position = _modifierSettings.position.CreateProperty(_jobData.animator.gameObject);
            _rotation = _modifierSettings.rotation.CreateProperty(_jobData.animator.gameObject);
            _scale = _modifierSettings.scale.CreateProperty(_jobData.animator.gameObject);
            
#if UNITY_EDITOR
            _boneReference = jobData.skeleton.GetBoneTransform(_modifierSettings.boneToModify.name);
#endif
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            Initialize(_jobData, newSettings);
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            var playable = AnimationScriptPlayable.Create(graph, this);
            
#if UNITY_EDITOR
            _ownerPlayable = playable;
#endif
            
            return playable;
        }

        public void PreUpdateJobData()
        {
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            _jobData.weight = weight;
            
            _transform.position = _position.GetValue();
            _transform.rotation = _rotation.GetValue();
            _transform.scale = _scale.GetValue();
            
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
        }
        
        public void OnSceneGUI()
        {
#if UNITY_EDITOR
            if (!_modifierSettings.position.IsBound && _modifierSettings.positionMode != EModifyMode.Ignore
                && Tools.current == Tool.Move)
            {
                Quaternion rotationSpace = Quaternion.identity;
                ESpaceType space = _modifierSettings.positionSpace;
                
                if (space == ESpaceType.ComponentSpace)
                {
                    rotationSpace = _jobData.animator.transform.rotation;
                }
                else if (space == ESpaceType.ParentBoneSpace || space == ESpaceType.BoneSpace &&
                         _modifierSettings.positionMode == EModifyMode.Replace)
                {
                    rotationSpace = _boneReference.parent.rotation;
                }
                else if (space == ESpaceType.BoneSpace)
                {
                    rotationSpace = _boneReference.rotation;
                }
                
                Vector3 position = Handles.PositionHandle(_boneReference.position, rotationSpace);
                if (!position.Equals(_boneReference.position))
                {
                    Undo.RecordObject(_modifierSettings, "Modify Bone Position");
                    
                    position = Quaternion.Inverse(rotationSpace) * (position - _boneReference.position);
                    position += _modifierSettings.position.GetValue();
                    _modifierSettings.position.SetDefaultValue(position);
                }

                return;
            }
            
            if (!_modifierSettings.rotation.IsBound && _modifierSettings.rotationMode != EModifyMode.Ignore
                                                    && Tools.current == Tool.Rotate)
            {
                ESpaceType space = _modifierSettings.rotationSpace;
                _boneTransform = _ownerPlayable.GetJobData<ModifyBoneJob>()._boneTransform;

                Quaternion rotationSpace = Quaternion.identity;
                Quaternion offset = _modifierSettings.rotation.GetValue();

                if (space == ESpaceType.ComponentSpace)
                {
                    rotationSpace = _jobData.animator.transform.rotation;
                }
                else if (space == ESpaceType.ParentBoneSpace)
                {
                    rotationSpace = _boneReference.parent.rotation;
                }
                else if (space == ESpaceType.BoneSpace)
                {
                    rotationSpace = _boneReference.rotation;
                }
                
                EditorGUI.BeginChangeCheck();
           
                Quaternion handleRotation = Handles.RotationHandle(rotationSpace, _boneReference.position);
                
                if (_isDragging && GUIUtility.hotControl == 0) _isDragging = false;
                
                if (!EditorGUI.EndChangeCheck()) return;
                
                if (!_isDragging)
                {
                    _isDragging = true;
                    _initialOffset = offset;
                }

                if (space == ESpaceType.BoneSpace)
                {
                    offset = Quaternion.Inverse(rotationSpace * Quaternion.Inverse(offset)) * handleRotation;
                }
                else
                {
                    Quaternion delta = Quaternion.Inverse(rotationSpace) * handleRotation;
                    offset = KAnimationMath.RotateInSpace(rotationSpace, _initialOffset, delta, 1f);
                }

                offset.Normalize();

                Undo.RecordObject(_modifierSettings, "Modify Bone Rotation");
                _modifierSettings.rotation.SetDefaultValue(offset);
            }
#endif
        }
    }
}