// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace KINEMATION.FPSAnimationPack.Scripts.Player
{
    [ExecuteInEditMode]
    [AddComponentMenu("KINEMATION/FPS Animation Pack/Character/FPS Procedural Animation")]
    public class FPSProceduralAnimation : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float ikWeight = 1f;
        
        [Header("Skeleton")]
        [SerializeField] private Transform skeletonRoot;
        [SerializeField] private Transform weaponBone;
        [SerializeField] private Transform weaponBoneAdditive;
        [SerializeField] private IKTransforms rightHand;
        [SerializeField] private IKTransforms leftHand;
        
        private FPSProceduralJob _job;
        private AnimationScriptPlayable _playable;

        private RecoilAnimation _recoilAnimation;
        private Animator _animator;

        private void FindBoneByName(Transform search, ref Transform bone, string boneName)
        {
            if (search.name.Equals(boneName))
            {
                bone = search;
                return;
            }

            for (int i = 0; i < search.childCount; i++)
            {
                FindBoneByName(search.GetChild(i), ref bone, boneName);
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying) return;

            if (ReferenceEquals(skeletonRoot, null))
            {
                FindBoneByName(transform, ref skeletonRoot, "root");
            }

            if (ReferenceEquals(weaponBone, null))
            {
                FindBoneByName(transform, ref weaponBone, "ik_hand_gun");
            }

            if (ReferenceEquals(weaponBoneAdditive, null))
            {
                FindBoneByName(transform, ref weaponBoneAdditive, "ik_hand_gun_additive");
            }
            
            if (ReferenceEquals(rightHand.tip, null))
            {
                FindBoneByName(transform, ref rightHand.tip, "hand_r");
                rightHand.mid = rightHand.tip.parent;
                rightHand.root = rightHand.mid.parent;
            }
            
            if (ReferenceEquals(leftHand.tip, null))
            {
                FindBoneByName(transform, ref leftHand.tip, "hand_l");
                leftHand.mid = leftHand.tip.parent;
                leftHand.root = leftHand.mid.parent;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            
            _recoilAnimation = transform.root.GetComponent<RecoilAnimation>();
            _animator = GetComponent<Animator>();
            
            _job = new FPSProceduralJob()
            {
                animator = _animator,
                skeletonRoot = skeletonRoot,
                rightArm = rightHand,
                leftArm = leftHand,
                weaponBone = weaponBone,
                weaponBoneAdditive = weaponBoneAdditive,
                recoilAnimation = _recoilAnimation
            };
            _job.Setup();
            
            _playable = AnimationScriptPlayable.Create(_animator.playableGraph, _job);
            var output = AnimationPlayableOutput.Create(_animator.playableGraph, "FPS Procedural Output", 
                _animator);
            
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            output.SetSourcePlayable(_playable);
        }
    }
}