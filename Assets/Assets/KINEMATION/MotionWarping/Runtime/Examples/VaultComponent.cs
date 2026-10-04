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
    [AddComponentMenu(WarpingUtility.Path_MotionWarping + "Vault Component")]
    public class VaultComponent : MonoBehaviour, IWarpPointProvider
    {
        [SerializeField] private VaultSettings vaultSettings;

        private bool FindCloseEdge(out Quaternion targetRotation, out Vector3 closeEdge)
        {
            targetRotation = Quaternion.identity;
            closeEdge = Vector3.zero;
            
            if (vaultSettings == null)
            {
                return false;
            }
            
            Vector3 start = transform.position;
            Vector3 end = start + transform.up * vaultSettings.maxAllowedStartHeight;
            start.y += vaultSettings.characterCapsuleRadius + vaultSettings.minAllowedStartHeight;
            
            Vector3 direction = transform.forward;

            bool bHit = Physics.CapsuleCast(start, end, vaultSettings.characterCapsuleRadius, direction,
                out var hit, vaultSettings.maxAllowedStartLength, vaultSettings.layerMask);

            if (!bHit)
            {
                return false;
            }
            
            targetRotation = Quaternion.LookRotation(-hit.normal, transform.up);

            start = hit.point;
            start.y = end.y;
            direction = -transform.up;

            bHit = Physics.SphereCast(start, vaultSettings.sphereEdgeCheckRadius, direction, out hit,
                vaultSettings.maxAllowedStartHeight, vaultSettings.layerMask);

            if (!bHit)
            {
                return false;
            }

            closeEdge = hit.point;
            
            return true;
        }

        private bool FindEndPoint(in Vector3 farEdge, in Quaternion targetRotation, out Vector3 endPoint)
        {
            endPoint = Vector3.zero;
            
            Vector3 start = farEdge + (targetRotation * Vector3.forward) * vaultSettings.farEdgeOffset;
            Vector3 direction = -transform.up;
            float distance = vaultSettings.maxAllowedEndHeight;

            bool bHit = Physics.SphereCast(start, vaultSettings.sphereEdgeCheckRadius, direction, out var hit,
                distance, vaultSettings.layerMask);

            if (!bHit || (hit.point - start).magnitude < vaultSettings.minAllowedEndHeight)
            {
                return false;
            }

            endPoint = hit.point;
            return true;
        }

        private bool FindEndEdge(in Quaternion targetRotation, in Vector3 closeEdge, out Vector3 farEdge)
        {
            farEdge = Vector3.zero;
            
            Vector3 forward = (targetRotation * Vector3.forward).normalized;
            
            float length = vaultSettings.maxObstacleLength + vaultSettings.characterCapsuleRadius;
            Vector3 start = closeEdge + forward * length;
            Vector3 end = start;
            
            start.y = closeEdge.y - vaultSettings.closeEdgeDeviation;
            end.y = closeEdge.y + vaultSettings.closeEdgeDeviation;

            length -= vaultSettings.minObstacleLength;

            bool bHit = Physics.CapsuleCast(start, end, vaultSettings.characterCapsuleRadius,
                -forward, out var hit, length, vaultSettings.layerMask);

            if (!bHit) return false;

            start = hit.point;
            start.y = closeEdge.y + vaultSettings.closeEdgeDeviation + vaultSettings.sphereEdgeCheckRadius;

            bHit = Physics.SphereCast(start, vaultSettings.sphereEdgeCheckRadius, -transform.up, out hit,
                vaultSettings.closeEdgeDeviation * 2f, vaultSettings.layerMask);

            if (!bHit) return false;

            farEdge = hit.point;
            return true;
        }

        public virtual WarpInteractionResult Interact(GameObject instigator)
        {
            WarpInteractionResult result = new WarpInteractionResult()
            {
                points = null,
                asset = null,
                success = false
            };

            if (vaultSettings.vaultWarpingAsset == null)
            {
                return result;
            }

            var motionWarping = instigator.GetComponent<MotionWarpingComponent>();
            
            Quaternion targetRotation;
            Vector3 closeEdge, farEdge = Vector3.zero, endPoint = Vector3.zero;

            bool success = FindCloseEdge(out targetRotation, out closeEdge)
                           && FindEndEdge(targetRotation, closeEdge, out farEdge)
                           && FindEndPoint(farEdge, targetRotation, out endPoint);

            if (!success)
            {
                return result;
            }

            result.asset = vaultSettings.vaultWarpingAsset;

            result.points = new WarpPoint[]
            {
                new WarpPoint()
                {
                    position = closeEdge,
                    rotation = targetRotation
                },
                new WarpPoint()
                {
                    position = farEdge,
                    rotation = targetRotation
                },
                new WarpPoint()
                {
                    position = endPoint,
                    rotation = targetRotation
                }
            };

            result.success = true;
            
#if UNITY_EDITOR
            Core.MotionWarpingComponent.AddWarpDebugData(motionWarping, new WarpDebugData()
            {
                duration = 5f,
                onDrawGizmos = () =>
                {
                    var color = Gizmos.color;

                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(closeEdge, 0.1f);
                    Handles.Label(closeEdge, "Close Edge");
                    
                    Gizmos.DrawWireSphere(farEdge, 0.1f);
                    Handles.Label(farEdge, "Far Edge");
                    
                    Gizmos.DrawWireSphere(endPoint, 0.1f);
                    Handles.Label(endPoint, "End Point");

                    Gizmos.color = color;
                }
            });
#endif
            
            return result;
        }
    }
}