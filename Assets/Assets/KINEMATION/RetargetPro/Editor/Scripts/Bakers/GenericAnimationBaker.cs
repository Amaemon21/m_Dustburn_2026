// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.FbxExporter.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    public class GenericAnimationBaker : IRetargetProBaker
    {
        private static readonly string[] RootPositionPropertyNames = { "RootT.x", "RootT.y", "RootT.z" };
        private static readonly string[] RootRotationPropertyNames = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        private const float CurveTimeEpsilon = 0.00001f;

        private sealed class AnimationFrame
        {
            public string path = string.Empty;
            public Transform boneReference;
            public readonly AnimationCurve localPositionX = new AnimationCurve();
            public readonly AnimationCurve localPositionY = new AnimationCurve();
            public readonly AnimationCurve localPositionZ = new AnimationCurve();
            public readonly AnimationCurve localRotationX = new AnimationCurve();
            public readonly AnimationCurve localRotationY = new AnimationCurve();
            public readonly AnimationCurve localRotationZ = new AnimationCurve();
            public readonly AnimationCurve localRotationW = new AnimationCurve();
            public Quaternion previousRotation = Quaternion.identity;
            public bool hasPreviousRotation;
            public readonly AnimationCurve localScaleX = new AnimationCurve();
            public readonly AnimationCurve localScaleY = new AnimationCurve();
            public readonly AnimationCurve localScaleZ = new AnimationCurve();
        }

        public const string BakerId = "Generic";

        public string Id => BakerId;
        public string DisplayName => "Generic";

        private readonly List<AnimationFrame> _animationFrames = new List<AnimationFrame>();
        private Transform _rootNode;
        private Transform _modelRoot;
        
        private string _rootNodePath = string.Empty;
        private AnimationCurve[] _rootPositionCurves;
        private AnimationCurve[] _rootRotationCurves;
        
        private AnimationCurve[] _rootMotionPositionCurves;
        private AnimationCurve[] _rootMotionRotationCurves;
        private KTransform _targetStartPose = KTransform.Identity;
        private Vector3 _rootNodeStartScale = Vector3.one;
        private AsciiFbxExporter.DirectPoseRecorder _directPoseRecorder;

        public bool TryGetRootMotionTransformPath(out string path)
        {
            path = _rootNodePath;
            return _rootNode != null;
        }

        private void AddLinearKey(AnimationCurve curve, float time, float value, bool removeRedundant = true)
        {
            int index = curve.length - 1;
            if (index >= 0 && Mathf.Abs(curve.keys[index].time - time) <= CurveTimeEpsilon)
            {
                Keyframe key = curve.keys[index];
                key.time = time;
                key.value = value;
                index = curve.MoveKey(index, key);
            }
            else
            {
                index = curve.AddKey(time, value);
            }

            if (index < 0 || index >= curve.length) return;

            if (removeRedundant && index > 1)
            {
                Keyframe[] keys = curve.keys;
                float a = keys[index - 2].value;
                float b = keys[index - 1].value;

                if (Mathf.Approximately(a, b) && Mathf.Approximately(b, keys[index].value))
                {
                    curve.RemoveKey(index - 1);
                    index--;
                }
            }

            SetLinearTangents(curve, index);
            SetLinearTangents(curve, index - 1);
        }

        private void SetLinearTangents(AnimationCurve curve, int index)
        {
            if (index < 0 || index >= curve.length) return;
            
            AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
        }

        public void Initialize(KRigComponent rigComponent, KRigElement rootMotionBone)
        {
            if (rigComponent == null) return;
            
            Transform root = rigComponent.transform;
            _animationFrames.Clear();

            _modelRoot = rigComponent.transform;
            _rootNode = GetRootNode(rigComponent, rootMotionBone, out _rootNodePath);

            _rootPositionCurves = new[]
            {
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve()
            };
            _rootRotationCurves = new[]
            {
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve()
            };
            
            _rootMotionPositionCurves = new[]
            {
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve()
            };
            _rootMotionRotationCurves = new[]
            {
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve(),
                new AnimationCurve()
            };
            
            _targetStartPose = new KTransform(root);

            var hierarchy = rigComponent.GetHierarchy();

            foreach (var element in hierarchy)
            {
                if (element == null) continue;
                
                if (element == root || !element.IsChildOf(root) || element == _rootNode) continue;
                
                string path = AnimationUtility.CalculateTransformPath(element, root);
                _animationFrames.Add(new AnimationFrame
                {
                    boneReference = element,
                    path = path
                });
            }
        }

        public bool TryInitializeDirectPoseRecorder(KRigComponent rigComponent, KRigElement rootMotionBone,
            string outputPath, float frameRate, int expectedSampleCount, out string error)
        {
            DisposeDirectPoseRecorder();
            if (rigComponent == null)
            {
                error = "Target rig component is missing.";
                return false;
            }

            Transform root = rigComponent.transform;
            _modelRoot = root;
            _rootNode = GetRootNode(rigComponent, rootMotionBone, out _);
            _targetStartPose = new KTransform(root);
            _rootNodeStartScale = _rootNode.localScale;

            var animatedTransforms = new List<Transform>();
            foreach (Transform element in rigComponent.GetHierarchy())
            {
                if (element == null || element == root || !element.IsChildOf(root)) continue;
                animatedTransforms.Add(element);
            }

            var options = new AsciiFbxExporter.DirectPoseExportOptions
            {
                model = root.gameObject,
                animatedTransforms = animatedTransforms,
                outputAssetPath = outputPath,
                sampleRate = frameRate,
                expectedSampleCount = expectedSampleCount,
                stripRootNode = true,
                includeScaleCurves = true,
                optimizeConstantCurves = true
            };

            return AsciiFbxExporter.TryCreateDirectPoseRecorder(options, out _directPoseRecorder, out error);
        }

        public bool CaptureDirectPose(float time, out string error)
        {
            if (_directPoseRecorder == null || _rootNode == null)
            {
                error = "Direct FBX pose recorder is not initialized.";
                return false;
            }

            Vector3 localPosition = _rootNode.localPosition;
            Quaternion localRotation = _rootNode.localRotation;
            Vector3 localScale = _rootNode.localScale;
            KTransform rootPose = _targetStartPose.GetRelativeTransform(new KTransform(_rootNode), false);

            try
            {
                // Match the clip path by folding live model root motion into the recorded root node.
                _rootNode.localPosition = rootPose.position;
                _rootNode.localRotation = rootPose.rotation;
                _rootNode.localScale = _rootNodeStartScale;
                return _directPoseRecorder.Capture(time, out error);
            }
            finally
            {
                _rootNode.localPosition = localPosition;
                _rootNode.localRotation = localRotation;
                _rootNode.localScale = localScale;
            }
        }

        public bool WriteDirectPose(out string error)
        {
            if (_directPoseRecorder == null)
            {
                error = "Direct FBX pose recorder is not initialized.";
                return false;
            }

            return _directPoseRecorder.Write(out error);
        }

        public void DisposeDirectPoseRecorder()
        {
            _directPoseRecorder?.Dispose();
            _directPoseRecorder = null;
        }

        public void BakeAnimationFrame(float time)
        {
            foreach (AnimationFrame frame in _animationFrames)
            {
                Transform element = frame.boneReference;
                Quaternion normalizedRotation = element.localRotation.normalized;
                if (frame.hasPreviousRotation && Quaternion.Dot(frame.previousRotation, normalizedRotation) < 0f)
                {
                    normalizedRotation = new Quaternion(-normalizedRotation.x, -normalizedRotation.y,
                        -normalizedRotation.z, -normalizedRotation.w);
                }

                frame.previousRotation = normalizedRotation;
                frame.hasPreviousRotation = true;

                AddLinearKey(frame.localPositionX, time, element.localPosition.x);
                AddLinearKey(frame.localPositionY, time, element.localPosition.y);
                AddLinearKey(frame.localPositionZ, time, element.localPosition.z);

                AddLinearKey(frame.localRotationW, time, normalizedRotation.w, false);
                AddLinearKey(frame.localRotationX, time, normalizedRotation.x, false);
                AddLinearKey(frame.localRotationY, time, normalizedRotation.y, false);
                AddLinearKey(frame.localRotationZ, time, normalizedRotation.z, false);

                AddLinearKey(frame.localScaleX, time, element.localScale.x);
                AddLinearKey(frame.localScaleY, time, element.localScale.y);
                AddLinearKey(frame.localScaleZ, time, element.localScale.z);
            }

            KTransform rootMotionPose = _targetStartPose.GetRelativeTransform(new KTransform(_rootNode), false);
            
            AddLinearKey(_rootPositionCurves[0], time, rootMotionPose.position.x);
            AddLinearKey(_rootPositionCurves[1], time, rootMotionPose.position.y);
            AddLinearKey(_rootPositionCurves[2], time, rootMotionPose.position.z);
            AddLinearKey(_rootRotationCurves[0], time, rootMotionPose.rotation.x);
            AddLinearKey(_rootRotationCurves[1], time, rootMotionPose.rotation.y);
            AddLinearKey(_rootRotationCurves[2], time, rootMotionPose.rotation.z);
            AddLinearKey(_rootRotationCurves[3], time, rootMotionPose.rotation.w);

            rootMotionPose = _targetStartPose.GetRelativeTransform(new KTransform(_modelRoot), false);
            AddLinearKey(_rootMotionPositionCurves[0], time, rootMotionPose.position.x);
            AddLinearKey(_rootMotionPositionCurves[1], time, rootMotionPose.position.y);
            AddLinearKey(_rootMotionPositionCurves[2], time, rootMotionPose.position.z);
            AddLinearKey(_rootMotionRotationCurves[0], time, rootMotionPose.rotation.x);
            AddLinearKey(_rootMotionRotationCurves[1], time, rootMotionPose.rotation.y);
            AddLinearKey(_rootMotionRotationCurves[2], time, rootMotionPose.rotation.z);
            AddLinearKey(_rootMotionRotationCurves[3], time, rootMotionPose.rotation.w);
        }

        public void WriteToClip(AnimationClip clip)
        {
            foreach (AnimationFrame frame in _animationFrames)
            {
                clip.SetCurve(frame.path, typeof(Transform), "localPosition.x", frame.localPositionX);
                clip.SetCurve(frame.path, typeof(Transform), "localPosition.y", frame.localPositionY);
                clip.SetCurve(frame.path, typeof(Transform), "localPosition.z", frame.localPositionZ);

                clip.SetCurve(frame.path, typeof(Transform), "localRotation.x", frame.localRotationX);
                clip.SetCurve(frame.path, typeof(Transform), "localRotation.y", frame.localRotationY);
                clip.SetCurve(frame.path, typeof(Transform), "localRotation.z", frame.localRotationZ);
                clip.SetCurve(frame.path, typeof(Transform), "localRotation.w", frame.localRotationW);

                clip.SetCurve(frame.path, typeof(Transform), "localScale.x", frame.localScaleX);
                clip.SetCurve(frame.path, typeof(Transform), "localScale.y", frame.localScaleY);
                clip.SetCurve(frame.path, typeof(Transform), "localScale.z", frame.localScaleZ);
            }
            
            clip.SetCurve(_rootNodePath, typeof(Transform), "localPosition.x", _rootPositionCurves[0]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localPosition.y", _rootPositionCurves[1]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localPosition.z", _rootPositionCurves[2]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localRotation.x", _rootRotationCurves[0]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localRotation.y", _rootRotationCurves[1]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localRotation.z", _rootRotationCurves[2]);
            clip.SetCurve(_rootNodePath, typeof(Transform), "localRotation.w", _rootRotationCurves[3]);
        }

        public void WriteRootMotion(AnimationClip source, AnimationClip target)
        {
            if (target == null || _rootNode == null)
            {
                return;
            }

            for (int i = 0; i < _rootPositionCurves.Length; i++)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator),
                    RootPositionPropertyNames[i]);
                AnimationUtility.SetEditorCurve(target, binding, _rootMotionPositionCurves[i]);
            }

            for (int i = 0; i < _rootRotationCurves.Length; i++)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator),
                    RootRotationPropertyNames[i]);
                AnimationUtility.SetEditorCurve(target, binding, _rootMotionRotationCurves[i]);
            }
        }

        private static Transform GetRootNode(KRigComponent rigComponent, KRigElement rootNodeBone, 
            out string transformPath)
        {
            transformPath = string.Empty;
            Transform resolved = rigComponent.GetRigTransform(rootNodeBone.name);
            
            if (resolved == null) return rigComponent.transform;
            
            transformPath = AnimationUtility.CalculateTransformPath(resolved, rigComponent.transform);
            return resolved;
        }
    }
}
