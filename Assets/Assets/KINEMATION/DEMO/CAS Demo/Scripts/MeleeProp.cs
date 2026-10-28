// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using UnityEngine;

namespace CAS_Demo.Scripts
{
    [AddComponentMenu(CasNames.Path_ComponentMenu + "Melee Item")]
    public class MeleeProp : CasProp
    {
        [SerializeField] protected List<AnimationAsset> attackAnimations;
        [SerializeField] protected AnimationAsset blockAnimation;

        protected CharacterAnimationComponent _characterAnimation;

        private void Start()
        {
            _characterAnimation = GetComponentInParent<CharacterAnimationComponent>();
        }

        public override void UseItem()
        {
            if (_characterAnimation == null) return;
            _characterAnimation.PlayAnimation(attackAnimations[Random.Range(0, attackAnimations.Count)]);
        }

        public override void OnAim(bool isAiming)
        {
            if (isAiming)
            {
                _characterAnimation.PlayAnimation(blockAnimation);
                return;
            }
            
            _characterAnimation.StopAnimation(blockAnimation);
        }
    }
}