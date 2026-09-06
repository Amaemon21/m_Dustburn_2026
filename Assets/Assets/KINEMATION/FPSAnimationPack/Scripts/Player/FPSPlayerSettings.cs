// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace KINEMATION.FPSAnimationPack.Scripts.Player
{
    [CreateAssetMenu(fileName = "NewPlayerSettings", menuName = "KINEMATION/FPS Animation Pack/FPS Player Settings")]
    public class FPSPlayerSettings : ScriptableObject
    {
        [Header("Controls")]
        [Min(0f)] public float sensitivity = 1f;
        
        public List<GameObject> weaponPrefabs;
        public float grenadeDelay = 0f;
        public float gaitSmoothing = 0f;
        
        public float aimSpeed = 0f;

        public IKMotion aimingMotion;
        public IKMotion fireModeMotion;

        public List<AudioClip> generalSounds;
    }
}