// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Misc;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [HelpURL("https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/character-animation-component")]
    [AddComponentMenu("KINEMATION/CAS/Character Animation Component")]
    [DisallowMultipleComponent]
    public class CharacterAnimationComponent : MonoBehaviour, IAssetDragAndDrop
    {
        public delegate void CreateAssetDelegate<T>(string assetName) where T : ScriptableObject;
        [SerializeField] protected CharacterAnimationSettings animationSettings;
        
        protected LayeredBlendingComponent _layeredBlendingComponent;
        protected ProceduralAnimationComponent _proceduralAnimationComponent;

        protected AnimationSlotMixer _preOverlaySlot;
        protected AnimationSlotMixer _overlaySlot;
        protected AnimationSlotMixer _fullBodySlot;
        
        protected Animator _animator;
        protected CharacterSkeleton _skeleton;

        protected bool _ignoreBlending = true;
        protected bool _isInitialized;
        protected bool _isGraphValid;
        protected bool _hasValidBlendingComponent;
        protected bool _hasValidProceduralComponent;

        protected PoseBlendingJob _poseBlendingJob;
        protected AnimationScriptPlayable _poseBlendingPlayable;
        protected AnimationPlayableOutput _poseBlendingOutput;
        protected bool _isBlendingOut;
        protected BlendTime _blendTime;

        protected CharacterAnimationSettings _activeSettings;
        protected CasSettingsOverride _casOverride;
        private CharacterAnimationSettings _cachedSettings;
        
        protected bool IsValidAndInitialized()
        {
            return _isInitialized && _isGraphValid;
        }

        public float GetOverlaySlotWeight()
        {
            return _overlaySlot.MixerWeight;
        }

        public virtual bool PlayAnimation(AnimationAsset newAnimation, float startTime = 0f, 
            AnimationMixerEvent[] customEvents = null)
        {
            if (!IsValidAndInitialized() || !_hasValidBlendingComponent || newAnimation == null ||
                newAnimation.clip == null)
            {
                return false;
            }

            if (newAnimation.slot == AnimationSlot.DefaultOverlay)
            {
                _preOverlaySlot.Play(newAnimation, startTime, customEvents);
                return true;
            }

            if (newAnimation.slot == AnimationSlot.Overlay)
            {
                _overlaySlot.Play(newAnimation, startTime, customEvents);
                return true;
            }
            
            _fullBodySlot.Play(newAnimation, startTime, customEvents);
            return true;
        }

        public virtual void StopAllAnimations(float blendOutTime = 0f)
        {
            if (!IsValidAndInitialized() || !_hasValidBlendingComponent) return;
             
            _preOverlaySlot.Stop(blendOutTime);
            _overlaySlot.Stop(blendOutTime);
            _fullBodySlot.Stop(blendOutTime);
        }

        public virtual void StopAnimation(AnimationAsset animationToStop)
        {
            if (animationToStop == null) return;
            StopAnimation(animationToStop.slot, animationToStop.blendTime.blendOutTime);
        }

        public virtual void StopAnimation(AnimationSlot slot, float blendOutTime = 0f)
        {
            if (!IsValidAndInitialized() || !_hasValidBlendingComponent) return;

            if (slot == AnimationSlot.DefaultOverlay)
            {
                _preOverlaySlot.Stop(blendOutTime);
                return;
            }
            
            if (slot == AnimationSlot.Overlay)
            {
                _overlaySlot.Stop(blendOutTime);
                return;
            }
            
            _fullBodySlot.Stop(blendOutTime);
        }
        
        public virtual void UpdateAnimationSettings(CharacterAnimationSettings newSettings, 
            CasSettingsOverride casOverride)
        {
            if (!IsValidAndInitialized() || newSettings == null) return;

            bool hadNullSettings = _activeSettings == null;
            _activeSettings = animationSettings = _cachedSettings = newSettings;
            _casOverride = casOverride;

            if (_ignoreBlending || hadNullSettings || Mathf.Approximately(newSettings.blendTime.blendInTime, 0f))
            {
                OnAnimationSettingsUpdated();
                _animator.WriteDefaultValues();
                return;
            }
            
            RequestPoseBlending(_activeSettings.blendTime);
            if(_hasValidProceduralComponent) _proceduralAnimationComponent.QueueModifierUpdates();
        }

        public virtual void UpdateAnimationSettings(CharacterAnimationSettings newSettings)
        {
            CasSettingsOverride casOverride = new CasSettingsOverride();
            UpdateAnimationSettings(newSettings, casOverride);
        }

        public virtual void RequestPoseBlending(BlendTime blendTime)
        {
            _blendTime = blendTime;
            _poseBlendingJob.cachePose = true;
            _poseBlendingPlayable.SetJobData(_poseBlendingJob);
        }

        protected virtual void OnAnimationSettingsUpdated()
        {
            if (_layeredBlendingComponent != null)
            {
                _layeredBlendingComponent.UpdateLayeringData(new AnimationLayeringData()
                {
                    basePose = _casOverride.basePose == null ? _activeSettings.basePose : _casOverride.basePose,
                    
                    overlayPose = _casOverride.overlayPose == null
                        ? _activeSettings.overlayPose
                        : _casOverride.overlayPose,
                    
                    overlayController = _casOverride.overlayAnimator == null
                        ? _activeSettings.overlayAnimator
                        : _casOverride.overlayAnimator,
                    
                    blendTime = _activeSettings.blendTime
                });
            }

            if (_proceduralAnimationComponent != null)
            {
                _proceduralAnimationComponent.UpdateProceduralAnimation(_activeSettings.proceduralSettings);
            }
        }

        protected virtual void BuildAnimationSlotPlayables()
        {
            if (!_hasValidBlendingComponent) return;

            var playableGraph = _animator.playableGraph;
            var blendingOutput = _layeredBlendingComponent.GetOutput();
            var sourcePlayable = blendingOutput.GetSourcePlayable();
                
            _preOverlaySlot = new AnimationSlotMixer(playableGraph, 3, 1);
            _overlaySlot = new AnimationSlotMixer(playableGraph, 3, 1);

            // Disconnect the overlay playable.
            var overlayJobPlayable = sourcePlayable.GetInput(1).GetInput(0);
            sourcePlayable.GetInput(1).DisconnectInput(0);

            // Connect it to the dynamic slot.
            _preOverlaySlot.mixer.ConnectInput(0, overlayJobPlayable, 0, 1f);
            _overlaySlot.mixer.ConnectInput(0, _preOverlaySlot.mixer, 0, 1f);

            // Connect plug the dynamic slot into the main layered mixer.
            sourcePlayable.GetInput(1).ConnectInput(0, _overlaySlot.mixer, 0, 1f);
                
            _fullBodySlot = new AnimationSlotMixer(playableGraph, 3, 1);
            _fullBodySlot.mixer.ConnectInput(0, sourcePlayable, 0, 1f);
            
            blendingOutput.SetSourcePlayable(_fullBodySlot.mixer);
        }

        protected virtual void InitializeBlendingPlayable()
        {
            var hierarchy = _skeleton.GetTransformHierarchy();
            
            _poseBlendingJob = new PoseBlendingJob();
            _poseBlendingJob.bones = new NativeArray<BonePose>(hierarchy.Length, Allocator.Persistent);

            for (int i = 0; i < hierarchy.Length; i++)
            {
                _poseBlendingJob.bones[i] = new BonePose()
                {
                    handle = _animator.BindStreamTransform(hierarchy[i]),
                    pose = KTransform.Identity
                };
            }

            BuildBlendingPlayable();
        }

        protected virtual void BuildBlendingPlayable()
        {
            _poseBlendingJob.blendWeight = 1f;
            _poseBlendingPlayable = AnimationScriptPlayable.Create(_animator.playableGraph, _poseBlendingJob);
            _poseBlendingOutput =
                AnimationPlayableOutput.Create(_animator.playableGraph, "CAS Pose Blending Output", _animator);
            _poseBlendingOutput.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            _poseBlendingOutput.SetSourcePlayable(_poseBlendingPlayable);
        }

        protected virtual void InitializeCharacterAnimation()
        {
            if (_animator == null)
            {
                Debug.LogError("Character Animation Component: Animator not found.");
                return;
            }
            
            if (!_animator.playableGraph.IsValid()) return;
            
            if (_skeleton == null)
            {
                Debug.LogError("Character Animation Component: Skeleton not found.");
                return;
            }
            
            if (_hasValidBlendingComponent)
            {
                _layeredBlendingComponent.InitializePlayableComponent(_animator, _skeleton);
            }
            
            if (_hasValidProceduralComponent)
            {
                _proceduralAnimationComponent.InitializePlayableComponent(_animator, _skeleton);
            }

            BuildAnimationSlotPlayables();
            InitializeBlendingPlayable();
            
            _isInitialized = true;
            _isGraphValid = true;
            UpdateAnimationSettings(animationSettings);
        }

        private void Awake()
        {
            if (!Application.isPlaying) return;
             
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogError("Character Animation Component: Animator not found.");
                return;
            }
            
            _layeredBlendingComponent = GetComponent<LayeredBlendingComponent>();
            _proceduralAnimationComponent = GetComponent<ProceduralAnimationComponent>();

            _hasValidBlendingComponent = _layeredBlendingComponent != null;
            _hasValidProceduralComponent = _proceduralAnimationComponent != null;
            
            _skeleton = GetComponentInChildren<CharacterSkeleton>();
            InitializeCharacterAnimation();

            if (!_isInitialized) return;
            
#if UNITY_EDITOR
            _cachedSettings = animationSettings;
#endif
        }

        private void Update()
        {
            if (_animator == null) return;
            
            if (_isGraphValid && !_animator.playableGraph.IsValid()) _isGraphValid = false;
            if (!IsValidAndInitialized()) return;

            if (_hasValidProceduralComponent) _proceduralAnimationComponent.PreUpdateProceduralAnimation();
            if (_poseBlendingJob.cachePose) return;
            
            if (_hasValidBlendingComponent)
            {
                _preOverlaySlot.Update();
                _overlaySlot.Update();
                _fullBodySlot.Update();
                
                _layeredBlendingComponent.UpdatePlayableComponent();
                _layeredBlendingComponent.UpdateDynamicBoneBlending(_overlaySlot.MixerWeight);
            }
            
            if (_hasValidProceduralComponent) _proceduralAnimationComponent.UpdatePlayableComponent();

            if (!Mathf.Approximately(_poseBlendingJob.blendWeight, 1f))
            {
                _poseBlendingJob.Evaluate(_blendTime, !_isBlendingOut);
                _poseBlendingPlayable.SetJobData(_poseBlendingJob);
            }
        }

        private void LateUpdate()
        {
            if (_animator == null) return;
            
            if (!_isInitialized && _animator.playableGraph.IsValid())
            {
                InitializeCharacterAnimation();
                if (!_isInitialized) return;
            }

            _ignoreBlending = false;

            // Handle the playable graph destruction in runtime.
            if (!_isGraphValid && _animator.playableGraph.IsValid())
            {
                if (_hasValidBlendingComponent) _layeredBlendingComponent.BuildPlayables();
                if (_hasValidProceduralComponent) _proceduralAnimationComponent.BuildPlayables();
                
                BuildAnimationSlotPlayables();
                BuildBlendingPlayable();
                
                _isGraphValid = true;
            }

            if (!IsValidAndInitialized()) return;

            // If changing animation settings, update the components and start blending.
            if (_poseBlendingJob.cachePose)
            {
                OnAnimationSettingsUpdated();
                _poseBlendingJob.blendPlayback = _poseBlendingJob.blendWeight = 0f;
                _poseBlendingJob.cachePose = false;
                _poseBlendingPlayable.SetJobData(_poseBlendingJob);
            }
            
            // Late update components otherwise.
            if (_hasValidBlendingComponent) _layeredBlendingComponent.LateUpdatePlayableComponent();
            if (_hasValidProceduralComponent) _proceduralAnimationComponent.LateUpdatePlayableComponent();
        }

        private void OnDestroy()
        {
            if (_poseBlendingJob.bones.IsCreated) _poseBlendingJob.bones.Dispose();
        }

        public void SetAsset(ScriptableObject asset)
        {
            if (asset is CharacterAnimationSettings newSettings) animationSettings = newSettings;
        }
        
#if UNITY_EDITOR
        public static CreateAssetDelegate<CharacterAnimationSettings> onCreateAsset;

        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            
            if (_cachedSettings == animationSettings) return;

            if (animationSettings != null)
            {
                UpdateAnimationSettings(animationSettings);
            }

            _cachedSettings = animationSettings;
        }

        [ContextMenu("Create Animation Settings")]
        public void CreateAnimationSettings()
        {
            onCreateAsset?.Invoke($"CAS_{gameObject.name}");
        }
#endif
    }
}