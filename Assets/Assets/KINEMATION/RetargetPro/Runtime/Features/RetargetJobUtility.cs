// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;

using UnityEngine.Animations;
using UnityEngine.Playables;

using Unity.Collections;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features
{
    public interface IDynamicRetarget
    {
        public IRetargetJob SetupRetargetJob(PlayableGraph graph, out AnimationScriptPlayable playable);
    }
    
    public interface IRetargetJob
    {
        public void Setup(RetargetFeature feature, Animator animator, KRigComponent source, KRigComponent target);

        public void UpdateSourceRootPose(KTransform sourceRootPose);

        public void SetJobData(AnimationScriptPlayable playable, RetargetFeature feature);
        
        public void Dispose();
    }

    public struct RetargetStreamAtom
    {
        public TransformStreamHandle handle;
        public KTransform cachedMeshPose;
        public KTransform cachedLocalPose;
    }

    public struct RetargetSceneAtom
    {
        public TransformSceneHandle handle;
        public KTransform cachedMeshPose;
    }

    public struct BasicRetargetData
    {
        public float scale;
        public float scaleWeight;
        public float featureWeight;
        public float translationWeight;
        public Vector3 offset;

        public KTransform sourceRootPose;
        public TransformSceneHandle targetRoot;
    }

    public struct IKRetargetStreamAtom
    {
        public RetargetStreamAtom basicAtom;
        public Vector3 position;
        public Vector3 previousPosition;
        public Quaternion previousRotation;
        public float length;
    }

    public struct IKRetargetData
    {
        public BasicRetargetData basicData;
        public float ikWeight;
        public Vector3 effectorOffset;
        public ESpaceType effectorSpace;
        public Vector3 jointOffset;
        public Vector3 localJointForward;
        public Vector3 localJointUp;
        public float poleWeight;
        public int jointIndex;
        public byte useChainIk;
        public float reachMultiplier;
        public int maxIterations;
        public float tolerance;
    }
    
    public class RetargetJobUtility
    {
        public static Vector3 DetectClosestLocalAxis(Quaternion boneRotation, Vector3 worldDirection)
        {
            Vector3 localDirection = Quaternion.Inverse(boneRotation) * worldDirection;
            if (localDirection.sqrMagnitude <= KMath.SqrEpsilon)
            {
                return Vector3.forward;
            }

            Vector3 axis = Vector3.forward;
            float maxDot = 0f;

            if (Mathf.Abs(localDirection.x) > maxDot)
            {
                maxDot = Mathf.Abs(localDirection.x);
                axis = new Vector3(Mathf.Sign(localDirection.x), 0f, 0f);
            }

            if (Mathf.Abs(localDirection.y) > maxDot)
            {
                maxDot = Mathf.Abs(localDirection.y);
                axis = new Vector3(0f, Mathf.Sign(localDirection.y), 0f);
            }

            if (Mathf.Abs(localDirection.z) > maxDot)
            {
                axis = new Vector3(0f, 0f, Mathf.Sign(localDirection.z));
            }

            return axis;
        }

        public static KTransform GetPoseFromHandle(AnimationStream stream, TransformStreamHandle handle)
        {
            return new KTransform()
            {
                position = handle.GetPosition(stream),
                rotation = handle.GetRotation(stream),
                scale = handle.GetLocalScale(stream)
            };
        }
        
        public static KTransform GetPoseFromHandle(AnimationStream stream, TransformSceneHandle handle)
        {
            return new KTransform()
            {
                position = handle.GetPosition(stream),
                rotation = handle.GetRotation(stream),
                scale = handle.GetLocalScale(stream)
            };
        }
        
        public static void SetupStreamAtomChain(Animator animator, ref NativeArray<RetargetStreamAtom> streamChain, 
            Transform[] transformChain, Transform root)
        {
            if (transformChain == null)
            {
                return;
            }

            int num = transformChain.Length;
            streamChain = new NativeArray<RetargetStreamAtom>(num, Allocator.Persistent);

            root ??= animator.transform;
            for (int i = 0; i < num; i++)
            {
                KTransform cachedMeshPose = new KTransform
                {
                    position = root.InverseTransformPoint(transformChain[i].position),
                    rotation = Quaternion.Inverse(root.rotation) * transformChain[i].rotation
                };
                
                streamChain[i] = new RetargetStreamAtom()
                {
                    handle = animator.BindStreamTransform(transformChain[i]),
                    cachedMeshPose = cachedMeshPose,
                    cachedLocalPose = new KTransform(transformChain[i], false),
                };
            }
        }
        
        public static void SetupSceneAtomChain(Animator animator, ref NativeArray<RetargetSceneAtom> streamChain, 
            Transform[] transformChain, Transform root)
        {
            if (transformChain == null)
            {
                return;
            }

            int num = transformChain.Length;
            streamChain = new NativeArray<RetargetSceneAtom>(num, Allocator.Persistent);

            KTransform rootTransform = new KTransform(root);
            for (int i = 0; i < num; i++)
            {
                streamChain[i] = new RetargetSceneAtom()
                {
                    handle = animator.BindSceneTransform(transformChain[i]),
                    cachedMeshPose = rootTransform.GetRelativeTransform(new KTransform(transformChain[i]), true),
                };
            }
        }

        public static void SetupStreamIkAtomChain(Animator animator, ref NativeArray<IKRetargetStreamAtom> streamChain,
            Transform[] transformChain, Transform root)
        {
            if (transformChain == null)
            {
                return;
            }

            int num = transformChain.Length;
            streamChain = new NativeArray<IKRetargetStreamAtom>(num, Allocator.Persistent);

            root ??= animator.transform;
            for (int i = 0; i < num; i++)
            {
                KTransform cachedMeshPose = new KTransform
                {
                    position = root.InverseTransformPoint(transformChain[i].position),
                    rotation = Quaternion.Inverse(root.rotation) * transformChain[i].rotation
                };

                streamChain[i] = new IKRetargetStreamAtom()
                {
                    basicAtom = new RetargetStreamAtom()
                    {
                        handle = animator.BindStreamTransform(transformChain[i]),
                        cachedMeshPose = cachedMeshPose,
                        cachedLocalPose = new KTransform(transformChain[i], false)
                    }
                };
            }
        }

        public static float ComputeAverageRigRadius(Transform[] transforms, Transform root)
        {
            if (transforms == null || root == null || transforms.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;

            int num = transforms.Length;
            for (int i = 0; i < num; i++)
            {
                Transform element = transforms[i];
                if (element == null) continue;

                float distance = Vector3.Distance(root.position, element.position);
                if (distance <= 0.0001f) continue;

                sum += distance;
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            return sum / count;
        }

        public static float ComputeBodyScale(Transform[] sourceTransforms, Transform sourceRoot,
            Transform[] targetTransforms, Transform targetRoot, float fallbackScale = 1f)
        {
            float sourceRadius = ComputeAverageRigRadius(sourceTransforms, sourceRoot);
            float targetRadius = ComputeAverageRigRadius(targetTransforms, targetRoot);

            if (sourceRadius <= 0.0001f || targetRadius <= 0.0001f)
            {
                return fallbackScale;
            }

            return targetRadius / sourceRadius;
        }

        private static KTransform GetSourceComponentPose(AnimationStream stream, RetargetSceneAtom source,
            KTransform sourceRoot)
        {
            return new KTransform(
                sourceRoot.InverseTransformPoint(source.handle.GetPosition(stream), true),
                Quaternion.Inverse(sourceRoot.rotation) * source.handle.GetRotation(stream),
                Vector3.one);
        }

        private static void RetargetAtoms(AnimationStream stream, KTransform sourcePose, KTransform cachedSourcePose,
            RetargetStreamAtom target, BasicRetargetData retargetData, KTransform targetRoot)
        {
            float scale = Mathf.Lerp(1f, retargetData.scale, retargetData.scaleWeight);

            Quaternion delta = Quaternion.Inverse(cachedSourcePose.rotation) * target.cachedMeshPose.rotation;
            Quaternion targetRotation = targetRoot.rotation * sourcePose.rotation * delta;
            
            target.handle.SetLocalRotation(stream, target.cachedLocalPose.rotation);
            targetRotation = Quaternion.Slerp(target.handle.GetRotation(stream), targetRotation, 
                retargetData.featureWeight);
            
            target.handle.SetRotation(stream, targetRotation);

            Vector3 sourceLocal = (sourcePose.position - cachedSourcePose.position) * scale;
            
            Vector3 targetPosition = target.cachedMeshPose.position + sourceLocal + retargetData.offset;
            targetPosition = targetRoot.TransformPoint(targetPosition, true);
            target.handle.SetPosition(stream, targetPosition);

            targetPosition = target.handle.GetLocalPosition(stream);
            
            targetPosition = Vector3.Lerp(target.cachedLocalPose.position, targetPosition, 
                retargetData.translationWeight * retargetData.featureWeight);
            target.handle.SetLocalPosition(stream, targetPosition);
        }

        private static void RetargetAtoms(AnimationStream stream, RetargetSceneAtom source,
            RetargetSceneAtom nextSource, float sourceBlend, RetargetStreamAtom target,
            BasicRetargetData retargetData, KTransform sourceRoot, KTransform targetRoot)
        {
            KTransform sourcePose = KTransform.Lerp(GetSourceComponentPose(stream, source, sourceRoot),
                GetSourceComponentPose(stream, nextSource, sourceRoot), sourceBlend);
            KTransform cachedSourcePose = KTransform.Lerp(source.cachedMeshPose, nextSource.cachedMeshPose, sourceBlend);
            RetargetAtoms(stream, sourcePose, cachedSourcePose, target, retargetData, targetRoot);
        }
        
        public static void BasicRetarget(AnimationStream stream, NativeArray<RetargetSceneAtom> source, 
            NativeArray<RetargetStreamAtom> target, BasicRetargetData retargetData)
        {
            int sourceCount = source.Length;
            int targetCount = target.Length;
            if (sourceCount == 0 || targetCount == 0) return;

            KTransform sourceRoot = retargetData.sourceRootPose;
            KTransform targetRoot = GetPoseFromHandle(stream, retargetData.targetRoot);

            for (int i = 0; i < targetCount; i++)
            {
                float normalizedIndex = targetCount == 1 ? 0f : (float) i / (targetCount - 1);
                float sourcePosition = (sourceCount - 1) * normalizedIndex;
                int sourceIndex = Mathf.FloorToInt(sourcePosition);
                int nextSourceIndex = Mathf.Min(sourceIndex + 1, sourceCount - 1);
                RetargetAtoms(stream, source[sourceIndex], source[nextSourceIndex], sourcePosition - sourceIndex,
                    target[i], retargetData, sourceRoot, targetRoot);
            }
        }
        
        public static void BasicRetarget(AnimationStream stream, NativeArray<RetargetSceneAtom> source, 
            NativeArray<IKRetargetStreamAtom> target, BasicRetargetData retargetData)
        {
            int sourceCount = source.Length;
            int targetCount = target.Length;
            if (sourceCount == 0 || targetCount == 0) return;

            KTransform sourceRoot = retargetData.sourceRootPose;
            KTransform targetRoot = GetPoseFromHandle(stream, retargetData.targetRoot);

            for (int i = 0; i < targetCount; i++)
            {
                float normalizedIndex = targetCount == 1 ? 0f : (float) i / (targetCount - 1);
                float sourcePosition = (sourceCount - 1) * normalizedIndex;
                int sourceIndex = Mathf.FloorToInt(sourcePosition);
                int nextSourceIndex = Mathf.Min(sourceIndex + 1, sourceCount - 1);
                RetargetAtoms(stream, source[sourceIndex], source[nextSourceIndex], sourcePosition - sourceIndex,
                    target[i].basicAtom, retargetData, sourceRoot, targetRoot);
            }
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

        private static void ApplyPoleConstraint(NativeArray<IKRetargetStreamAtom> atoms, Vector3 pole, float poleWeight,
            int jointIndex)
        {
            if (atoms.Length < 3 || poleWeight <= 0f)
            {
                return;
            }

            float weight = Mathf.Clamp01(poleWeight);
            int tipIndex = atoms.Length - 1;
            jointIndex = jointIndex > 0 ? jointIndex : tipIndex - 1;
            jointIndex = Mathf.Clamp(jointIndex, 1, tipIndex - 1);

            Vector3 prev = atoms[jointIndex - 1].position;
            Vector3 next = atoms[jointIndex + 1].position;
            Vector3 axis = next - prev;

            if (axis.sqrMagnitude < KMath.SqrEpsilon)
            {
                return;
            }

            axis.Normalize();

            Vector3 projectedJoint = ProjectPointOnPlane(atoms[jointIndex].position, prev, axis);
            Vector3 projectedPole = ProjectPointOnPlane(pole, prev, axis);

            Vector3 from = projectedJoint - prev;
            Vector3 to = projectedPole - prev;

            if (from.sqrMagnitude < KMath.SqrEpsilon || to.sqrMagnitude < KMath.SqrEpsilon)
            {
                return;
            }

            float angle = Vector3.SignedAngle(from, to, axis) * weight;
            var atom = atoms[jointIndex];
            atom.position = Quaternion.AngleAxis(angle, axis) * (atom.position - prev) + prev;
            atoms[jointIndex] = atom;
        }

        public static bool SolveFABRIK(NativeArray<IKRetargetStreamAtom> atoms, Vector3 target, float maxReach,
            int maxIterations, float tolerance, Vector3 pole, float poleWeight, int poleIndex, float reachMultiplier)
        {
            if (atoms.Length < 2)
            {
                return false;
            }

            float effectiveReach = maxReach * Mathf.Max(0f, reachMultiplier);
            Vector3 rootPosition = atoms[0].position;
            target = ClampToReach(rootPosition, target, effectiveReach);

            var rootToTargetDir = target - rootPosition;
            if (rootToTargetDir.sqrMagnitude > KMath.Square(maxReach))
            {
                var dir = rootToTargetDir.normalized;
                for (int i = 1; i < atoms.Length; ++i)
                {
                    var atom = atoms[i];
                    atom.position = atoms[i - 1].position + dir * atoms[i - 1].length;
                    atoms[i] = atom;
                }

                ApplyPoleConstraint(atoms, pole, poleWeight, poleIndex);
                return true;
            }

            int tipIndex = atoms.Length - 1;
            float sqrTolerance = KMath.Square(tolerance);

            if (KMath.SqrDistance(atoms[tipIndex].position, target) > sqrTolerance)
            {
                var rootPos = atoms[0].position;
                int iteration = 0;

                do
                {
                    var atom = atoms[tipIndex];
                    atom.position = target;
                    atoms[tipIndex] = atom;

                    for (int i = tipIndex - 1; i > -1; --i)
                    {
                        atom = atoms[i];
                        atom.position = atoms[i + 1].position +
                                        (atoms[i].position - atoms[i + 1].position).normalized * atoms[i].length;
                        atoms[i] = atom;
                    }

                    atom = atoms[0];
                    atom.position = rootPos;
                    atoms[0] = atom;

                    for (int i = 1; i < atoms.Length; ++i)
                    {
                        atom = atoms[i];
                        atom.position = atoms[i - 1].position +
                                        (atoms[i].position - atoms[i - 1].position).normalized * atoms[i - 1].length;
                        atoms[i] = atom;
                    }

                    ApplyPoleConstraint(atoms, pole, poleWeight, poleIndex);

                } while (KMath.SqrDistance(atoms[tipIndex].position, target) > sqrTolerance &&
                         ++iteration < maxIterations);

                return true;
            }

            return false;
        }

        public static Vector3 GetEffector(Vector3 target, KTransform sourceRoot, KTransform targetRoot, 
            Vector3 sourcePose, Vector3 targetPose, float scale)
        {
            Vector3 effector = target - sourceRoot.TransformPoint(sourcePose, true);
            effector = effector * scale + targetRoot.TransformPoint(targetPose, true);
            return effector;
        }

        private static Vector3 MoveTargetInSpace(KTransform space, Vector3 targetPosition, Vector3 offset)
        {
            KTransform target = new KTransform(targetPosition, Quaternion.identity, Vector3.one);
            return KAnimationMath.MoveInSpace(space, target, offset, 1f);
        }

        private static KTransform ResolveEffectorOffsetSpace(AnimationStream stream,
            NativeArray<IKRetargetStreamAtom> target, TransformSceneHandle targetRoot, ESpaceType space)
        {
            switch (space)
            {
                case ESpaceType.BoneSpace:
                case ESpaceType.ParentBoneSpace:
                    return GetPoseFromHandle(stream, target[^1].basicAtom.handle);
                case ESpaceType.WorldSpace:
                    return KTransform.Identity;
                default:
                    return GetPoseFromHandle(stream, targetRoot);
            }
        }

        private static Quaternion GetJointOffsetRotation(Vector3 jointForward, Vector3 jointUp)
        {
            if (jointForward.sqrMagnitude <= KMath.SqrEpsilon)
            {
                jointForward = Vector3.forward;
            }

            if (jointUp.sqrMagnitude <= KMath.SqrEpsilon ||
                Mathf.Abs(Vector3.Dot(jointForward.normalized, jointUp.normalized)) > 0.999f)
            {
                jointUp = Mathf.Abs(Vector3.Dot(jointForward.normalized, Vector3.up)) > 0.999f
                    ? Vector3.right
                    : Vector3.up;
            }

            return Quaternion.LookRotation(jointForward, jointUp);
        }

        private static Vector3 GetPoleTarget(in KTransform joint, in IKRetargetData ikData)
        {
            Vector3 direction = GetJointOffsetRotation(ikData.localJointForward, ikData.localJointUp)
                                * ikData.jointOffset;
            return joint.position + joint.rotation * direction;
        }

        public static void SolveTwoBoneIK(AnimationStream stream, NativeArray<RetargetSceneAtom> source, 
            NativeArray<IKRetargetStreamAtom> target, IKRetargetData ikData)
        {
            KTransform sourceTransform = ikData.basicData.sourceRootPose;
            KTransform targetTransform = GetPoseFromHandle(stream, ikData.basicData.targetRoot);

            var tipHandle = target[^1].basicAtom.handle;
            var midHandle = target[^2].basicAtom.handle;
            var rootHandle = target[^3].basicAtom.handle;
            
            Vector3 sourceTip = source[^1].handle.GetPosition(stream);
            KTransform tip = GetPoseFromHandle(stream, tipHandle);
            KTransform mid = GetPoseFromHandle(stream, midHandle);
            KTransform root = GetPoseFromHandle(stream, rootHandle);
            
            Vector3 sourcePose = source[^1].cachedMeshPose.position;
            Vector3 targetPose = target[^1].basicAtom.cachedMeshPose.position;
            
            float scale = Mathf.Lerp(1f, ikData.basicData.scale, ikData.basicData.scaleWeight);
            
            Vector3 effector = GetEffector(sourceTip, sourceTransform, targetTransform, 
                sourcePose, targetPose, scale);
            
            KTransform ikTarget = new KTransform()
            {
                position = effector,
                rotation = Quaternion.identity,
                scale = Vector3.one
            };

            KTransform effectorSpace = ResolveEffectorOffsetSpace(stream, target, ikData.basicData.targetRoot,
                ikData.effectorSpace);
            ikTarget.position = MoveTargetInSpace(effectorSpace, ikTarget.position, ikData.effectorOffset);

            float rawReach = Vector3.Distance(root.position, mid.position) + Vector3.Distance(mid.position, tip.position);
            float maxReach = rawReach * Mathf.Max(0f, ikData.reachMultiplier);
            ikTarget.position = ClampToReach(root.position, ikTarget.position, maxReach);

            int targetJointIndex = Mathf.Clamp(ikData.jointIndex, 0, target.Length - 1);
            KTransform poleJoint = GetPoseFromHandle(stream, target[targetJointIndex].basicAtom.handle);
            KTransform ikPole = new KTransform()
            {
                position = GetPoleTarget(poleJoint, ikData),
                rotation = Quaternion.identity,
                scale = Vector3.one
            };

            float weight = ikData.ikWeight * ikData.basicData.featureWeight;
            float poleWeight = Mathf.Clamp01(ikData.poleWeight) * weight;
            KTwoBoneIkData twoBoneIkData = new KTwoBoneIkData()
            {
                root = root,
                mid = mid,
                tip = tip,
                hint = ikPole,
                target = ikTarget,
                hasValidHint = poleWeight > 0f,
                rotWeight = 0f,
                posWeight = weight,
                hintWeight = poleWeight
            };
            
            KTwoBoneIK.Solve(ref twoBoneIkData);
            
            rootHandle.SetRotation(stream, twoBoneIkData.root.rotation);
            midHandle.SetRotation(stream, twoBoneIkData.mid.rotation);
            tipHandle.SetRotation(stream, twoBoneIkData.tip.rotation);
        }

        public static void SolveChainIK(AnimationStream stream, NativeArray<RetargetSceneAtom> source, 
            NativeArray<IKRetargetStreamAtom> target, IKRetargetData ikData)
        {
            float rawReach = 0f;
            
            for (int i = 0; i < target.Length; i++)
            {
                var atom = target[i];
                Vector3 position = target[i].basicAtom.handle.GetPosition(stream);

                float distance = 0f;
                if (i != target.Length - 1)
                {
                    distance = Vector3.Distance(position, target[i + 1].basicAtom.handle.GetPosition(stream));
                }

                rawReach += distance;
                 
                atom.position = position;
                atom.previousPosition = position;
                atom.previousRotation = atom.basicAtom.handle.GetRotation(stream);
                atom.length = distance;
                target[i] = atom;
            }
            
            KTransform sourceRoot = ikData.basicData.sourceRootPose;
            KTransform targetRoot = GetPoseFromHandle(stream, ikData.basicData.targetRoot);
            
            Vector3 sourceTip = source[^1].handle.GetPosition(stream);
            Vector3 sourcePose = source[^1].cachedMeshPose.position;
            Vector3 targetPose = target[^1].basicAtom.cachedMeshPose.position;
            
            float scale = Mathf.Lerp(1f, ikData.basicData.scale, ikData.basicData.scaleWeight);
            Vector3 effector = GetEffector(sourceTip, sourceRoot, targetRoot, 
                sourcePose, targetPose, scale);
            
            KTransform ikTarget = new KTransform()
            {
                position = effector,
                rotation = Quaternion.identity,
                scale = Vector3.one
            };

            KTransform effectorSpace = ResolveEffectorOffsetSpace(stream, target, ikData.basicData.targetRoot,
                ikData.effectorSpace);
            ikTarget.position = MoveTargetInSpace(effectorSpace, ikTarget.position, ikData.effectorOffset);

            int targetJointIndex = Mathf.Clamp(ikData.jointIndex, 0, target.Length - 1);
            KTransform joint = GetPoseFromHandle(stream, target[targetJointIndex].basicAtom.handle);
            joint.position = target[targetJointIndex].position;
            Vector3 pole = GetPoleTarget(joint, ikData);
            
            if (!SolveFABRIK(target, ikTarget.position, rawReach, ikData.maxIterations, ikData.tolerance, pole,
                    ikData.poleWeight, ikData.jointIndex, ikData.reachMultiplier))
            {
                return;
            }
             
            int tipIndex = target.Length - 1;
            Quaternion tipRotation = target[^1].previousRotation;
            float weight = ikData.ikWeight * ikData.basicData.featureWeight;
             
            // 3. Apply rotations.
            for (int i = 0; i < tipIndex; ++i)
            {
                var prevDir = target[i + 1].previousPosition - target[i].previousPosition;
                var newDir = target[i + 1].position - target[i].position;

                Quaternion baseRot = target[i].previousRotation;
                Quaternion targetRot = KMath.FromToRotation(prevDir, newDir) * baseRot;
                targetRot = Quaternion.Slerp(baseRot, targetRot, weight);
                 
                target[i].basicAtom.handle.SetRotation(stream, targetRot);
            }

            target[^1].basicAtom.handle.SetRotation(stream, tipRotation);
        }
    }
}
