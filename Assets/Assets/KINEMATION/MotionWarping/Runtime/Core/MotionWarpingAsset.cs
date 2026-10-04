// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.MotionWarping.Runtime.Utility;
using UnityEngine;

namespace KINEMATION.MotionWarping.Runtime.Core
{
    [CreateAssetMenu(menuName = WarpingUtility.Path_MotionWarping + "Motion Warping Asset", 
        fileName = "NewWarpingAsset", order = 0)]
    public class MotionWarpingAsset : ScriptableObject
    {
        [Header("Animation")]
        
        [Tooltip("Animation to play.")] public AnimationClip animation;
        [Tooltip("The name of the target Animator state.")] public string animatorStateName = string.Empty;
        [Tooltip("Blend in time in seconds.")] [Min(0f)] public float blendTime = 0.25f;
        
        [Tooltip("Root motion for X axis.")]
        public AnimationCurve rootX = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Root motion for Y axis.")]
        public AnimationCurve rootY = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Root motion for Z axis.")]
        public AnimationCurve rootZ = AnimationCurve.Constant(0f, 1f, 0f);
        
        [Header("Warping Settings")]

        [Tooltip("What axes will use linear interpolation instead of warping.")]
        public VectorBool useLinear = VectorBool.Enabled;
        [Tooltip("What axes will apply animation on top of warping.")]
        public VectorBool useAnimation = VectorBool.Enabled;
        [Tooltip("What axes will use motion warping.")]
        public VectorBool useWarping = VectorBool.Enabled;

        [Tooltip("Whether to apply warping with physics enabled for CharacterController or Rigidbody.")]
        public bool useCollision = false;
        
        [Header("Warp Phases")]
        
        [Tooltip("Global play rate multiplier")]
        [Min(0f)] public float playRateBasis = 1f;
        
        [Min(1)] public int phasesNum = 1;
        public List<WarpPhase> warpPhases = new List<WarpPhase>();
        
        public Vector3 GetVectorValue(float time)
        {
            if (rootX == null || rootY == null || rootZ == null)
            {
                Debug.LogError(name + ": null reference curve!");
                return Vector3.zero;
            }

            return new Vector3(rootX.Evaluate(time), rootY.Evaluate(time), rootZ.Evaluate(time));
        }

        public float GetLength()
        {
            return animation == null ? 0f : animation.length;
        }
    }
}