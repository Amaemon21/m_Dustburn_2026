// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct StrideModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private ModifierJobData _jobData;
        private FootWarpingModifierJob _footWarpingJob;
        private FootPinningModifierJob _footPinningJob;

        public void ProcessAnimation(AnimationStream stream)
        {
            _footWarpingJob.ProcessAnimation(stream);
            _footPinningJob.ProcessAnimation(stream);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _footWarpingJob = default;
            _footPinningJob = default;

            _footWarpingJob.Initialize(jobData, settings);
            _footPinningJob.Initialize(jobData, settings);
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
            _footWarpingJob.PreUpdateJobData();
            _footPinningJob.PreUpdateJobData();
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            var job = playable.GetJobData<StrideModifierJob>();

            _footWarpingJob.UpdateJobData(job._footWarpingJob, weight);
            _footPinningJob.UpdateJobData(job._footPinningJob, weight);

            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
            _footWarpingJob.LateUpdate();
            _footPinningJob.LateUpdate();
        }

        public void Dispose()
        {
            _footWarpingJob.Dispose();
            _footPinningJob.Dispose();
        }

        public void OnDrawGizmos()
        {
            _footWarpingJob.OnDrawGizmos();
            _footPinningJob.OnDrawGizmos();
        }

        public void OnSceneGUI()
        {
            _footWarpingJob.OnSceneGUI();
            _footPinningJob.OnSceneGUI();
        }
    }
}