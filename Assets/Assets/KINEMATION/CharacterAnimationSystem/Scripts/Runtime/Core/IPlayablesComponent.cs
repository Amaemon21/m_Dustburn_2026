// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    public interface IPlayablesComponent
    {
        public void InitializePlayableComponent(Animator animator, CharacterSkeleton skeleton);
        public AnimationPlayableOutput GetOutput();
        public void UpdatePlayableComponent();
        public void LateUpdatePlayableComponent();
        public void BuildPlayables();
    }
}