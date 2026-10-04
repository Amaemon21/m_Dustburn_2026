// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct ModifyBoneChainJob : IAnimationJob, IAnimationModifierJob
    {
        private ModifyBoneChainSettings _settings;
        private ModifierJobData _jobData;
        
        private KPose _pose;
        private NativeArray<TransformStreamHandle> _bones;
        private BindableProperty<Vector3> _positionProp;
        private BindableProperty<Quaternion> _rotationProp;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (Mathf.Approximately(_jobData.weight, 0f)) return;

            foreach (var bone in _bones)
            {
                KAnimationMath.ModifyTransform(stream, _jobData.rootHandle, bone, _pose, _jobData.weight);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (ModifyBoneChainSettings) settings;

            _bones = new NativeArray<TransformStreamHandle>(_settings.chainToModify.Count, Allocator.Persistent);
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i] = AnimationModifierUtility.GetHandle(_jobData, _settings.chainToModify.elementChain[i]);
            }

            _pose = new KPose() { pose = KTransform.Identity };
            _positionProp = _settings.position.CreateProperty(_jobData.animator.gameObject);
            _rotationProp = _settings.rotation.CreateProperty(_jobData.animator.gameObject);
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
            
            _pose.pose.position = _positionProp.GetValue();
            _pose.pose.rotation = _rotationProp.GetValue();
            _pose.space = _settings.space;
            _pose.modifyMode = _settings.mode;
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            if (_bones.IsCreated) _bones.Dispose();
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}