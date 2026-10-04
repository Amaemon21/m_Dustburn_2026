// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.StateBehaviours
{
    public class PlayAnimation : StateMachineBehaviour
    {
        [Tooltip("Animation to play.")]
        [SerializeField] protected AnimationAsset animation;
        protected CharacterAnimationComponent _characterAnimation;
        
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (_characterAnimation == null)
            {
                _characterAnimation = animator.GetComponent<CharacterAnimationComponent>();
            }

            if (_characterAnimation == null) return;
            _characterAnimation.PlayAnimation(animation);
        }
    }
}