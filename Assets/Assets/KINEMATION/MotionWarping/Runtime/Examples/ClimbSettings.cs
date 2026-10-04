// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Utility;
using UnityEngine;

namespace KINEMATION.MotionWarping.Runtime.Examples
{
    [CreateAssetMenu(menuName = WarpingUtility.Path_MotionWarping + "Climb Settings", fileName = "NewClimbSettings", 
        order = 2)]
    public class ClimbSettings : ScriptableObject
    {
        public MotionWarpingAsset climbHigh;
        public MotionWarpingAsset climbLow;
        
        public LayerMask layerMask;

        [Min(0f)] public float maxHeight;
        [Min(0f)] public float lowHeight;
        [Min(0f)] public float minHeight;
        [Min(0f)] public float maxDistance;
        
        [Min(0f)] public float characterCapsuleRadius;
        [Min(0f)] public float characterCapsuleHeight;
        [Min(0f)] public float sphereEdgeCheckRadius;

        [Range(0f, 90f)] public float maxSurfaceInclineAngle;
        
        [Min(0f)] public float forwardOffset;
    }
}