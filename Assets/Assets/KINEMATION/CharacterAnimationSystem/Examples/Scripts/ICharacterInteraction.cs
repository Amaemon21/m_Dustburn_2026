// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Examples.Scripts
{
    public interface ICharacterInteraction
    {
        public Transform GetLeftHandTarget();
        public Transform GetRightHandTarget();
        public void StartInteraction(GameObject player);
    }
}