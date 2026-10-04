// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [Serializable]
    public struct CharacterSkeletonBone
    {
        public Transform transform;
        [HideInInspector] public KRigElement rigElement;
        [HideInInspector] public KTransform meshPose;
    }

    [HelpURL("https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/character-skeleton")]
    [ExecuteInEditMode, AddComponentMenu("KINEMATION/CAS/Character Skeleton")]
    public class CharacterSkeleton : MonoBehaviour, IRigProvider
    {
        public List<CharacterSkeletonBone> SkeletonBones => skeleton;
        public List<KRigElementChain> ElementChains => skeletonBoneChains;

        [Unfold]
        [SerializeField] private List<CharacterSkeletonBone> skeleton = new List<CharacterSkeletonBone>();

        [CustomElementChainDrawer(false, true)]
        [SerializeField] private List<KRigElementChain> skeletonBoneChains = new List<KRigElementChain>();
        private Dictionary<string, CharacterSkeletonBone> _skeletonMap;
        private List<KTransform> _cachedSkeletonPose = new List<KTransform>();

        private HashSet<string> _boneNames;

        public void UpdateSkeleton()
        {
            _skeletonMap = null;
            skeleton.Clear();
            skeletonBoneChains.Clear();
            GenerateSkeletonAndChains(transform, 0, null);
        }

        public KRigElement[] GetRigElements()
        {
            return skeleton.Select(bone => bone.rigElement).ToArray();
        }

        public KRigElement GetRigElement(Transform bone)
        {
            var skeletonBone = skeleton.Find(skeletonBone => skeletonBone.rigElement.name.Equals(bone.name));
            return skeletonBone.rigElement;
        }

        public KRigElement GetRigElement(string boneName)
        {
            var skeletonBone = skeleton.Find(skeletonBone => skeletonBone.rigElement.name.Equals(boneName));
            return skeletonBone.rigElement;
        }

        public KRigElement GetRigElement(int index)
        {
            if (index < 0 || index > skeleton.Count - 1) return new KRigElement(-1);
            return skeleton[index].rigElement;
        }

        public CharacterSkeletonBone GetSkeletonBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return new CharacterSkeletonBone();
            InitializeSkeletonMap();
            _skeletonMap.TryGetValue(boneName, out var output);
            return output;
        }

        public CharacterSkeletonBone GetSkeletonBone(KRigElement element)
        {
            if (string.IsNullOrEmpty(element.name)) return new CharacterSkeletonBone();
            InitializeSkeletonMap();
            _skeletonMap.TryGetValue(element.name, out var output);
            return output;
        }

        public Transform[] GetTransformHierarchy()
        {
            return skeleton.Select(bone => bone.transform).ToArray();
        }

        public Transform GetBoneTransform(string boneName)
        {
            Transform boneTransform = null;

            if (_skeletonMap == null)
            {
                boneTransform = skeleton.Find(skeletonBone => skeletonBone.rigElement.name.Equals(boneName)).transform;
            }
            else
            {
                _skeletonMap.TryGetValue(boneName, out var skeletonBone);
                boneTransform = skeletonBone.transform;
            }

            return boneTransform;
        }

        public Transform GetBoneTransform(KRigElement element)
        {
            int index = element.index;

            // If index out of range, try getting a bone by name.
            if (index < 0 || index > skeleton.Count - 1)
            {
                return GetBoneTransform(element.name);
            }

            return skeleton[index].transform;
        }

        public void CacheSkeletonPose()
        {
            _cachedSkeletonPose.Clear();
            foreach (var bone in skeleton)
            {
                _cachedSkeletonPose.Add(new KTransform(bone.transform, false));
            }
        }

        public void RestoreCachedSkeletonPose()
        {
            int count = _cachedSkeletonPose.Count;
            for (int i = 0; i < count; i++)
            {
                var boneTransform = skeleton[i].transform;
                boneTransform.localPosition = _cachedSkeletonPose[i].position;
                boneTransform.localRotation = _cachedSkeletonPose[i].rotation;
            }
        }

        private void GenerateSkeletonAndChains(Transform currentTransform, int depth, KRigElementChain currentChain)
        {
            if (currentTransform.parent != null
                && currentTransform.parent.GetComponent<CasBoneSocket>() != null)
            {
                return;
            }

            int activeIndex = skeleton.Count;
            KRigElement newElement = new KRigElement(activeIndex, currentTransform.name, depth);

            skeleton.Add(new CharacterSkeletonBone()
            {
                transform = currentTransform,
                rigElement = newElement,
                meshPose = KTransform.Identity
            });

            if (currentChain == null)
            {
                currentChain = new KRigElementChain()
                {
                    chainName = currentTransform.name,
                    elementChain = new List<KRigElement>()
                };
                skeletonBoneChains.Add(currentChain);
            }

            currentChain.elementChain.Add(newElement);

            if (currentTransform.childCount == 1)
            {
                GenerateSkeletonAndChains(currentTransform.GetChild(0), depth + 1, currentChain);
            }
            else
            {
                foreach (Transform child in currentTransform)
                {
                    GenerateSkeletonAndChains(child, depth + 1, null);
                }
            }
        }

        private void Awake()
        {
            if (!Application.isPlaying) return;
            InitializeSkeletonMap();
        }

        private void InitializeSkeletonMap()
        {
            if (_skeletonMap != null) return;

            _skeletonMap = new Dictionary<string, CharacterSkeletonBone>();
            KTransform root = new KTransform(transform.parent == null ? transform : transform.parent);
            foreach (var bone in skeleton)
            {
                _skeletonMap.TryAdd(bone.rigElement.name, new CharacterSkeletonBone()
                {
                    transform = bone.transform,
                    rigElement = bone.rigElement,
                    meshPose = root.GetRelativeTransform(new KTransform(bone.transform), false)
                });
            }
        }

        public KRigElement[] GetHierarchy()
        {
            return GetRigElements();
        }
    }
}
