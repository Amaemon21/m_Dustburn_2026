// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;
using UnityEngine.Animations;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers
{
    public struct AnimationModifierLayer
    {
        public AnimationScriptPlayable playable;
        public IAnimationModifierJob job;
        public AnimationModifierSettings setting;
        public List<WeightOverride> weightOverrides;
    }

    public struct AnimationStreamBone
    {
        public int index;
        public TransformStreamHandle handle;
    }

    public struct AnimationStreamBoneTransform
    {
        public TransformStreamHandle handle;
        public KTransform transform;
    }

    public struct ModifierJobData
    {
        public TransformSceneHandle rootHandle;
        public Animator animator;
        public CharacterSkeleton skeleton;
        public Dictionary<string, AnimationStreamBone> streamBones;
        public Dictionary<string, PropertyStreamHandle> customPropHandles;
        public float weight;
    }
    
    [Serializable]
    public struct LowerBodyBones
    {
        [Tooltip("Pelvis transform.")]
        public KRigElement pelvis;

        [Tooltip("Left foot transform.")]
        public KRigElement leftFoot;

        [Tooltip("Right foot transform.")]
        public KRigElement rightFoot;

        [Tooltip("Left IK foot target.")]
        public KRigElement leftFootIkTarget;

        [Tooltip("Right IK foot target.")]
        public KRigElement rightFootIkTarget;

        public static LowerBodyBones Default => new LowerBodyBones()
        {
            pelvis = new KRigElement(-1),
            leftFoot = new KRigElement(-1),
            rightFoot = new KRigElement(-1),
            leftFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootLeft),
            rightFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootRight)
        };
    }
    
    public class AnimationModifierUtility
    {
        public static TransformStreamHandle GetHandle(in ModifierJobData jobData, KRigElement element)
        {
            TransformStreamHandle outHandle = new TransformStreamHandle();
            if (jobData.streamBones.TryGetValue(element.name, out var streamBone)) outHandle = streamBone.handle;
            return outHandle;
        }
        
        public static TransformStreamHandle GetHandle(in ModifierJobData jobData, string boneName)
        {
            TransformStreamHandle outHandle = new TransformStreamHandle();
            if (jobData.streamBones.TryGetValue(boneName, out var streamBone)) outHandle = streamBone.handle;
            return outHandle;
        }

        public static void SolveTwoBoneIK(AnimationStream stream, TransformStreamHandle tip, TransformStreamHandle mid,
            TransformStreamHandle root, KTransform target, KTransform hint, float effectorWeight, float hintWeight)
        {
            if (!tip.IsValid(stream) || !mid.IsValid(stream) || !root.IsValid(stream)) return;
            
            KTwoBoneIkData data = new KTwoBoneIkData()
            {
                tip = KAnimationMath.GetTransform(stream, tip),
                mid = KAnimationMath.GetTransform(stream, mid),
                root = KAnimationMath.GetTransform(stream, root),
                target = target,
                hint = hint,
                posWeight = effectorWeight,
                rotWeight = effectorWeight,
                hintWeight = hintWeight,
                hasValidHint = KAnimationMath.IsWeightRelevant(hintWeight),
            };
            
            KTwoBoneIK.Solve(ref data);
            
            root.SetRotation(stream, data.root.rotation);
            mid.SetRotation(stream, data.mid.rotation);
            tip.SetRotation(stream, data.tip.rotation);
        }

        public static Quaternion AlignRotationWithPlane(Quaternion rotation, Vector3 localForward, Vector3 normal)
        {
            Vector3 footForward = Vector3.ProjectOnPlane(rotation * localForward, normal);
            if (Mathf.Approximately(footForward.sqrMagnitude, 0f)) return rotation;
            footForward.Normalize();

            rotation = Quaternion.LookRotation(footForward, normal);
            rotation *= Quaternion.FromToRotation(localForward, Vector3.forward);
            return rotation;
        }

        public static Quaternion AlignRotationWithPlane(Quaternion rotation, Vector3 localForward, Vector3 localUp,
            Vector3 normal)
        {
            // 1. Project foot's forward vector onto the normal plane.
            Vector3 footForward = Vector3.ProjectOnPlane(rotation * localForward, normal);
            if (footForward.sqrMagnitude < 1e-10f)
            {
                // Degenerate case: fall back to projecting localUp and build a forward from it.
                Vector3 worldUpProjected = Vector3.ProjectOnPlane(rotation * localUp, normal);
                if (worldUpProjected.sqrMagnitude < 1e-10f) return rotation;
                footForward = Vector3.Cross(normal, worldUpProjected);
                if (footForward.sqrMagnitude < 1e-10f) return rotation;
            }

            footForward.Normalize();

            // 2. Form the aligned rotation.
            rotation = Quaternion.LookRotation(footForward, normal);

            // 3. Apply the foot orientation.
            rotation *= Quaternion.Inverse(Quaternion.LookRotation(localForward, localUp));
            return rotation;
        }
        
        public static Vector3 DetectClosestLocalAxis(Quaternion boneRotation, Vector3 worldDirection)
        {
            // Transform World Direction into Local Space
            Vector3 localDir = Quaternion.Inverse(boneRotation) * worldDirection;
            
            // Find the dominant axis component
            Vector3 axis = Vector3.forward;
            float maxDot = 0f;
            
            // Check X
            if (Mathf.Abs(localDir.x) > maxDot)
            {
                maxDot = Mathf.Abs(localDir.x);
                axis = new Vector3(Mathf.Sign(localDir.x), 0, 0);
            }
            // Check Y
            if (Mathf.Abs(localDir.y) > maxDot)
            {
                maxDot = Mathf.Abs(localDir.y);
                axis = new Vector3(0, Mathf.Sign(localDir.y), 0);
            }
            // Check Z
            if (Mathf.Abs(localDir.z) > maxDot)
            {
                axis = new Vector3(0, 0, Mathf.Sign(localDir.z));
            }

            return axis;
        }

#if UNITY_EDITOR
        public static void DrawPyramid(Vector3 point1, Vector3 point2, Vector3 size, Color color)
        {
            Vector3 direction = (point2 - point1).normalized;

            Vector3 baseCenter = point2;
            Vector3 up = Vector3.Cross(direction, Vector3.right).normalized * size.y / 2;
            Vector3 right = Vector3.Cross(direction, up).normalized * size.x / 2;

            Vector3 baseVertex1 = baseCenter + up + right;
            Vector3 baseVertex2 = baseCenter + up - right;
            Vector3 baseVertex3 = baseCenter - up - right;
            Vector3 baseVertex4 = baseCenter - up + right;

            Color originalColor = Handles.color;
            Matrix4x4 originalMatrix = Handles.matrix;

            Handles.color = color;

            Handles.DrawLine(point1, baseVertex1);
            Handles.DrawLine(point1, baseVertex2);
            Handles.DrawLine(point1, baseVertex3);
            Handles.DrawLine(point1, baseVertex4);

            Handles.DrawLine(baseVertex1, baseVertex2);
            Handles.DrawLine(baseVertex2, baseVertex3);
            Handles.DrawLine(baseVertex3, baseVertex4);
            Handles.DrawLine(baseVertex4, baseVertex1);

            Handles.color = originalColor;
            Handles.matrix = originalMatrix;
        }

        public static void DrawWireSphere(Vector3 position, float radius, Color color)
        {
            Color originalColor = Handles.color;

            Handles.color = color;

            Handles.DrawWireArc(position, Vector3.up, Vector3.forward, 360, radius);
            Handles.DrawWireArc(position, Vector3.right, Vector3.up, 360, radius);
            Handles.DrawWireArc(position, Vector3.forward, Vector3.right, 360, radius);

            Handles.color = originalColor;
        }

        public static void DrawArrow(Vector3 position, Quaternion rotation, float length, float thickness, Color color)
        {
            Color oldColor = Handles.color;
            Handles.color = color;

            Vector3 dir = rotation * Vector3.forward;

            Vector3 capScale = new Vector3(thickness, thickness, thickness);
            capScale.x = capScale.y *= 1.8f;
            capScale.z *= 1.2f;

            float shaftLength = length - capScale.z;
            Vector3 shaftCenter = position + dir * (shaftLength / 2f);

            Matrix4x4 oldMatrix = Handles.matrix;

            Handles.matrix = Matrix4x4.TRS(
                shaftCenter,
                rotation,
                new Vector3(thickness, thickness, shaftLength)
            );

            Handles.CylinderHandleCap(0, Vector3.zero, Quaternion.identity, 1f, EventType.Repaint);

            Handles.matrix = oldMatrix;

            Vector3 conePos = position + dir * (shaftLength - 0.001f + capScale.z / 2f);
            Handles.matrix = Matrix4x4.TRS(conePos, rotation, capScale);
            Handles.ConeHandleCap(0, Vector3.zero, Quaternion.identity, 1f, EventType.Repaint);

            Handles.matrix = oldMatrix;
            Handles.color = oldColor;
        }

        public static void DrawArrow(Vector3 start, Vector3 end, float thickness, Color color)
        {
            Vector3 vector = (end - start);
            Quaternion rotation = Quaternion.LookRotation(vector);
            DrawArrow(start, rotation, vector.magnitude, thickness, color);
        }

        public static void DrawCapsule(Vector3 start, Vector3 end, float radius, Color color)
        {
            Vector3 forward = end - start;
            float distance = forward.magnitude;

            // Save and set color
            Color oldColor = Handles.color;
            Handles.color = color;

            // CHECK: If the points are effectively the same, just draw a sphere
            if (distance < 0.0001f)
            {
                // Draw three orthogonal circles to make a wire sphere
                Handles.DrawWireDisc(start, Vector3.up, radius);
                Handles.DrawWireDisc(start, Vector3.forward, radius);
                Handles.DrawWireDisc(start, Vector3.right, radius);

                Handles.color = oldColor;
                return;
            }

            // Calculate Basis Vectors
            // We normalize forward to get a pure direction for rotation
            Vector3 direction = forward / distance;
            Quaternion rotation = Quaternion.LookRotation(direction);
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;

            // 1. Draw Cylinder Body (4 Lines connecting the spheres)
            Handles.DrawLine(start + (up * radius), end + (up * radius));
            Handles.DrawLine(start - (up * radius), end - (up * radius));
            Handles.DrawLine(start + (right * radius), end + (right * radius));
            Handles.DrawLine(start - (right * radius), end - (right * radius));

            // 2. Draw End Caps (The Rings at the connection points)
            Handles.DrawWireDisc(start, direction, radius);
            Handles.DrawWireDisc(end, direction, radius);

            // 3. Draw Rounded Tips (The Hemispheres)
            // Note: We flipped the angles here to ensure they point OUTWARDS.

            // START CAP (Bulges backwards, away from 'end')
            Handles.DrawWireArc(start, right, up, -180f, radius); // Vertical arc
            Handles.DrawWireArc(start, up, right, 180f, radius); // Horizontal arc

            // END CAP (Bulges forwards, away from 'start')
            Handles.DrawWireArc(end, right, up, 180f, radius); // Vertical arc
            Handles.DrawWireArc(end, up, right, -180f, radius); // Horizontal arc

            // Restore Color
            Handles.color = oldColor;
        }
#endif
    }
}