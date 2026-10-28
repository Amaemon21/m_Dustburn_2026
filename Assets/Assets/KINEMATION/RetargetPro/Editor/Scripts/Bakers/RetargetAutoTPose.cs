// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.BoneLookups;
using KINEMATION.RetargetPro.Editor.Scripts.Presets;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    internal static class RetargetAutoTPose
    {
        private const float DirectionEpsilon = 0.00000001f;
        private static readonly RetargetChainId[] FingerChains =
        {
            RetargetChainId.IndexFingers, RetargetChainId.MiddleFingers,
            RetargetChainId.RingFingers, RetargetChainId.PinkyFingers
        };

        internal static void Apply(KRigComponent rigComponent)
        {
            if (rigComponent == null) return;

            // Map the instance hierarchy without the profile's expanded helper/twist chains.
            KRig rig = ScriptableObject.CreateInstance<KRig>();
            rig.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform[] hierarchy = rigComponent.GetRigTransforms();
                int[] depths = rigComponent.GetHierarchyDepths();
                for (int i = 0; i < hierarchy.Length; i++)
                {
                    rig.rigHierarchy.Add(new KRigElement(i, hierarchy[i].name, depths[i]));
                }

                var map = RetargetPresetUtility.BuildBestChainMap(rig, out RetargetPreset preset,
                    out bool hasPreset, character: rigComponent.gameObject);
                if (!hasPreset || preset is QuadrupedRetargetPreset) return;

                List<Transform> leftArm = GetArm(map, RetargetChainId.LeftArm, rigComponent, preset);
                List<Transform> rightArm = GetArm(map, RetargetChainId.RightArm, rigComponent, preset);
                Vector3 up = rigComponent.transform.up;
                Vector3 right = rigComponent.transform.right;
                float shoulderHeight = 0f;
                bool hasShoulders = leftArm.Count >= 3 && rightArm.Count >= 3;
                if (hasShoulders)
                {
                    Vector3 leftShoulder = leftArm[^3].position;
                    Vector3 rightShoulder = rightArm[^3].position;
                    right = Vector3.ProjectOnPlane(rightShoulder - leftShoulder, up);
                    if (right.sqrMagnitude < DirectionEpsilon) return;
                    right.Normalize();
                    shoulderHeight = Vector3.Dot((leftShoulder + rightShoulder) * 0.5f, up);
                }

                SolveArm(leftArm, map, rigComponent, -right, up, up, hasShoulders, shoulderHeight);
                SolveArm(rightArm, map, rigComponent, right, up, -up, hasShoulders, shoulderHeight);
                AlignLimb(GetChain(map, RetargetChainId.LeftLeg, rigComponent), -up, true);
                AlignLimb(GetChain(map, RetargetChainId.RightLeg, rigComponent), -up, true);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        private static List<Transform> GetChain(Dictionary<RetargetChainId, KRigElementChain> map,
            RetargetChainId id, KRigComponent rigComponent)
        {
            List<Transform> bones = new List<Transform>();
            if (!map.TryGetValue(id, out KRigElementChain chain)) return bones;
            foreach (KRigElement element in chain.elementChain)
            {
                Transform bone = rigComponent.GetRigTransform(element.index);
                if (bone != null && !bones.Contains(bone)) bones.Add(bone);
            }

            return bones;
        }

        private static bool IsUsableChain(List<Transform> chain)
        {
            for (int i = 1; i < chain.Count; i++)
            {
                if (!chain[i].IsChildOf(chain[i - 1]) ||
                    (chain[i].position - chain[i - 1].position).sqrMagnitude < DirectionEpsilon) return false;
            }

            return chain.Count > 0;
        }

        private static List<Transform> GetArm(Dictionary<RetargetChainId, KRigElementChain> map,
            RetargetChainId id, KRigComponent rigComponent, RetargetPreset preset)
        {
            List<Transform> arm = GetChain(map, id, rigComponent);
            if (arm.Count != 3) return arm;

            // Three bones are only sufficient when the optional shoulder is absent.
            Animator animator = rigComponent.GetComponentInChildren<Animator>(true);
            if (preset is UnityHumanoidRetargetPreset && animator != null && animator.isHuman)
            {
                HumanBodyBones shoulder = id == RetargetChainId.LeftArm
                    ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
                if (arm[0] == animator.GetBoneTransform(shoulder)) arm.Clear();
                return arm;
            }

            if (preset.GetChainLookups().TryGetValue(id, out RetargetBoneNameLookups lookup) && lookup.IsValid)
            {
                // Presets start arm queries with the clavicle; Unity also accepts shoulder.
                lookup.queries = preset is UnityHumanoidRetargetPreset
                    ? new[] { lookup.queries[0], lookup.queries[1] } : new[] { lookup.queries[0] };
                if (RetargetBoneLookupUtility.IsNameMatching(arm[0].name, lookup)) arm.Clear();
            }

            return arm;
        }

        private static void Align(Transform bone, Vector3 currentDirection, Vector3 targetDirection)
        {
            if (currentDirection.sqrMagnitude < DirectionEpsilon ||
                targetDirection.sqrMagnitude < DirectionEpsilon) return;
            bone.rotation = Quaternion.FromToRotation(currentDirection, targetDirection) * bone.rotation;
        }

        private static void AlignLimb(List<Transform> chain, Vector3 direction, bool preserveEndRotation)
        {
            if (chain.Count < 3 || chain.Count > 4 || (preserveEndRotation && chain.Count != 3) ||
                !IsUsableChain(chain)) return;
            Quaternion endRotation = chain[^1].rotation;
            for (int i = chain.Count - 3; i < chain.Count - 1; i++)
            {
                Align(chain[i], chain[i + 1].position - chain[i].position, direction);
            }

            if (preserveEndRotation) chain[^1].rotation = endRotation;
        }

        private static void SolveArm(List<Transform> arm, Dictionary<RetargetChainId, KRigElementChain> map,
            KRigComponent rigComponent, Vector3 direction, Vector3 up, Vector3 palmNormal,
            bool levelShoulder, float shoulderHeight)
        {
            if (arm.Count < 3 || arm.Count > 4 || !IsUsableChain(arm)) return;
            if (levelShoulder && arm.Count == 4)
            {
                Vector3 shoulderDirection = arm[1].position - arm[0].position;
                float length = shoulderDirection.magnitude;
                float height = Mathf.Clamp(shoulderHeight - Vector3.Dot(arm[0].position, up), -length, length);
                Vector3 horizontal = Vector3.ProjectOnPlane(shoulderDirection, up);
                if (horizontal.sqrMagnitude >= DirectionEpsilon)
                {
                    Vector3 leveled = horizontal.normalized * Mathf.Sqrt(Mathf.Max(0f, length * length - height * height))
                                      + up * height;
                    Align(arm[0], shoulderDirection, leveled);
                }
            }

            AlignLimb(arm, direction, false);
            Transform hand = arm[^1];
            List<List<Transform>> fingers = GetFingers(map, rigComponent, hand);
            if (fingers.Count == 0) return;

            Vector3 knuckleCenter = Vector3.zero;
            foreach (List<Transform> finger in fingers) knuckleCenter += finger[0].position;
            knuckleCenter /= fingers.Count;
            Vector3 handDirection = knuckleCenter - hand.position;
            Vector3 knuckleSpan = fingers[^1][0].position - fingers[0][0].position;
            Vector3 normal = Vector3.Cross(knuckleSpan, handDirection);
            Quaternion handRotation = hand.rotation;
            Align(hand, handDirection, direction);
            normal = (hand.rotation * Quaternion.Inverse(handRotation)) * normal;
            if (normal.sqrMagnitude >= DirectionEpsilon)
            {
                hand.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(normal, palmNormal, direction), direction)
                                * hand.rotation;
            }

            foreach (List<Transform> finger in fingers)
            {
                if (!IsUsableChain(finger)) continue;
                for (int i = 0; i < finger.Count - 1; i++)
                {
                    Align(finger[i], finger[i + 1].position - finger[i].position, direction);
                }

                // A leaf's local rotation cannot be inferred without a continuation.
                Transform tip = finger[^1];
                if (tip.childCount == 1)
                {
                    Align(tip, tip.GetChild(0).position - tip.position, direction);
                }
            }
        }

        private static List<List<Transform>> GetFingers(Dictionary<RetargetChainId, KRigElementChain> map,
            KRigComponent rigComponent, Transform hand)
        {
            List<List<Transform>> fingers = new List<List<Transform>>();
            foreach (RetargetChainId id in FingerChains)
            {
                List<Transform> bones = GetChain(map, id, rigComponent);
                bones.RemoveAll(bone => bone == hand || !bone.IsChildOf(hand));
                foreach (Transform bone in bones)
                {
                    // Preset chains can contain both hands or several Synty fingers.
                    bool hasMappedParent = false;
                    for (Transform parent = bone.parent; parent != null && parent != hand; parent = parent.parent)
                    {
                        if (!bones.Contains(parent)) continue;
                        hasMappedParent = true;
                        break;
                    }

                    if (hasMappedParent) continue;
                    List<Transform> finger = bones.FindAll(candidate => candidate == bone || candidate.IsChildOf(bone));
                    finger.Sort((a, b) => a == b ? 0 : a.IsChildOf(b) ? 1 : b.IsChildOf(a) ? -1 : 0);
                    if (IsUsableChain(finger)) fingers.Add(finger);
                }
            }

            return fingers;
        }
    }
}
