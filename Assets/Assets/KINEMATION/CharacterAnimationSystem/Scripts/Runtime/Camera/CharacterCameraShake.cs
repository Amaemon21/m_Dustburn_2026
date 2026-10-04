// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera
{
    [CreateAssetMenu(fileName = "NewCameraShake", menuName = "KINEMATION/CAS/Camera Shake")]
    public class CharacterCameraShake : ScriptableObject
    {
        [Unfold] public VectorCurve shakeCurve = VectorCurve.Constant(0f, 1f, 0f);
        public Vector4 pitch = Vector4.one;
        public Vector4 yaw = Vector4.one;
        public Vector4 roll = Vector4.one;
        
        [Min(0f)] public float playRate = 1f;
        [Min(0f)] public float smoothSpeed;

        public static float GetTarget(Vector4 value)
        {
            float a = Random.Range(value.x, value.y);
            float b = Random.Range(value.z, value.w);
            return Random.Range(0, 2) == 0 ? a : b;
        }
    }
}