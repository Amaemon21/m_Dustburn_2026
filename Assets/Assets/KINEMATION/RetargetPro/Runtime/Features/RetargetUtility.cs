// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features
{
    public class RetargetUtility
    {
        public struct FABRIK_Data
        {
            public Vector3[] positions;
            public float[] lengths;
        
            public Vector3 target;
            public Vector3 pole;
            public bool usePole;
            public float poleWeight;
            public int poleIndex;
            public float tolerance;
            public float maxReach;
            public float reachMultiplier;
            public int maxIterations;
        }
        
        private static Vector3 ProjectPointOnPlane(Vector3 point, Vector3 planeOrigin, Vector3 planeNormal)
        {
            float dot = Vector3.Dot(point - planeOrigin, planeNormal);
            return point - planeNormal * dot;
        }

        private static Vector3 ClampToReach(Vector3 root, Vector3 target, float maxReach)
        {
            if (maxReach <= 0f)
            {
                return target;
            }

            Vector3 rootToTarget = target - root;
            float distance = rootToTarget.magnitude;
            if (distance <= maxReach || distance <= KMath.FloatMin)
            {
                return target;
            }

            return root + rootToTarget / distance * maxReach;
        }

        private static void ApplyPoleConstraint(ref FABRIK_Data ikData)
        {
            if (!ikData.usePole || ikData.poleWeight <= 0f || ikData.positions.Length < 3)
            {
                return;
            }

            int tipIndex = ikData.positions.Length - 1;
            float poleWeight = Mathf.Clamp01(ikData.poleWeight);
            int jointIndex = ikData.poleIndex > 0 ? ikData.poleIndex : tipIndex - 1;
            jointIndex = Mathf.Clamp(jointIndex, 1, tipIndex - 1);

            Vector3 prev = ikData.positions[jointIndex - 1];
            Vector3 next = ikData.positions[jointIndex + 1];
            Vector3 axis = next - prev;

            if (axis.sqrMagnitude < KMath.SqrEpsilon)
            {
                return;
            }

            axis.Normalize();

            Vector3 projectedJoint = ProjectPointOnPlane(ikData.positions[jointIndex], prev, axis);
            Vector3 projectedPole = ProjectPointOnPlane(ikData.pole, prev, axis);

            Vector3 from = projectedJoint - prev;
            Vector3 to = projectedPole - prev;

            if (from.sqrMagnitude < KMath.SqrEpsilon || to.sqrMagnitude < KMath.SqrEpsilon)
            {
                return;
            }

            float angle = Vector3.SignedAngle(from, to, axis) * poleWeight;
            Quaternion rotation = Quaternion.AngleAxis(angle, axis);
            ikData.positions[jointIndex] = rotation * (ikData.positions[jointIndex] - prev) + prev;
        }

        public static bool SolveFABRIK(ref FABRIK_Data ikData)
        {
            if (ikData.positions == null || ikData.lengths == null)
            {
                return false;
            }

            int count = ikData.positions.Length;
            if (count < 2 || ikData.lengths.Length < count)
            {
                return false;
            }

            float reachMultiplier = ikData.reachMultiplier <= KMath.FloatMin ? 1f : ikData.reachMultiplier;
            Vector3 rootPosition = ikData.positions[0];
            Vector3 target = ClampToReach(rootPosition, ikData.target, ikData.maxReach * Mathf.Max(0f, reachMultiplier));

            // If the target is unreachable
            var rootToTargetDir = target - rootPosition;
            if (rootToTargetDir.sqrMagnitude > KMath.Square(ikData.maxReach))
            {
                // Line up chain towards target
                var dir = rootToTargetDir.normalized;
                for (int i = 1; i < count; ++i)
                {
                    ikData.positions[i] = ikData.positions[i - 1] + dir * ikData.lengths[i - 1];
                }

                ApplyPoleConstraint(ref ikData);

                return true;
            }

            int tipIndex = count - 1;
            float sqrTolerance = KMath.Square(ikData.tolerance);
            
            if (KMath.SqrDistance(ikData.positions[tipIndex], target) > sqrTolerance)
            {
                var rootPos = rootPosition;
                int iteration = 0;

                do
                {
                    // Forward reaching phase
                    // Set tip to target and propagate displacement to rest of chain
                    ikData.positions[tipIndex] = target;
                    for (int i = tipIndex - 1; i > -1; --i)
                    {
                        ikData.positions[i] = ikData.positions[i + 1] +
                                                   ((ikData.positions[i] - ikData.positions[i + 1]).normalized *
                                                    ikData.lengths[i]);
                    }
                    
                    // Backward reaching phase
                    // Set root back at it's original position and propagate displacement to rest of chain
                    ikData.positions[0] = rootPos;
                    for (int i = 1; i < count; ++i)
                    {
                        ikData.positions[i] = ikData.positions[i - 1] +
                                                   ((ikData.positions[i] - ikData.positions[i - 1]).normalized * ikData.lengths[i - 1]);
                    }

                    ApplyPoleConstraint(ref ikData);
                } while ((KMath.SqrDistance(ikData.positions[tipIndex], target) > sqrTolerance) &&
                         (++iteration < ikData.maxIterations));

                return true;
            }

            return false;
        }
        
        public static KTransformChain GetTransformChain(KRigComponent rigComponent, KRig rigAsset, 
            KRigElementChain chain)
        {
            if (rigComponent == null || rigAsset == null)
            {
                return null;
            }
            
            if (chain == null)
            {
                return null;
            }

            KTransformChain output = new KTransformChain();

            foreach (var element in chain.elementChain)
            {
                Transform bone = rigComponent.GetRigTransform(element.name);

                if (bone == null)
                {
                    Debug.LogWarning($"Failed to find {element.name} of {rigAsset.name} {chain.chainName}");
                    continue;
                }
                
                output.transformChain.Add(bone);
            }
            
            return output;
        }

        public static bool IsNameMatching(string from, string to)
        {
            from = from.ToLower();
            to = to.ToLower();
            
            bool isToRight = to.Contains("_r") || to.Contains(".r") || to.Contains("right") || to.Contains(" r");
            bool isToLeft = to.Contains("_l") || to.Contains(".l") || to.Contains("left") || to.Contains(" l");
            
            bool isFromRight = from.Contains("_r") || from.Contains(".r") || from.Contains("right");
            bool isFromLeft = from.Contains("_l") || from.Contains(".l") || from.Contains("left");

            bool sameSide = isToRight && isFromRight || isToLeft && isFromLeft;
            
            if (to.Contains("root"))
            {
                return from.EndsWith("root");
            }

            if (to.Contains("pelvis") || to.Contains("hip"))
            {
                return from.Contains("pelvis") || from.Contains("hip");
            }
            
            if (to.Contains("lowerleg"))
            {
                return sameSide && from.Contains("lowerleg");
            }

            if (to.Contains("upperleg") || to.Contains("shin") || to.Contains("thigh") || to.Contains("leg"))
            {
                return sameSide && (from.Contains("upperleg") || from.Contains("shin") || from.Contains("thigh") 
                       || from.Contains("leg"));
            }

            if (to.Contains("foot") || to.Contains("ball"))
            {
                return sameSide && (from.Contains("foot") || from.Contains("ball"));
            }

            if (to.Contains("spine")) return from.Contains("spine");

            if (to.Contains("neck")) return from.Contains("neck");
            if (to.Contains("head")) return from.Contains("head");

            if (to.Contains("clavicle") || to.Contains("shoulder"))
            {
                return sameSide && (from.Contains("clavicle") || from.Contains("shoulder"));
            }

            if (to.Contains("upperarm")) 
            {
                return sameSide && from.Contains("upperarm");
            }

            if (to.Contains("lowerarm") || to.Contains("forearm"))
            {
                return sameSide && (from.Contains("lowerarm") || from.Contains("forearm"));
            }

            if (to.Contains("index")) return sameSide && from.Contains("index");
            if (to.Contains("middle")) return sameSide && from.Contains("middle");
            if (to.Contains("ring"))
            {
                //Debug.Log($"{from}, {to}: {sameSide && from.Contains("ring")}");
                return sameSide && from.Contains("ring");
            }
            if (to.Contains("pinky")) return sameSide && from.Contains("pinky");
            if (to.Contains("thumb")) return sameSide && from.Contains("thumb");
            if (to.Contains("finger")) return sameSide && from.Contains("finger");

            if (to.Contains("hand") || to.Contains("palm"))
            {
                return sameSide && (from.Contains("hand") || from.Contains("palm"));
            }

            if (to.Contains("tail")) return from.Contains("tail");
            return false;
        }
    }
}