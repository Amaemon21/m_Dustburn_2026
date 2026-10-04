// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    public struct AvatarVelocity
    {
        public Vector3 translation;
        public Vector3 angular;

        public AvatarVelocity(Vector3 translation, Vector3 angular)
        {
            this.translation = translation;
            this.angular = angular;
        }
    }
    
    public struct LocomotionJob : IAnimationJob
    {
        [ReadOnly] public TransformSceneHandle root;

        // Translation and angular velocities.
        public NativeReference<AvatarVelocity> avatarVelocity;
        public NativeArray<BlendStreamAtom> atoms;
        public NativeArray<OverlayCurveProperty> curveProperties;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            KTransform rootTransform = new KTransform()
            {
                rotation = root.GetRotation(stream),
                position = root.GetPosition(stream)
            };
            
            int num = atoms.Length;
            for (int i = 0; i < num; i++)
            {
                var atom = atoms[i];

                TransformStreamHandle handle = atom.isDynamicBone ? atom.dynamicBoneData.source : atom.handle;

                KTransform atomTransform = new KTransform()
                {
                    rotation = handle.GetRotation(stream),
                    position = handle.GetPosition(stream),
                    scale = Vector3.one
                };
                
                atom.meshStreamPose = rootTransform.GetRelativeTransform(atomTransform, false);
                if (atom.dynamicBoneData.updateMode != BlendMode.FootTarget)
                {
                    atom.meshStreamPose.position = handle.GetLocalPosition(stream);
                }
                
                if (atom.baseWeightHandle.IsValid(stream))
                {
                    atom.baseWeight = atom.baseWeightHandle.GetFloat(stream);
                }
                
                if (atom.additiveWeightHandle.IsValid(stream))
                {
                    atom.additiveWeight = atom.additiveWeightHandle.GetFloat(stream);
                }
                
                if (atom.localWeightHandle.IsValid(stream))
                {
                    atom.localWeight = atom.localWeightHandle.GetFloat(stream);
                }

                atoms[i] = atom;
            }

            num = curveProperties.Length;
            for (int i = 0; i < num; i++)
            {
                var property = curveProperties[i];
                if (!property.handle.IsValid(stream)) continue;
                property.locomotionValue = property.handle.GetFloat(stream);
                curveProperties[i] = property;
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
            var scaleFactor = 1.0f;
            if (stream.isHumanStream)
            {
                var humanStream = stream.AsHuman();
                scaleFactor = humanStream.humanScale;
            }
            
            Vector3 translation = stream.velocity * scaleFactor;
            Vector3 rotation = stream.angularVelocity * scaleFactor;
            avatarVelocity.Value = new AvatarVelocity(translation, rotation);
        }
    }
    
    public struct PoseJob : IAnimationJob
    {
        [ReadOnly] public TransformSceneHandle root;
        public NativeArray<BlendStreamAtom> atoms;
        
        [ReadOnly] public bool alwaysAnimate;
        [ReadOnly] public bool readPose;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            AnimationStream overlayPose = stream.GetInputStream(0);
            KTransform rootTransform = new KTransform()
            {
                rotation = root.GetRotation(stream),
                position = root.GetPosition(stream)
            };
            
            int num = atoms.Length;
            for (int i = 0; i < num; i++)
            {
                var atom = atoms[i];
                
                TransformStreamHandle handle = atom.isDynamicBone ? atom.dynamicBoneData.source : atom.handle;

                if (atom.isDynamicBone)
                {
                    atom.dynamicBoneData.spacePose = DynamicBone.GetDynamicBonePose(overlayPose, atom.dynamicBoneData);
                }
                
                if (alwaysAnimate || readPose)
                {
                    KTransform atomTransform = new KTransform()
                    {
                        position = handle.GetPosition(stream),
                        rotation = handle.GetRotation(stream)
                    };

                    atomTransform = rootTransform.GetRelativeTransform(atomTransform, false);
                    atom.activePose.basePose = atomTransform;
                    if (atom.dynamicBoneData.updateMode != BlendMode.FootTarget)
                    {
                        atom.activePose.basePose.position = handle.GetLocalPosition(stream);
                    }
                }
                
                atoms[i] = atom;
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }

    public struct OverlayJob : IAnimationJob
    {
        public NativeArray<BlendStreamAtom> atoms;
        [ReadOnly] public TransformSceneHandle root;
        [ReadOnly] public bool readOverlay;
        [ReadOnly] public bool alwaysAnimate;
        [ReadOnly] public float dynamicBoneWeight;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            KTransform rootTransform = new KTransform()
            {
                rotation = root.GetRotation(stream),
                position = root.GetPosition(stream)
            };

            int num = atoms.Length;
            for (int i = 0; i < num; i++)
            {
                var atom = atoms[i];

                // 1. Process dynamic bones.
                if (atom.isDynamicBone)
                {
                    // 1.1 Extract dynamic bone pose from idle and normal overlays.
                    var spaceHandle = atom.dynamicBoneData.space;
                    var source = atom.dynamicBoneData.spacePose;
                    var target = DynamicBone.GetDynamicBonePose(stream, atom.dynamicBoneData);
                    var space = KAnimationMath.GetTransform(stream, spaceHandle);

                    // 1.2 Blend dynamic bone poses.
                    source = KTransform.Lerp(source, target,
                        atom.dynamicBoneData.updateMode == BlendMode.Default ? dynamicBoneWeight : 0f);
                    source = space.GetWorldTransform(source, false);

                    // 1.3 Update dynamic bone poses.
                    atom.handle.SetPosition(stream, source.position);
                    atom.handle.SetRotation(stream, source.rotation);
                }
                else if (!alwaysAnimate && !readOverlay)
                {
                    // Don't process if not a dynamic bone, and we've read the pose before.
                    continue;
                }
                
                KTransform atomTransform = new KTransform()
                {
                    rotation = atom.handle.GetRotation(stream),
                    position = atom.handle.GetPosition(stream)
                };
                
                atomTransform = rootTransform.GetRelativeTransform(atomTransform, false);

                atom.activePose.overlayPose = atomTransform;
                if (atom.dynamicBoneData.updateMode != BlendMode.FootTarget)
                {
                    atom.activePose.overlayPose.position = atom.handle.GetLocalPosition(stream);
                }
                
                atom.activePose.localOverlayRotation = atom.handle.GetLocalRotation(stream);

                atoms[i] = atom;
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
    
    // Processes final layering.
    public struct LayeringJob : IAnimationJob
    {
        [ReadOnly] public float blendWeight;
        [ReadOnly] public float globalWeight;
        [ReadOnly] public bool cachePose;
        
        [ReadOnly] public TransformSceneHandle root;
        public NativeArray<BlendStreamAtom> atoms;
        public NativeArray<OverlayCurveProperty> curveProperties;

        public void ProcessAnimation(AnimationStream stream)
        {
            KTransform rootTransform = new KTransform()
            {
                rotation = root.GetRotation(stream),
                position = root.GetPosition(stream)
            };

            int num = atoms.Length;
            
            // Apply mesh-space additive.
            for (int i = 0; i < num; i++)
            {
                var atom = atoms[i];

                atom.UpdateAtomWeights(stream);
                
                if (atom.dynamicBoneData.updateMode == BlendMode.FootTarget)
                {
                    KTransform footTargetPose = KAnimationMath.GetTransform(stream, atom.dynamicBoneData.source);
                    footTargetPose = rootTransform.GetRelativeTransform(footTargetPose, false);

                    footTargetPose.position.y = atom.meshStreamPose.position.y;
                    footTargetPose = rootTransform.GetWorldTransform(footTargetPose, false);
                    
                    atom.handle.SetPosition(stream, footTargetPose.position);
                    atom.handle.SetRotation(stream, footTargetPose.rotation);
                    continue;
                }
                
                float weight = atom.isDynamicBone && !atom.dynamicBoneData.applyBlending ? 1f : blendWeight;
                AtomPose blendedPose = atom.GetBlendedAtomPose(weight);

                if (cachePose)
                {
                    atom.cachedPose = blendedPose;
                    atoms[i] = atom;
                }

                KTransform meshBasePose = blendedPose.basePose;
                KTransform meshOverlayPose = blendedPose.overlayPose;
                Quaternion localOverlayRotation = blendedPose.localOverlayRotation;

                float additiveWeight = blendedPose.additiveWeight;
                float baseWeight = blendedPose.baseWeight;
                float localWeight = blendedPose.localWeight;
                
                KTransform additive = new KTransform()
                {
                    rotation = atom.meshStreamPose.rotation * Quaternion.Inverse(meshBasePose.rotation),
                    position = atom.meshStreamPose.position - meshBasePose.position
                };

                Quaternion rotation = additive.rotation * meshOverlayPose.rotation;

                // Blend additive.
                rotation = Quaternion.Slerp(meshOverlayPose.rotation, rotation, additiveWeight);
                // Blend locomotion pose.
                rotation = Quaternion.Slerp(atom.meshStreamPose.rotation, rotation, baseWeight);
                // Convert to world space.
                rotation = rootTransform.rotation * rotation;
                
                Vector3 position = meshOverlayPose.position + additive.position * additiveWeight;
                position = Vector3.Lerp(atom.meshStreamPose.position, position, baseWeight);
                
                atom.handle.SetRotation(stream, rotation);
                rotation = Quaternion.Slerp(atom.handle.GetLocalRotation(stream), localOverlayRotation,
                    localWeight);
                atom.handle.SetLocalRotation(stream, rotation);

                position = Vector3.Lerp(position, meshOverlayPose.position, localWeight);
                if (atom.dynamicBoneData.updateMode == BlendMode.FootTarget)
                {
                    atom.handle.SetPosition(stream, rootTransform.TransformPoint(position, false));
                    continue;
                }
                
                atom.handle.SetLocalPosition(stream, position);
            }

            num = curveProperties.Length;
            for (int i = 0; i < num; i++)
            {
                var property = curveProperties[i];
                if (!property.handle.IsValid(stream)) continue;
                property.handle.SetFloat(stream, property.handle.GetFloat(stream) + property.locomotionValue);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
    
    public struct BonePose
    {
        public TransformStreamHandle handle;
        public KTransform pose;
    }
    
    public struct PoseBlendingJob : IAnimationJob
    {
        public NativeArray<BonePose> bones;
        
        [ReadOnly] public float blendWeight;
        [ReadOnly] public float blendPlayback;
        [ReadOnly] public bool cachePose;

        public void Evaluate(BlendTime blendTime, bool blendIn = true)
        {
            float time = blendIn ? blendTime.blendInTime : blendTime.blendOutTime;
            blendPlayback = Mathf.Clamp(blendPlayback + Time.deltaTime, 0f, time);
            blendWeight = KCurves.Ease(0f, 1f, blendPlayback / time, blendTime.easeMode);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (Mathf.Approximately(blendWeight, 1f) && !cachePose) return;
            
            int count = bones.Length;
            for (int i = 0; i < count; i++)
            {
                var atom = bones[i];
               
                Vector3 position = Vector3.Lerp(atom.pose.position, 
                    atom.handle.GetLocalPosition(stream), blendWeight);
                atom.handle.SetLocalPosition(stream, position);
                
                Quaternion rotation = Quaternion.Slerp(atom.pose.rotation, 
                    atom.handle.GetLocalRotation(stream), blendWeight);
                atom.handle.SetLocalRotation(stream, rotation);

                if (!cachePose) continue;
                
                atom.pose.position = position;
                atom.pose.rotation = rotation;

                bones[i] = atom;
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
}