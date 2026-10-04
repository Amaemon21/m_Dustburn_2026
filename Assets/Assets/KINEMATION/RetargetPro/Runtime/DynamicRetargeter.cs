// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System.Collections.Generic;

namespace KINEMATION.RetargetPro.Runtime
{
    public struct DynamicRetargetPair
    {
        public IRetargetJob job;
        public AnimationScriptPlayable playable;
        public RetargetFeature feature;
    }

    public class DynamicRetargeter : MonoBehaviour
    {
        [Tooltip("Model to retarget FROM.")]
        [SerializeField] private GameObject animationSource;
        [SerializeField] private RetargetProfile retargetProfileAsset;

        private Animator _sourceAnimator;
        private Animator _animator;
        private Transform _sourceRootTransform;
        private Transform _sourceRootPoseTransform;
        private Vector3 _sourceRootMotionRestPosition;
        private Quaternion _sourceRootMotionRestRotation = Quaternion.identity;
        private Vector3 _targetRootMotionRestPosition;
        private Quaternion _targetRootMotionRestRotation = Quaternion.identity;
        private bool _hasRootMotionReferencePose;
        private bool _sourceApplyRootMotion;
        private AnimatorCullingMode _sourceCullingMode;
        private bool _hasSourceAnimatorSettings;
        private bool _started;

        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _mixerPlayable;
        private List<DynamicRetargetPair> _retargetJobs = new List<DynamicRetargetPair>();

        private void Start()
        {
            _started = true;
            InitializeRetargeter();
        }

        private void OnEnable()
        {
            if (_started)
            {
                InitializeRetargeter();
            }
        }

        private void InitializeRetargeter()
        {
            _retargetJobs.Clear();
            _hasRootMotionReferencePose = false;
            _sourceRootPoseTransform = null;

            if (animationSource == null)
            {
                Debug.LogError("DynamicRetargeter: Animation Source not assigned!");
                return;
            }

            _sourceAnimator = animationSource.GetComponent<Animator>();
            if (_sourceAnimator == null)
            {
                _sourceAnimator = animationSource.GetComponentInChildren<Animator>();
            }

            if (_sourceAnimator == null)
            {
                Debug.LogWarning("DynamicRetargeter: Source Animator not found. Root motion will be disabled.");
            }
            else
            {
                _sourceApplyRootMotion = _sourceAnimator.applyRootMotion;
                _sourceCullingMode = _sourceAnimator.cullingMode;
                _hasSourceAnimatorSettings = true;
            }

            if (retargetProfileAsset == null)
            {
                Debug.LogError("DynamicRetargeter: Retarget Profile is NULL!");
                return;
            }

            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogError("DynamicRetargeter: Animator is NULL!");
                return;
            }

            KRigComponent sourceRigComponent = animationSource.GetComponentInChildren<KRigComponent>();
            if (sourceRigComponent == null)
            {
                sourceRigComponent = animationSource.AddComponent<KRigComponent>();
                sourceRigComponent.RefreshHierarchy();
            }

            KRigComponent targetRigComponent = GetComponentInChildren<KRigComponent>();
            if (targetRigComponent == null)
            {
                targetRigComponent = gameObject.AddComponent<KRigComponent>();
                targetRigComponent.RefreshHierarchy();
            }

            _sourceRootPoseTransform = sourceRigComponent.transform;

            if (retargetProfileAsset.sourcePose != null)
            {
                retargetProfileAsset.sourcePose.SampleAnimation(animationSource, 0f);
            }

            if (retargetProfileAsset.targetPose != null)
            {
                retargetProfileAsset.targetPose.SampleAnimation(gameObject, 0f);
            }

            sourceRigComponent.Initialize();
            targetRigComponent.Initialize();

            _sourceRootTransform = _sourceAnimator != null ? _sourceAnimator.transform : null;
            if (_sourceRootTransform != null)
            {
                _sourceRootMotionRestPosition = _sourceRootTransform.position;
                _sourceRootMotionRestRotation = _sourceRootTransform.rotation;
                _targetRootMotionRestPosition = transform.position;
                _targetRootMotionRestRotation = transform.rotation;
                _hasRootMotionReferencePose = true;
            }

            _playableGraph = PlayableGraph.Create($"{gameObject.name}_Graph");
            _mixerPlayable = AnimationLayerMixerPlayable.Create(_playableGraph);

            Playable targetPlayable = _mixerPlayable;
            List<RetargetFeature> features = retargetProfileAsset.retargetFeatures;
            int num = features != null ? features.Count : 0;

