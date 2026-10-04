// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct CopyBoneJob : IAnimationJob, IAnimationModifierJob
    {
        private CopyBoneSettings _modifierSettings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _copyFrom;
        private TransformStreamHandle _copyTo;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight)) return;
            if (!_jobData.rootHandle.IsValid(stream) || !_copyFrom.IsValid(stream) || !_copyTo.IsValid(stream))
            {
                return;
            }
            
            KTransform rootBone = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            KTransform fromWorld = KAnimationMath.GetTransform(stream, _copyFrom);
            KTransform fromComponent = rootBone.GetRelativeTransform(fromWorld, false);
            KTransform bonePose = KAnimationMath.GetTransform(stream, _copyFrom, false);
            
            if (_modifierSettings.copySpace == ESpaceType.ComponentSpace)
            {
                bonePose = fromComponent;
            }
            else if (_modifierSettings.copySpace == ESpaceType.WorldSpace)
            {
                bonePose = fromWorld;
            }

            if (_modifierSettings.copyPosition)
            {
                KAnimationMath.ModifyPosition(stream, _jobData.rootHandle, _copyTo, bonePose.position, 
                    _modifierSettings.copySpace, _modifierSettings.copyMode, _jobData.weight);
            }
            
            if (_modifierSettings.copyRotation)
            {
                KAnimationMath.ModifyRotation(stream, _jobData.rootHandle, _copyTo, bonePose.rotation, 
                    _modifierSettings.copySpace, _modifierSettings.copyMode, _jobData.weight);
            }

            //todo: modify scale here.
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
        
        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _modifierSettings = (CopyBoneSettings) settings;
            
            _copyFrom = AnimationModifierUtility.GetHandle(in _jobData, _modifierSettings.copyFrom);
            _copyTo = AnimationModifierUtility.GetHandle(in _jobData, _modifierSettings.copyTo);
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
            _jobData.weight = weight;
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
        }
    }
}