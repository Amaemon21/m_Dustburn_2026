// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Utility;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.MotionWarping.Runtime.Examples
{
    [AddComponentMenu(WarpingUtility.Path_MotionWarping + "Climb Component")]
    public class ClimbComponent : MonoBehaviour, IWarpPointProvider
    {
        [SerializeField] private ClimbSettings climbSettings;

        public virtual WarpInteractionResult Interact(GameObject instigator)
        {
            WarpInteractionResult result = new WarpInteractionResult()
            {
                points = null,
                asset = null,
                success = false
            };

            if (climbSettings == null)
            {
                return result;
            }

            var motionWarping = instigator.GetComponent<MotionWarpingComponent>();

            Vector3 start = transform.position;
            Vector3 end = start;

            start.y += climbSettings.minHeight + climbSettings.characterCapsuleRadius;
            end.y += climbSettings.maxHeight;

            Vector3 direction = transform.forward;
            float distance = climbSettings.maxDistance;

            bool bHit = Physics.CapsuleCast(start, end, climbSettings.characterCapsuleRadius, direction,
                out var hit, distance, climbSettings.layerMask);

            if (!bHit)
            {
                return result;
            }

            Quaternion targetRotation = Quaternion.LookRotation(-hit.normal, transform.up);

            distance = (end - start).magnitude;

            start = hit.point;
            start += (targetRotation * Vector3.forward) * climbSettings.forwardOffset;

            start.y = end.y;

            bHit = Physics.SphereCast(start, climbSettings.sphereEdgeCheckRadius, -transform.up, out hit,
                distance, climbSettings.layerMask);

            start = hit.point;

            if (!bHit)
            {
                return result;
            }

            Vector3 surfaceNormal = hit.normal;

            start += surfaceNormal * (0.02f + climbSettings.characterCapsuleRadius);
            end = start + surfaceNormal * climbSettings.characterCapsuleHeight;

            bHit = Physics.CheckCapsule(start, end, climbSettings.characterCapsuleRadius, climbSettings.layerMask);

            if (bHit)
            {
                return result;
            }

            float surfaceIncline = Mathf.Clamp(Vector3.Dot(transform.up, surfaceNormal), -1f, 1f);
            surfaceIncline = Mathf.Acos(surfaceIncline) * Mathf.Rad2Deg;

            if (surfaceIncline > climbSettings.maxSurfaceInclineAngle)
            {
                return result;
            }

            Vector3 forwardVector = targetRotation * Vector3.forward;
            targetRotation = Quaternion.LookRotation(forwardVector);
            Vector3 targetPosition = hit.point;

            result.points = new[]
            {
                new WarpPoint()
                {
                    transform = hit.transform,
                    position = hit.transform.InverseTransformPoint(targetPosition),
                    rotation = Quaternion.Inverse(hit.transform.rotation) * targetRotation
                }
            };

            float height = targetPosition.y - transform.position.y;

            result.asset = height > climbSettings.lowHeight ? climbSettings.climbHigh : climbSettings.climbLow;
            result.success = true;

#if UNITY_EDITOR
            MotionWarpingComponent.AddWarpDebugData(motionWarping, new WarpDebugData()
            {
                duration = 5f,
                onDrawGizmos = () =>
                {
                    var color = Gizmos.color;
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(targetPosition, 0.1f);
                    Handles.Label(targetPosition, "Mantle Target Point");
                    Gizmos.color = color;
                }
            });
#endif

            return result;
        }
    }
}