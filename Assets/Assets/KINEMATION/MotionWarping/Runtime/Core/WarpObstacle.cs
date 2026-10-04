// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.MotionWarping.Runtime.Utility;
using UnityEngine;

namespace KINEMATION.MotionWarping.Runtime.Core
{
    // Obstacle with pre-defined Warp Points
    [AddComponentMenu(WarpingUtility.Path_MotionWarping + "Warp Obstacle")]
    public class WarpObstacle : MonoBehaviour, IWarpPointProvider
    {
        [SerializeField] private MotionWarpingAsset motionWarpingAsset;
        [SerializeField] private List<Transform> points = new List<Transform>();
        [SerializeField] private bool useTransforms = false;
        [SerializeField] private bool drawDebugPoints = false;

        private void OnDrawGizmos()
        {
            if (!drawDebugPoints) return;

            var color = Gizmos.color;
            Gizmos.color = Color.green;

            foreach (var point in points)
            {
                if(point == null) continue;
                Gizmos.DrawWireSphere(point.position, 0.07f);
            }
            
            Gizmos.color = color;
        }

        public WarpInteractionResult Interact(GameObject instigator)
        {
            WarpInteractionResult result;
            result.points = new WarpPoint[points.Count];
            result.asset = motionWarpingAsset;
            result.success = points.Count > 0;
            
            for (int i = 0; i < result.points.Length; i++)
            {
                if (useTransforms)
                {
                    result.points[i] = new WarpPoint()
                    {
                        transform = points[i]
                    };
                }
                else
                {
                    result.points[i] = new WarpPoint()
                    {
                        position = points[i].position,
                        rotation = points[i].rotation
                    };
                }
            }
            
            return result;
        }
    }
}