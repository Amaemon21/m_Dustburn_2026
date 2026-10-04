// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Utility;
using UnityEngine;

namespace Demo.Scripts
{
    public class LeverInteraction : MonoBehaviour, ICharacterInteraction
    {
        public Transform ikHandTarget;
        public Transform playerTarget;
        [Range(0, 180f)] public float maxAllowedAngle = 40f;
        public MotionWarpingAsset warpingAsset;
        private Animator _animator;

        private void Start()
        {
            _animator = GetComponent<Animator>();
        }

        public Transform GetLeftHandTarget()
        {
            return ikHandTarget;
        }

        public Transform GetRightHandTarget()
        {
            return null;
        }

        public void StartInteraction(GameObject player)
        {
            Vector3 targetDirection = playerTarget.forward;
            Vector3 playerDirection = player.transform.forward;

            if (Vector3.Angle(targetDirection, playerDirection) > maxAllowedAngle) return;
            
            MotionWarpingComponent warpingComponent = player.GetComponent<MotionWarpingComponent>();
            if(warpingComponent != null) warpingComponent.Play(warpingAsset, new []
            {
                new WarpPoint(playerTarget)
            });
            
            _animator.Play("Lever", -1, 0f);
        }
    }
}