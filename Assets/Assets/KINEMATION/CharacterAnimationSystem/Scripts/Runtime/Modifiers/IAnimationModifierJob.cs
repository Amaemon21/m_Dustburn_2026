// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers
{
    public interface IAnimationModifierJob
    {
        /// <summary>
        /// Sets up a job. Called on the main thread when a new profile is linked.
        /// </summary>
        /// <param name="jobData">General job data.</param>
        /// <param name="settings">Procedural animation asset.</param>
        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings);

        /// <summary>
        /// Updates current modifier settings.
        /// </summary>
        /// <param name="newSettings">New modifier settings.</param>
        public void OnModifierUpdated(AnimationModifierSettings newSettings);

        /// <summary>
        /// Creates a new AnimationScriptPlayable based on the Animation Job.
        /// </summary>
        /// <param name="graph">Main Playable Graph.</param>
        /// <returns></returns>
        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph);
        
        /// <summary>
        /// Called before the game thread update.
        /// </summary>
        public void PreUpdateJobData();

        /// <summary>
        /// Standard game thread update.
        /// </summary>
        /// <param name="playable">Playable to update.</param>
        /// <param name="weight">General feature weight.</param>
        public void UpdateJobData(AnimationScriptPlayable playable, float weight);

        /// <summary>
        /// Called after the pose is finalized.
        /// </summary>
        public void LateUpdate();

        /// <summary>
        /// Destroys this job and disposes its data.
        /// </summary>
        public void Dispose();

        /// <summary>
        /// Draws modifier's Gizmos.
        /// </summary>
        public void OnDrawGizmos();

        /// <summary>
        /// Called during scene view Gui update.
        /// </summary>
        public void OnSceneGUI();
    }
}