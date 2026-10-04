// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [AddComponentMenu("KINEMATION/CAS/Aim At")]
    public class AimAtComponent : MonoBehaviour
    {
        public Transform aimAtTransform;
        [HideInInspector] public Vector3 aimAtPosition;
        public bool enableAimAt;

        public Vector3 GetAimAtTarget()
        {
            return aimAtTransform == null ? aimAtPosition : aimAtTransform.position;
        }
    }
}