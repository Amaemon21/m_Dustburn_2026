// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Mapping
{
    internal static class ModelRootRotationUtility
    {
        private struct ChildWorldTransform
        {
            public Transform child;
            public Vector3 position;
            public Quaternion rotation;
        }

        public static void ResetRootRotationPreserveFirstChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            if (Quaternion.Angle(root.rotation, Quaternion.identity) <= 0.0001f)
            {
                return;
            }

            int childCount = root.childCount;
            if (childCount == 0)
            {
                root.rotation = Quaternion.identity;
                return;
            }

            ChildWorldTransform[] cachedChildren = new ChildWorldTransform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                cachedChildren[i] = new ChildWorldTransform
                {
                    child = child,
                    position = child.position,
                    rotation = child.rotation
                };
            }

            root.rotation = Quaternion.identity;

            for (int i = 0; i < cachedChildren.Length; i++)
            {
                ChildWorldTransform cachedChild = cachedChildren[i];
                if (cachedChild.child == null)
                {
                    continue;
                }

                cachedChild.child.SetPositionAndRotation(cachedChild.position, cachedChild.rotation);
            }
        }
    }
}