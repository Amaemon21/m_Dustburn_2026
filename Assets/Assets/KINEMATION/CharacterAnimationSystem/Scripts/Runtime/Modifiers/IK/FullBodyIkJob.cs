// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct FullBodyIkJob : IAnimationJob, IAnimationModifierJob
    {
        private FullBodyIkSettings _settings;
        private ModifierJobData _jobData;

        private LimbIKSettings _rightHandIk;
        private LimbIKSettings _leftHandIk;
        private LimbIKSettings _rightFootIk;
        private LimbIKSettings _leftFootIk;

        private static void SyncLimbSettings(ref LimbIKSettings runtimeSettings, in LimbIKSettings sourceSettings)
        {
            runtimeSettings.weight = sourceSettings.weight;
            runtimeSettings.poleWeight = sourceSettings.poleWeight;
            runtimeSettings.poleVector = sourceSettings.poleVector;
        }

        private void SolveTwoBoneIK(AnimationStream stream, in LimbIKSettings ikSettings)
        {
            float effectorWeight = ikSettings.weight * _jobData.weight;
            if (!KAnimationMath.IsWeightRelevant(effectorWeight)) return;
            if (!ikSettings.tipHandle.IsValid(stream) || !ikSettings.midHandle.IsValid(stream) ||
                !ikSettings.rootHandle.IsValid(stream) || !ikSettings.targetHandle.IsValid(stream))
            {
                return;
            }

            KTwoBoneIkData ikData = new KTwoBoneIkData()
            {
                tip = KAnimationMath.GetTransform(stream, ikSettings.tipHandle),
                mid = KAnimationMath.GetTransform(stream, ikSettings.midHandle),
                root = KAnimationMath.GetTransform(stream, ikSettings.rootHandle),
                target = KAnimationMath.GetTransform(stream, ikSettings.targetHandle),
                hintWeight = ikSettings.poleWeight * _jobData.weight,
                posWeight = effectorWeight,
                rotWeight = effectorWeight
            };

            if (ikSettings.poleHandle.IsValid(stream))
            {
                ikData.hasValidHint = true;
                ikData.hint = KAnimationMath.GetTransform(stream, ikSettings.poleHandle);
            }
            else if (KAnimationMath.IsWeightRelevant(ikData.hintWeight))
            {
                ikData.hasValidHint = true;

                Vector3 poleVector = ikSettings.poleVector.sqrMagnitude > Mathf.Epsilon
                    ? ikSettings.poleVector
                    : Vector3.forward;
                Vector3 hintDirection = Quaternion.LookRotation(ikSettings.localJointForward,
                    ikSettings.localJointUp) * poleVector;
                ikData.hint.position = ikData.mid.position + ikData.mid.rotation * hintDirection;
            }

            KTwoBoneIK.Solve(ref ikData);

            ikSettings.rootHandle.SetRotation(stream, ikData.root.rotation);
            ikSettings.midHandle.SetRotation(stream, ikData.mid.rotation);
            ikSettings.tipHandle.SetRotation(stream, ikData.tip.rotation);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight)) return;

            SolveTwoBoneIK(stream, _rightHandIk);
            SolveTwoBoneIK(stream, _leftHandIk);
            SolveTwoBoneIK(stream, _rightFootIk);
            SolveTwoBoneIK(stream, _leftFootIk);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = settings as FullBodyIkSettings;
            
            _rightHandIk = _settings.rightHandIk;
            _leftHandIk = _settings.leftHandIk;
            _rightFootIk = _settings.rightFootIk;
            _leftFootIk = _settings.leftFootIk;
            
            _rightHandIk.Initialize(jobData);
            _leftHandIk.Initialize(jobData);
            _rightFootIk.Initialize(jobData);
            _leftFootIk.Initialize(jobData);
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

            if (_settings != null)
            {
                SyncLimbSettings(ref _rightHandIk, _settings.rightHandIk);
                SyncLimbSettings(ref _leftHandIk, _settings.leftHandIk);
                SyncLimbSettings(ref _rightFootIk, _settings.rightFootIk);
                SyncLimbSettings(ref _leftFootIk, _settings.leftFootIk);
            }

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