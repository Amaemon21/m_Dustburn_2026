// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [HelpURL("https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/layered-blending")]
    [AddComponentMenu("KINEMATION/CAS/CAS: Layered Blending")]
    [DisallowMultipleComponent]
    public class LayeredBlendingComponent : MonoBehaviour, IPlayablesComponent, IBindableContext
    {
        public AnimatorControllerPlayable OverlayAnimator => _overlayAnimator;
        
        [Range(0f, 1f)] [SerializeField] protected float globalWeight = 1f;
        [Unfold] public List<LayeredBlend> layeredBlends = new List<LayeredBlend>();
        
        [Tooltip("If true, Overlays will be forced to update every frame.")]
        [SerializeField] protected bool alwaysAnimatePoses = true;
        
        [Tooltip("If true, parameters will be copied from main Animator to Overlays.")]
        [SerializeField] protected bool linkAnimatorParameters = true;

        [Tooltip("Toggles root motion.")]
        [SerializeField] protected bool applyRootMotion = true;

        [Tooltip("Propagates animated properties from main Animator to Overlays.")]
        [Unfold] [SerializeField] protected List<OverlayFloatProperty> overlayParameters;
        
        protected PoseJob _poseJob;
        protected AnimationScriptPlayable _poseJobPlayable;

        protected OverlayJob _overlayJob;
        protected AnimationScriptPlayable _overlayJobPlayable;
        protected Playable _overlayPlayable;
        
        protected LayeringJob _layeringJob;
        protected AnimationScriptPlayable _layeringJobPlayable;

        protected LocomotionJob _locomotionJob;
        
        protected AnimatorControllerPlayable _overlayAnimator;
        protected NativeArray<BlendStreamAtom> _atoms;
        protected NativeArray<OverlayCurveProperty> _curveProperties;

        protected bool _isInitialized;

        protected Animator _animator;
        protected PlayableGraph _playableGraph;
        protected AnimationPlayableOutput _locomotionOutput;
        protected AnimationPlayableOutput _layeredBlendingOutput;
        
        protected float _blendPlayback = 1f;

        protected bool _isOverlayPose;
        protected AnimationLayeringData _layeringData;

        protected bool _forceBlendOut;
        protected CharacterSkeleton _skeleton;

        protected HashSet<string> _overlayAnimatorParameters;

        protected CharacterController _characterController;
        protected Rigidbody _rigidBody;
        
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_LowerBody;
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_Spine;
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_Head;
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_Arm_R;
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_Arm_L;
        [HideInInspector] [SerializeField] public LayeringVectorParameter Layering_Fingers;

        [HideInInspector] [SerializeField] public LayeringFloatParameter Mask_Procedural_Animation;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Weapon_Bone_Weight;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Mask_Look_Rotation;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Mask_Attach_Hand;
        
        [HideInInspector] [SerializeField] public LayeringFloatParameter Enable_HandR_IK;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Enable_HandL_IK;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Enable_FootR_IK;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Enable_FootL_IK;
        
        [HideInInspector] [SerializeField] public LayeringFloatParameter Interaction_HandR_IK;
        [HideInInspector] [SerializeField] public LayeringFloatParameter Interaction_HandL_IK;
        
        [HideInInspector] [SerializeField] public LayeringFloatParameter Traversal_Weight;
        
        private void OnValidate()
        {
            if (!_isInitialized) return;
            
            _layeredBlendingOutput.SetWeight(globalWeight);

            _poseJob.alwaysAnimate = alwaysAnimatePoses;
            _overlayJob.alwaysAnimate = alwaysAnimatePoses;
            
            _poseJobPlayable.SetJobData(_poseJob);
            _overlayJobPlayable.SetJobData(_overlayJob);
        }

        public KTransform GetRootMotionDelta()
        {
            if (!_locomotionJob.avatarVelocity.IsCreated) return KTransform.Identity;

            float deltaTime = Time.unscaledDeltaTime;
            
            AvatarVelocity velocity = _locomotionJob.avatarVelocity.Value;
            velocity.translation = transform.TransformVector(velocity.translation) * deltaTime;
            velocity.angular *= Mathf.Rad2Deg * deltaTime;
            
            Quaternion angularVelocity = Quaternion.Euler(velocity.angular.x, velocity.angular.y, velocity.angular.z);
            
            return new KTransform(velocity.translation, angularVelocity);
        }

        public bool IsAnimatorValid()
        {
            return _overlayAnimator.IsValid() && _overlayAnimator.GetLayerCount() > 0;
        }
        
        public virtual void UpdateDynamicBoneBlending(float weight = 0f)
        {
            if (!_isInitialized) return;
            
            _overlayJob.dynamicBoneWeight = weight;
            _overlayJobPlayable.SetJobData(_overlayJob);
        }

        public void StopLayeredBlending(float blendOutTime = -1f)
        {
            if (_forceBlendOut) return;
            
            _forceBlendOut = true;
            _blendPlayback = 0f;
            if (blendOutTime >= 0f) _layeringData.blendTime.blendOutTime = blendOutTime;

            if (_layeringData.blendTime.blendOutTime <= 0f)
            {
                UpdateBlendWeights();
                SetProcessJobs(false);
            }
        }
        
        public virtual void UpdateLayeringData(AnimationLayeringData layeringData)
        {
            if (!layeringData.IsValid())
            {
                Debug.LogError("Animation Layering: Layering Data is invalid.");
                return;
            }
            
            SetNewLayeringData(layeringData);
            
            _layeringJob.blendWeight = 1f;
            _layeringJobPlayable.SetJobData(_layeringJob);
        }
        
        protected virtual void SetProcessJobs(bool isActive)
        {
            _poseJobPlayable.SetProcessInputs(isActive);
            _overlayJobPlayable.SetProcessInputs(isActive);
            _layeringJobPlayable.SetProcessInputs(isActive);
        }

        protected bool IsBlendingActive()
        {
            return _playableGraph.IsValid() && _layeringJobPlayable.GetProcessInputs();
        }
        
        protected virtual void SetNewLayeringData(AnimationLayeringData newData)
        {
            var prevData = _layeringData;
            _layeringData = newData;

            // If data is the same, do not update layering settings.
            if (prevData.overlayPose == _layeringData.overlayPose &&
                prevData.overlayController == _layeringData.overlayController)
            {
                return;
            }
            
            if (prevData.basePose != _layeringData.basePose)
            {
                AnimationLayeringUtility.ConnectPose(_poseJobPlayable, _playableGraph, 
                    _layeringData.basePose, 1);
            }

            if (prevData.overlayPose != _layeringData.overlayPose)
            {
                AnimationLayeringUtility.ConnectPose(_poseJobPlayable, _playableGraph, 
                    _layeringData.overlayPose);
            }

            if (_layeringData.overlayController != null 
                && _layeringData.overlayController != prevData.overlayController)
            {
                _overlayAnimator = AnimationLayeringUtility.ConnectController(_overlayPlayable, _playableGraph, 
                    _layeringData.overlayController);
                _isOverlayPose = false;

                // We always need to update the overlay animator controller.

                _overlayJob.alwaysAnimate = true;
                _overlayJobPlayable.SetJobData(_overlayJob);
                
                _overlayAnimatorParameters.Clear();
                for (int i = 0; i < _overlayAnimator.GetParameterCount(); i++)
                {
                    _overlayAnimatorParameters.Add(_overlayAnimator.GetParameter(i).name);
                }
            }
            else if(_layeringData.overlayPose != prevData.overlayPose || !_isOverlayPose)
            {
                AnimationLayeringUtility.ConnectPose(_overlayPlayable, _playableGraph, _layeringData.overlayPose);
                _isOverlayPose = true;
            }
            
            if (!alwaysAnimatePoses)
            {
                _poseJob.readPose = true;
                if (_isOverlayPose) _overlayJob.readOverlay = true;
                
                _poseJobPlayable.SetJobData(_poseJob);
                _overlayJobPlayable.SetJobData(_overlayJob);
            }
            
            UpdateBlendWeights(globalWeight);

            SetProcessJobs(true);
            _forceBlendOut = false;
        }

        protected bool InitializeBlendingProperty(BindableProperty<float> property, 
            ref Dictionary<string, PropertyStreamHandle> map, ref PropertyStreamHandle outHandle)
        {
            bool hasValidHandle = false;
            if (property.IsBound)
            {
                if (!map.TryGetValue(property.PropertyPath, out var handle))
                {
                    handle = _animator.BindStreamProperty(transform, property.GetContextType(), property.PropertyPath);
                    map.TryAdd(property.PropertyPath, handle);
                }

                outHandle = handle;
                hasValidHandle = true;
            }
            else
            {
                property.Initialize(gameObject);
            }
            
            return hasValidHandle;
        }
        
        protected virtual void InitializeBlendingProperties()
        {
            // Reset all weights.
            for (int i = 0; i < _atoms.Length; i++)
            {
                var atom = _atoms[i];
                atom.baseWeight = atom.additiveWeight = atom.localWeight = 0f;
                _atoms[i] = atom;
            }

            Dictionary<string, PropertyStreamHandle> propertiesMap = new Dictionary<string, PropertyStreamHandle>();

            int index = 0;
            // Initialize bound weight properties.
            foreach (var blend in layeredBlends)
            {
                foreach (var element in blend.layer.elementChain)
                {
                    var atom = _atoms[index];

                    InitializeBlendingProperty(blend.baseWeight, ref propertiesMap, ref atom.baseWeightHandle);
                    InitializeBlendingProperty(blend.additiveWeight, ref propertiesMap, ref atom.additiveWeightHandle);
                    InitializeBlendingProperty(blend.localWeight, ref propertiesMap, ref atom.localWeightHandle);

                    _atoms[index] = atom;
                    index++;
                }
            }

            // Initialize overlay curve properties.
            List<PropertyStreamHandle> handles = new List<PropertyStreamHandle>();

            foreach (var parameter in overlayParameters)
            {
                PropertyStreamHandle handle = new PropertyStreamHandle();
                if (!InitializeBlendingProperty(parameter.property, ref propertiesMap, ref handle)) continue;
                handles.Add(handle);
            }

            _curveProperties = new NativeArray<OverlayCurveProperty>(handles.Count, Allocator.Persistent);
            for (int i = 0; i < handles.Count; i++)
            {
                _curveProperties[i] = new OverlayCurveProperty()
                {
                    handle = handles[i],
                    locomotionValue = 0f
                };
            }
        }
        
        protected virtual void InitializeLayeringJobs()
        {
            var rootSceneHandle = _animator.BindSceneTransform(_animator.transform);

            _poseJob = new PoseJob()
            {
                atoms = _atoms,
                alwaysAnimate = alwaysAnimatePoses,
                readPose = false,
                root = rootSceneHandle
            };

            _overlayJob = new OverlayJob()
            {
                atoms = _atoms,
                alwaysAnimate = alwaysAnimatePoses,
                root = rootSceneHandle
            };
            
            _layeringJob = new LayeringJob()
            {
                atoms = _atoms,
                curveProperties = _curveProperties,
                root = rootSceneHandle,
                cachePose = false,
                blendWeight = 1f
            };

            _locomotionJob = new LocomotionJob()
            {
                atoms = _atoms,
                avatarVelocity = new NativeReference<AvatarVelocity>(Allocator.Persistent),
                curveProperties = _curveProperties,
                root = rootSceneHandle,
            };
        }
        
        protected virtual void UpdateBlendWeights(float overrideWeight = 1f)
        {
            if (_layeringJobPlayable.IsValid())
            {
                _layeringJob.globalWeight = overrideWeight * globalWeight;
                _layeringJobPlayable.SetJobData(_layeringJob);
            }
            
            int index = 0;

            foreach (var blend in layeredBlends)
            {
                bool isBaseBound = blend.baseWeight.IsBound;
                bool isAdditiveBound = blend.additiveWeight.IsBound;
                bool isLocalBound = blend.localWeight.IsBound;
                
                float baseWeight = isBaseBound ? 0f : blend.baseWeight.GetValue() * _layeringJob.globalWeight;
                float additiveWeight = isAdditiveBound ? 0f : blend.additiveWeight.GetValue() * _layeringJob.globalWeight;
                float localWeight = isLocalBound ? 0f : blend.localWeight.GetValue() * _layeringJob.globalWeight;
                
                foreach (var unused in blend.layer.elementChain)
                {
                    var atom = _atoms[index];
                    
                    if (!isBaseBound) atom.baseWeight = baseWeight;
                    if (!isAdditiveBound) atom.additiveWeight = additiveWeight;
                    if (!isLocalBound) atom.localWeight = localWeight;
                    
                    _atoms[index] = atom;
                    
                    index++;
                }
            }
        }

        public virtual void BuildPlayables()
        {
            _playableGraph = _animator.playableGraph;
            
            var locomotionPlayable = AnimationScriptPlayable.Create(_playableGraph, _locomotionJob);
            _locomotionOutput = AnimationPlayableOutput.Create(_playableGraph, "CAS Locomotion Output", _animator);
            _locomotionOutput.SetSourcePlayable(locomotionPlayable);
            _locomotionOutput.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            
            _poseJobPlayable = AnimationScriptPlayable.Create(_playableGraph, _poseJob, 2);
            _overlayJobPlayable = AnimationScriptPlayable.Create(_playableGraph, _overlayJob, 1);
            _overlayPlayable = Playable.Create(_playableGraph, 1);
            _overlayJobPlayable.ConnectInput(0, _overlayPlayable, 0, 1f);
            
            _layeringJobPlayable = AnimationScriptPlayable.Create(_playableGraph, _layeringJob, 2);
            _layeringJobPlayable.ConnectInput(0, _poseJobPlayable, 0, 1f);
            _layeringJobPlayable.ConnectInput(1, _overlayJobPlayable, 0, 1f);
            
            _layeredBlendingOutput = AnimationPlayableOutput.Create(_playableGraph, "CAS Layered Blending Output", 
                _animator);
            _layeredBlendingOutput.SetWeight(globalWeight);
            _layeredBlendingOutput.SetSourcePlayable(_layeringJobPlayable);
            _layeredBlendingOutput.SetAnimationStreamSource(AnimationStreamSource.DefaultValues);

            if (!_layeringData.IsValid()) return;
            
            AnimationLayeringUtility.ConnectPose(_poseJobPlayable, _playableGraph,
                _layeringData.basePose, 1);
            
            AnimationLayeringUtility.ConnectPose(_poseJobPlayable, _playableGraph,
                _layeringData.overlayPose);
            
            if (!_isOverlayPose)
            {
                _overlayAnimator = AnimationLayeringUtility.ConnectController(_overlayPlayable, _playableGraph, 
                    _layeringData.overlayController);
            }
            else
            {
                AnimationLayeringUtility.ConnectPose(_overlayPlayable, _playableGraph, _layeringData.overlayPose);
            }
        }

        public virtual void InitializePlayableComponent(Animator animator, CharacterSkeleton skeleton)
        {
            _animator = animator;
            if (_animator == null)
            {
                Debug.LogError("Animation Layering: Animator not found!");
                return;
            }

            _skeleton = skeleton;
            if (_skeleton == null)
            {
                Debug.LogError("Animation Layering: Skeleton not found!");
                return;
            }

            Transform root = transform.root;
            _characterController = root.GetComponent<CharacterController>();
            _rigidBody = root.GetComponent<Rigidbody>();

            _overlayAnimatorParameters = new HashSet<string>();

            // Populate a filtered hierarchy map.
            List<Transform> hierarchy = new List<Transform>();
            foreach (var blend in layeredBlends)
            {
                foreach (var element in blend.layer.elementChain)
                {
                    Transform boneTransform = _skeleton.GetBoneTransform(element.name);
                    if (boneTransform == null)
                    {
                        Debug.LogWarning($"{GetType().Name}: {element.name} is not in the hierarchy!");
                        continue;
                    }
                    
                    hierarchy.Add(boneTransform);
                }
            }
            
            _playableGraph = _animator.playableGraph;
            _atoms = AnimationLayeringUtility.SetupBlendAtoms(_animator, hierarchy.ToArray());
            
            InitializeBlendingProperties();
            UpdateBlendWeights();
            InitializeLayeringJobs();
            BuildPlayables();
            
            _isInitialized = true;
        }

        public AnimationPlayableOutput GetOutput()
        {
            return _layeredBlendingOutput;
        }

        private void OnEnable()
        { 
            if(_isInitialized) _layeredBlendingOutput.SetWeight(globalWeight);
        }

        private void OnDisable()
        {
            if (_isInitialized) _layeredBlendingOutput.SetWeight(0f);
        }
        
        protected virtual void OnAnimatorMove()
        {
            if (!applyRootMotion) return;

            KTransform rootMotionDelta = GetRootMotionDelta();
            bool hasTranslation = !Mathf.Approximately(rootMotionDelta.position.magnitude, 0f);
            
            if (_rigidBody != null && _rigidBody.detectCollisions)
            {
                if(hasTranslation) _rigidBody.MovePosition(_rigidBody.position + rootMotionDelta.position);
                _rigidBody.MoveRotation(_rigidBody.rotation * rootMotionDelta.rotation);
                return;
            }

            // Only affect the top transform.
            var targetTransform = transform.root;
            targetTransform.rotation *= rootMotionDelta.rotation;

            if (_characterController != null && _characterController.enabled && _characterController.detectCollisions)
            {
                if(hasTranslation) _characterController.Move(rootMotionDelta.position);
                return;
            }
            
            targetTransform.position += rootMotionDelta.position;
        }

        protected virtual void LinkAnimatorParameters()
        {
            foreach (var parameter in _animator.parameters)
            {
                if (!_overlayAnimatorParameters.TryGetValue(parameter.name, out var value))
                {
                    continue;
                }
                
                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    _overlayAnimator.SetBool(parameter.name, _animator.GetBool(parameter.name));
                    continue;
                }
                
                if (parameter.type == AnimatorControllerParameterType.Float)
                {
                    _overlayAnimator.SetFloat(parameter.name, _animator.GetFloat(parameter.name));
                    continue;
                }
                
                if (parameter.type == AnimatorControllerParameterType.Int)
                {
                    _overlayAnimator.SetInteger(parameter.name, _animator.GetInteger(parameter.name));
                }
            }
        }

        public virtual void UpdatePlayableComponent()
        {
            if (!_isInitialized || !IsBlendingActive()) return;
            
#if UNITY_EDITOR
            if (!_animator.isInitialized)
            {
                if (!_isOverlayPose)
                {
                    _overlayAnimator = AnimationLayeringUtility.ConnectController(_overlayPlayable, _playableGraph,
                        _layeringData.overlayController);
                }
            }
#endif
            
            if (linkAnimatorParameters && !_isOverlayPose && _overlayAnimator.IsValid() && _animator.isInitialized)
            {
                LinkAnimatorParameters();
            }

            float blendOutWeight = 1f;
            if (_forceBlendOut)
            {
                _blendPlayback += Time.deltaTime;
                _blendPlayback = Mathf.Clamp(_blendPlayback, 0f, _layeringData.blendTime.blendOutTime);
                blendOutWeight = _blendPlayback / _layeringData.blendTime.blendOutTime;
                blendOutWeight = KCurves.Ease(1f, 0f, blendOutWeight, _layeringData.blendTime.easeMode);
            }

            if (Mathf.Approximately(blendOutWeight, 0f))
            {
                SetProcessJobs(false);
                return;
            }

            if (_forceBlendOut || alwaysAnimatePoses || !_isOverlayPose)
            {
                UpdateBlendWeights(blendOutWeight);
            }
        }
        
        public virtual void LateUpdatePlayableComponent()
        {
        }

        protected virtual void OnDestroy()
        {
            if (_atoms.IsCreated) _atoms.Dispose();
            if (_curveProperties.IsCreated) _curveProperties.Dispose();
            if (_locomotionJob.avatarVelocity.IsCreated) _locomotionJob.avatarVelocity.Dispose();
        }

        public GameObject GetContext()
        {
            return gameObject;
        }
    }
}