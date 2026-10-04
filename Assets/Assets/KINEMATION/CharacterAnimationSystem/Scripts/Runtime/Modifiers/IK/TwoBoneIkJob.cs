// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK
{
    public struct TwoBoneIkJob : IAnimationJob, IAnimationModifierJob
    {
        private TwoBoneIkSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _tip;
        private TransformStreamHandle _mid;
        private TransformStreamHandle _root;
        
        private TransformStreamHandle _tipTarget;
        private TransformStreamHandle _poleTarget;

        private KTransform _ikTarget;

        private BindableProperty<Transform> _ikTargetProp;
        private Transform _lastIkTarget;
        private Transform _parentBone;
        private TransformStreamHandle _parentHandle;
        private bool _isValidSetup;

        private Vector3 _localJointForward;
        private Vector3 _localJointUp;

        private static Vector3 ClampEffectorPosition(in KTransform root, in KTransform mid, in KTransform tip,
            Vector3 effectorPosition, float maxLimbLengthScale)
        {
            float clampedScale = Mathf.Clamp01(maxLimbLengthScale);
            float upperLimbLength = Vector3.Distance(root.position, mid.position);
            float lowerLimbLength = Vector3.Distance(mid.position, tip.position);
            float maxLimbLength = (upperLimbLength + lowerLimbLength) * clampedScale;

            if (maxLimbLength <= KMath.FloatMin)
            {
                return root.position;
            }

            Vector3 toEffector = effectorPosition - root.position;
            float effectorDistance = toEffector.magnitude;

            if (effectorDistance <= maxLimbLength || effectorDistance <= KMath.FloatMin)
            {
                return effectorPosition;
            }

            return root.position + toEffector / effectorDistance * maxLimbLength;
        }

#if UNITY_EDITOR
        private Vector3 _fkRootPosition;
        private Vector3 _fkMidPosition;
        private Vector3 _fkTipPosition;

        private Vector3 _ikRootPosition;
        private Vector3 _ikMidPosition;
        private Vector3 _ikTipPosition;
#endif
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_isValidSetup || Mathf.Approximately(_jobData.weight, 0f)) return;
            if (!_tip.IsValid(stream) || !_mid.IsValid(stream) || !_root.IsValid(stream)) return;
            if (!_settings.useWorldTarget && !_tipTarget.IsValid(stream)) return;
            
            KTransform ikTarget;

            if (_settings.useWorldTarget)
            {
                ikTarget = _parentHandle.IsValid(stream)
                    ? KAnimationMath.GetTransform(stream, _parentHandle).GetWorldTransform(_ikTarget, false)
                    : _ikTarget;
            }
            else
            {
                ikTarget = KAnimationMath.GetTransform(stream, _tipTarget);
            }

            KTransform root = KAnimationMath.GetTransform(stream, _root);
            KTransform mid = KAnimationMath.GetTransform(stream, _mid);
            KTransform tip = KAnimationMath.GetTransform(stream, _tip);
            ikTarget.position = ClampEffectorPosition(root, mid, tip, ikTarget.position, _settings.maxLimbLengthScale);

            KTwoBoneIkData ikData = new KTwoBoneIkData()
            {
                tip = tip,
                mid = mid,
                root = root,
                target = ikTarget,
                hintWeight = _settings.hintWeight,
                posWeight = _jobData.weight,
                rotWeight = _jobData.weight
            };

#if UNITY_EDITOR
            _fkRootPosition = ikData.root.position;
            _fkMidPosition = ikData.mid.position;
            _fkTipPosition = ikData.tip.position;
#endif
            
            if (_poleTarget.IsValid(stream))
            {
                ikData.hasValidHint = true;
                ikData.hint = KAnimationMath.GetTransform(stream, _poleTarget);
            }
            else if (_settings.hintWeight > float.Epsilon)
            {
                ikData.hasValidHint = true;
                Vector3 direction = Quaternion.LookRotation(_localJointForward, _localJointUp) 
                                    * _settings.hintOffset;
                ikData.hint.position = ikData.mid.position + ikData.mid.rotation * direction;
            }

            KTwoBoneIK.Solve(ref ikData);
            
            _root.SetRotation(stream, ikData.root.rotation);
            _mid.SetRotation(stream, ikData.mid.rotation);
            _tip.SetRotation(stream, ikData.tip.rotation);
            
#if UNITY_EDITOR
            _ikRootPosition = ikData.root.position;
            _ikMidPosition = ikData.mid.position;
            _ikTipPosition = ikData.tip.position;
#endif
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        private void TryFindParentBone(Transform newTransform)
        {
            _parentBone = null;
            _parentHandle = new TransformStreamHandle();

            if (newTransform == null) return;

            // 1. Target is inside the hierarchy, find the closest stream bone.
            if (newTransform.root == _jobData.animator.transform.root)
            {
                Transform boneTransform = newTransform.transform;

                while (boneTransform != _jobData.animator.transform.root)
                {
                    if (_jobData.streamBones.TryGetValue(boneTransform.name, out var streamBone))
                    {
                        _parentHandle = streamBone.handle;
                        _parentBone = boneTransform;
                        return;
                    }

                    boneTransform = boneTransform.parent;
                }
            }
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = (TwoBoneIkSettings) settings;
            _ikTarget = KTransform.Identity;

#if UNITY_EDITOR
            _fkRootPosition = _fkMidPosition = _fkTipPosition = Vector3.zero;
            _ikRootPosition = _ikMidPosition = _ikTipPosition = Vector3.zero;
#endif

            var tipT = jobData.skeleton.GetBoneTransform(_settings.tip.name);
            if (tipT == null)
            {
                Debug.LogWarning($"{nameof(TwoBoneIkJob)}: Invalid tip bone '{_settings.tip.name}'.");
                return;
            }
            
            var midT = tipT.parent;
            if (midT == null)
            {
                Debug.LogWarning($"{nameof(TwoBoneIkJob)}: Mid bone not found for tip '{_settings.tip.name}'.");
                return;
            }
            
            var rootT = midT.parent;
            if (rootT == null)
            {
                Debug.LogWarning($"{nameof(TwoBoneIkJob)}: Root bone not found for tip '{_settings.tip.name}'.");
                return;
            }

            _tip = AnimationModifierUtility.GetHandle(in _jobData, _settings.tip);
            _mid = AnimationModifierUtility.GetHandle(in _jobData, new KRigElement(-1, midT.name));
            _root = AnimationModifierUtility.GetHandle(in _jobData, new KRigElement(-1, rootT.name));

            KTransform rootTransform = new KTransform(_jobData.animator.transform);

            KTransform midMeshPose = _jobData.skeleton.GetSkeletonBone(midT.name).meshPose;
            midMeshPose = rootTransform.GetWorldTransform(midMeshPose, false);
            
            _localJointForward = AnimationModifierUtility.DetectClosestLocalAxis(midMeshPose.rotation, 
                rootTransform.forward);
            _localJointUp = AnimationModifierUtility.DetectClosestLocalAxis(midMeshPose.rotation, rootTransform.up);
            
            _tipTarget = AnimationModifierUtility.GetHandle(in _jobData, _settings.target);
            _poleTarget = AnimationModifierUtility.GetHandle(in _jobData, _settings.hintTarget);

            _ikTargetProp = _settings.ikTargetTransform.CreateProperty(_jobData.animator.gameObject);
            _lastIkTarget = _ikTargetProp.GetValue();
            TryFindParentBone(_lastIkTarget);
            _isValidSetup = true;
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
            var jobData = playable.GetJobData<TwoBoneIkJob>();
            _jobData.weight = weight;

            if (_isValidSetup && _settings.useWorldTarget && _ikTargetProp.IsValid())
            {
                Transform ikTarget = _ikTargetProp.GetValue();
                if (_lastIkTarget != ikTarget) TryFindParentBone(ikTarget);
                _lastIkTarget = ikTarget;

                if (ikTarget == null)
                {
                    _ikTarget = KTransform.Identity;
                }
                else
                {
                    _ikTarget = new KTransform(ikTarget);
                    _ikTarget = _parentBone == null
                        ? _ikTarget
                        : new KTransform(_parentBone).GetRelativeTransform(_ikTarget, false);
                }
            }

            playable.SetJobData(this);
            
#if UNITY_EDITOR
            _fkRootPosition = jobData._fkRootPosition;
            _fkMidPosition = jobData._fkMidPosition;
            _fkTipPosition = jobData._fkTipPosition;
            _ikRootPosition = jobData._ikRootPosition;
            _ikMidPosition = jobData._ikMidPosition;
            _ikTipPosition = jobData._ikTipPosition;
#endif
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (Mathf.Approximately(_jobData.weight, 0f)) return;
            
            Color color = Gizmos.color;
            Vector3 size = new Vector3(0.05f, 0.05f, 0.05f);

            AnimationModifierUtility.DrawPyramid(_fkMidPosition, _fkRootPosition, size, Color.red);
            AnimationModifierUtility.DrawPyramid(_fkTipPosition, _fkMidPosition, size, Color.red);

            Handles.Label(_fkTipPosition, "FK");
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_fkTipPosition, 0.04f);

            if (_jobData.weight > 0f)
            {
                AnimationModifierUtility.DrawPyramid(_ikMidPosition, _ikRootPosition, size, Color.green);
                AnimationModifierUtility.DrawPyramid(_ikTipPosition, _ikMidPosition, size, Color.green);

                Handles.Label(_ikTipPosition, "IK");
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_ikTipPosition, 0.04f);
            }
            
            Gizmos.color = color;
#endif
        }
        
        public void OnSceneGUI()
        {
        }
    }
}