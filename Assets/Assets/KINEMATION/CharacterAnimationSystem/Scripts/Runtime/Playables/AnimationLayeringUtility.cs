// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    // Represents a layered pose state.
    public struct AtomPose
    {
        public KTransform basePose;
        public KTransform overlayPose;
        public Quaternion localOverlayRotation;
        
        public float baseWeight;
        public float additiveWeight;
        public float localWeight;

        public static AtomPose Lerp(AtomPose a, AtomPose b, float alpha)
        {
            AtomPose outPose = new AtomPose();
            
            outPose.basePose = KTransform.Lerp(a.basePose, b.basePose, alpha);
            outPose.overlayPose = KTransform.Lerp(a.overlayPose, b.overlayPose, alpha);
            outPose.localOverlayRotation = Quaternion.Slerp(a.localOverlayRotation, b.localOverlayRotation, alpha);

            outPose.additiveWeight = Mathf.Lerp(a.additiveWeight, b.additiveWeight, alpha);
            outPose.baseWeight = Mathf.Lerp(a.baseWeight, b.baseWeight, alpha);
            outPose.localWeight = Mathf.Lerp(a.localWeight, b.localWeight, alpha);
            
            return outPose;
        }
    }
    
    // Represents a layered pose for a single bone.
    public struct BlendStreamAtom
    {
        [Unity.Collections.ReadOnly] public TransformStreamHandle handle;
        [Unity.Collections.ReadOnly] public float baseWeight;
        [Unity.Collections.ReadOnly] public float additiveWeight;
        [Unity.Collections.ReadOnly] public float localWeight;
        
        [Unity.Collections.ReadOnly] public PropertyStreamHandle baseWeightHandle;
        [Unity.Collections.ReadOnly] public PropertyStreamHandle additiveWeightHandle;
        [Unity.Collections.ReadOnly] public PropertyStreamHandle localWeightHandle;
        
        public KTransform meshStreamPose;
        public AtomPose activePose;
        public AtomPose cachedPose;

        [Unity.Collections.ReadOnly] public bool isDynamicBone;
        public DynamicBoneData dynamicBoneData;
        
        private static float GetPropertyValue(AnimationStream stream, PropertyStreamHandle handle)
        {
            return handle.IsValid(stream) ? handle.GetFloat(stream) : 0f;
        }
        
        public AtomPose GetBlendedAtomPose(float blendWeight)
        {
            return AtomPose.Lerp(cachedPose, activePose, blendWeight);
        }

        public void UpdateAtomWeights(AnimationStream stream)
        {
            activePose.baseWeight = Mathf.Clamp01(baseWeight + GetPropertyValue(stream, baseWeightHandle));
            activePose.additiveWeight = Mathf.Clamp01(additiveWeight + GetPropertyValue(stream, additiveWeightHandle));
            activePose.localWeight = Mathf.Clamp01(localWeight + GetPropertyValue(stream, localWeightHandle));
        }
    }

    [Serializable]
    public struct LayeredBlend
    {
        [CustomElementChainDrawer(false, true)]
        public KRigElementChain layer;
        
        [CustomBindableDrawer(false)]
        [BoundRange(0f, 1f)] public BindableProperty<float> baseWeight;
        [CustomBindableDrawer(false)]
        [BoundRange(0f, 1f)] public BindableProperty<float> additiveWeight;
        [CustomBindableDrawer(false)]
        [BoundRange(0f, 1f)] public BindableProperty<float> localWeight;
    }

    [Serializable]
    public struct OverlayFloatProperty
    {
        [CustomBindableDrawer(false)]
        public BindableProperty<float> property;
    }

    public struct OverlayCurveProperty
    {
        [Unity.Collections.ReadOnly] public PropertyStreamHandle handle;
        public float locomotionValue;
    }
    
    [Serializable]
    public struct LayeringVectorParameter
    {
        public float baseWeight;
        public float additiveWeight;
        public float localWeight;
    }

    [Serializable]
    public struct LayeringFloatParameter
    {
        public float value;
    }
    
    public struct AnimationLayeringData
    {
        public AnimationClip basePose;
        public AnimationClip overlayPose;
        public RuntimeAnimatorController overlayController;

        public BlendTime blendTime;

        public bool IsValid()
        {
            return basePose != null && (overlayPose != null || overlayController != null);
        }
    }
    
    public class AnimationLayeringUtility
    {
        // Initializes Blend Stream Atoms for each bone.
        public static NativeArray<BlendStreamAtom> SetupBlendAtoms(Animator animator, Transform[] bones)
        {
            int num = bones.Length;
            var blendAtoms = new NativeArray<BlendStreamAtom>(num, Allocator.Persistent);
            for (int i = 0; i < num; i++)
            {
                Transform bone = bones[i];
                
                var blendStreamAtom = new BlendStreamAtom()
                {
                    handle = animator.BindStreamTransform(bone)
                };
                
                blendAtoms[i] = blendStreamAtom;

                DynamicBone dynamicBone = bone.GetComponent<DynamicBone>();

                if (dynamicBone != null)
                {
                    blendStreamAtom.isDynamicBone = true;
                    blendStreamAtom.dynamicBoneData = new DynamicBoneData()
                    {
                        updateMode = dynamicBone.updateMode,
                        space = animator.BindStreamTransform(dynamicBone.transform.parent),
                        source = animator.BindStreamTransform(dynamicBone.target),
                        applyBlending = dynamicBone.applyBlending
                    };
                }
                
                blendAtoms[i] = blendStreamAtom;
            }

            return blendAtoms;
        }

        public static void ConnectPose(Playable playable, PlayableGraph graph, AnimationClip pose, int inputIndex = 0, 
            float speed = 0f)
        {
            if (playable.GetInput(inputIndex).IsValid())
            {
                playable.DisconnectInput(inputIndex);
            }

            var posePlayable = AnimationClipPlayable.Create(graph, pose);
            posePlayable.SetSpeed(speed);
            posePlayable.SetApplyFootIK(false);
            
            playable.ConnectInput(inputIndex, posePlayable, 0, 1f);
        }

        public static AnimatorControllerPlayable ConnectController(Playable playable, PlayableGraph graph, 
            RuntimeAnimatorController controller)
        {
            if (playable.GetInput(0).IsValid())
            {
                playable.DisconnectInput(0);
            }

            var controllerPlayable = AnimatorControllerPlayable.Create(graph, controller);
            playable.ConnectInput(0, controllerPlayable, 0, 1f);

            return controllerPlayable;
        }

        public static KRigElementChain MergeChains(string name, in KRigElementChain[] chains)
        {
            KRigElementChain mergedChain = new KRigElementChain();
            mergedChain.chainName = name;

            foreach (var chain in chains)
            {
                if(chain == null) continue;
                foreach (var element in chain.elementChain) mergedChain.elementChain.Add(element);
            }

            return mergedChain;
        }
        
        public static KRigElementChain MergeChains(string name, in List<KRigElementChain> chains)
        {
            KRigElementChain mergedChain = new KRigElementChain();
            mergedChain.chainName = name;

            foreach (var chain in chains)
            {
                if(chain == null) continue;
                foreach (var element in chain.elementChain) mergedChain.elementChain.Add(element);
            }

            return mergedChain;
        }
    }
}