            for (int i = num - 1; i >= 0; i--)
            {
                RetargetFeature feature = features[i];
                if (feature == null) continue;

                var dynamicRetarget = feature as IDynamicRetarget;
                if (dynamicRetarget == null) continue;

                if (!IsFeatureStateValid(feature, sourceRigComponent, targetRigComponent))
                {
                    Debug.LogWarning($"DynamicRetargeter: Skipping invalid feature `{feature.name}`.");
                    continue;
                }

                var retargetJob = dynamicRetarget.SetupRetargetJob(_playableGraph, out var scriptPlayable);
                if (retargetJob == null || !scriptPlayable.IsValid())
                {
                    Debug.LogWarning($"DynamicRetargeter: Failed to create dynamic job for `{feature.name}`.");
                    continue;
                }

                retargetJob.Setup(feature, _animator, sourceRigComponent, targetRigComponent);
                retargetJob.UpdateSourceRootPose(GetSourceRootPose());
                retargetJob.SetJobData(scriptPlayable, feature);

                targetPlayable.AddInput(scriptPlayable, 0, 1f);
                targetPlayable = scriptPlayable;

                _retargetJobs.Add(new DynamicRetargetPair()
                {
                    job = retargetJob,
                    playable = scriptPlayable,
                    feature = feature
                });
            }

            if (_retargetJobs.Count == 0)
            {
                Debug.LogWarning("DynamicRetargeter: No valid dynamic retarget features found.");
                _hasRootMotionReferencePose = false;
                if (_playableGraph.IsValid())
                {
                    _playableGraph.Destroy();
                }

                return;
            }

            var output = AnimationPlayableOutput.Create(_playableGraph, $"{gameObject.name}_Output", _animator);
            output.SetSourcePlayable(_mixerPlayable);

            _playableGraph.Play();
        }

        private KTransform GetSourceRootPose()
        {
            return _sourceRootPoseTransform != null
                ? new KTransform(_sourceRootPoseTransform)
                : KTransform.Identity;
        }

        private static bool IsFeatureStateValid(RetargetFeature feature, KRigComponent sourceComponent,
            KRigComponent targetComponent)
        {
            RetargetFeatureState state = feature.CreateFeatureState();
            if (state == null)
            {
                return false;
            }

            state.InitializeComponents(sourceComponent, targetComponent, feature);
            bool isValid = state.IsValid();
            state.OnDestroy();
            return isValid;
        }

        private void Update()
        {
            if (_retargetJobs == null || !_playableGraph.IsValid())
            {
                return;
            }

            KTransform sourceRootPose = GetSourceRootPose();
            int num = _retargetJobs.Count;
            for (int i = 0; i < num; i++)
            {
                DynamicRetargetPair retargetJob = _retargetJobs[i];
                retargetJob.job.UpdateSourceRootPose(sourceRootPose);
                retargetJob.job.SetJobData(retargetJob.playable, retargetJob.feature);
            }
        }

        private void LateUpdate()
        {
            if (_sourceAnimator == null || _animator == null || _sourceRootTransform == null ||
                !_hasRootMotionReferencePose)
            {
                return;
            }

            _sourceAnimator.applyRootMotion = _animator.applyRootMotion;
            _sourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!_animator.applyRootMotion)
            {
                transform.position = _targetRootMotionRestPosition;
                transform.rotation = _targetRootMotionRestRotation;
                return;
            }

            Quaternion inverseSourceRestRotation = Quaternion.Inverse(_sourceRootMotionRestRotation);
            Vector3 sourceDeltaPosition = inverseSourceRestRotation *
                                          (_sourceRootTransform.position - _sourceRootMotionRestPosition);
            Quaternion sourceDeltaRotation = inverseSourceRestRotation * _sourceRootTransform.rotation;

            transform.position = _targetRootMotionRestPosition + _targetRootMotionRestRotation * sourceDeltaPosition;
            transform.rotation = _targetRootMotionRestRotation * sourceDeltaRotation;
        }

        private void OnDisable()
        {
            CleanupRetargeter();
        }

        private void OnDestroy()
        {
            CleanupRetargeter();
        }

        private void CleanupRetargeter()
        {
            if (_playableGraph.IsValid())
            {
                _playableGraph.Stop();
                _playableGraph.Destroy();
            }

            if (_retargetJobs != null)
            {
                foreach (var retargetJob in _retargetJobs) retargetJob.job.Dispose();
                _retargetJobs.Clear();
            }

            if (_sourceAnimator != null && _hasSourceAnimatorSettings)
            {
                _sourceAnimator.applyRootMotion = _sourceApplyRootMotion;
                _sourceAnimator.cullingMode = _sourceCullingMode;
            }

            _hasSourceAnimatorSettings = false;
            _hasRootMotionReferencePose = false;
            _sourceRootTransform = null;
            _sourceRootPoseTransform = null;
        }
    }
}
