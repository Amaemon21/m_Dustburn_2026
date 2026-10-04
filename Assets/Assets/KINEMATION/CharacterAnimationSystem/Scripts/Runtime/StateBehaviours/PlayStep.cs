// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.StateBehaviours
{
    public class PlayStep : StateMachineBehaviour
    {
        [Tooltip("Step played when transition starts to this state.")]
        [SerializeField] protected StepModifierSettings stateEnterStep;
        
        [Tooltip("Step played when transition starts from this state.")]
        [SerializeField] protected StepModifierSettings stateExitStep;
        protected ProceduralAnimationComponent _proceduralAnimation;

        protected bool _isStateActive;
        
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (_proceduralAnimation == null)
            {
                _proceduralAnimation = animator.GetComponent<ProceduralAnimationComponent>();
            }
             
            if (_proceduralAnimation == null) return;
            _proceduralAnimation.UpdateAnimationModifier(stateEnterStep);
            _isStateActive = true;
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateUpdate(animator, stateInfo, layerIndex);
            
            bool isThisState = animator.GetCurrentAnimatorStateInfo(layerIndex).fullPathHash == stateInfo.fullPathHash;
            
            bool isTransition = animator.IsInTransition(layerIndex);
            
            if (isThisState && isTransition && _isStateActive)
            {
                if (_proceduralAnimation == null)
                {
                    _proceduralAnimation = animator.GetComponent<ProceduralAnimationComponent>();
                }
                
                if (_proceduralAnimation == null) return;
                _proceduralAnimation.UpdateAnimationModifier(stateExitStep);
                _isStateActive = false;
            }
        }
    }
}