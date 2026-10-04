// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    public struct StabilizeBoneModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private StabilizeBoneModifierSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _boneHandle;
        private Vector3 _pivotPosition;
        private Vector3 _targetPivotPosition;
        private Vector3 _previousComponentPosition;
        private bool _hasPivot;
        private bool _isLookingForStance;
        private bool _isInterpolatingPivot;

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight) || !_jobData.rootHandle.IsValid(stream) ||
                !_boneHandle.IsValid(stream))
            {
                return;
            }

            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            Vector3 bonePosition = _boneHandle.GetPosition(stream);
            Vector3 componentPosition = rootTransform.InverseTransformPoint(bonePosition, false);

            if (!_hasPivot)
            {
                _pivotPosition = componentPosition;
                _targetPivotPosition = componentPosition;
                _previousComponentPosition = componentPosition;
                _hasPivot = true;
                return;
            }

            float positionThreshold = Mathf.Max(0f, _settings.positionThreshold);
            float stanceThreshold = Mathf.Max(0f, _settings.stanceThreshold);
            float pivotDelta = componentPosition.y - _pivotPosition.y;
            float motionDelta = componentPosition.y - _previousComponentPosition.y;
            bool isOutsidePivotBounds = Mathf.Abs(pivotDelta) > positionThreshold;
            bool isStancePose = Mathf.Abs(motionDelta) <= stanceThreshold;

            _previousComponentPosition = componentPosition;

            if (!_isLookingForStance && !_isInterpolatingPivot && isOutsidePivotBounds)
            {
                _isLookingForStance = true;
            }

            if (_isLookingForStance)
            {
                _targetPivotPosition = componentPosition;

                if (isStancePose)
                {
                    _isLookingForStance = false;
                    _isInterpolatingPivot = true;
                }
            }
            else if (_isInterpolatingPivot)
            {
                float stanceDelta = componentPosition.y - _targetPivotPosition.y;
                bool leftStancePose = Mathf.Abs(stanceDelta) > positionThreshold;

                if (!isStancePose || leftStancePose)
                {
                    _isInterpolatingPivot = false;
                    _isLookingForStance = true;
                    _targetPivotPosition = componentPosition;
                }
            }
            
            float alpha = _settings.pivotSmoothing > 0f
                ? KMath.ExpDecayAlpha(_settings.pivotSmoothing, stream.deltaTime)
                : 1f;

            _pivotPosition.x = Mathf.Lerp(_pivotPosition.x, componentPosition.x, alpha);
            _pivotPosition.z = Mathf.Lerp(_pivotPosition.z, componentPosition.z, alpha);

            if (_isLookingForStance || _isInterpolatingPivot)
            {
                _pivotPosition.y = Mathf.Lerp(_pivotPosition.y, _targetPivotPosition.y, alpha);

                float pivotPositionDelta = _pivotPosition.y - _targetPivotPosition.y;
                if (_isInterpolatingPivot && pivotPositionDelta * pivotPositionDelta < KMath.FloatMin)
                {
                    _pivotPosition.y = _targetPivotPosition.y;
                    _isInterpolatingPivot = false;
                }
            }
            
            Vector3 targetPosition = rootTransform.TransformPoint(_pivotPosition, false);
            _boneHandle.SetPosition(stream, Vector3.Lerp(bonePosition, targetPosition, _jobData.weight));
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (StabilizeBoneModifierSettings) settings;
            _boneHandle = AnimationModifierUtility.GetHandle(in _jobData, _settings.boneToStabilize);
            _pivotPosition = Vector3.zero;
            _targetPivotPosition = Vector3.zero;
            _previousComponentPosition = Vector3.zero;
            _hasPivot = false;
            _isLookingForStance = false;
            _isInterpolatingPivot = false;
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            Initialize(_jobData, newSettings);
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            return AnimationScriptPlayable.Create(graph, this);
        }

        public void PreUpdateJobData()
        {
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            var job = playable.GetJobData<StabilizeBoneModifierJob>();
            _pivotPosition = job._pivotPosition;
            _targetPivotPosition = job._targetPivotPosition;
            _previousComponentPosition = job._previousComponentPosition;
            _hasPivot = job._hasPivot;
            _isLookingForStance = job._isLookingForStance;
            _isInterpolatingPivot = job._isInterpolatingPivot;
            _jobData.weight = weight;
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}
