// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Misc;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [HelpURL("https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/procedural-animation")]
    [AddComponentMenu("KINEMATION/CAS/CAS: Procedural Animation")]
    [DisallowMultipleComponent]
    public class ProceduralAnimationComponent : MonoBehaviour, IPlayablesComponent, IAssetDragAndDrop
    {
        [SerializeField] [Range(0f, 1f)] protected float globalWeight = 1f;
        [SerializeField] protected ProceduralAnimationSettings proceduralSettings;
        
#if UNITY_EDITOR
        [Tooltip("Toggles animation modifiers gizmos.")]
        [SerializeField] private bool showModifierGizmos = true;
#endif
        protected AnimationPlayableOutput _proceduralAnimationOutput;
        
        protected PlayableGraph _playableGraph;
        protected List<AnimationModifierLayer> _modifierLayers = new List<AnimationModifierLayer>();
        protected List<AnimationModifierSettings> _modifiersToLink = new List<AnimationModifierSettings>();

        protected CharacterSkeleton _skeleton;
        protected ModifierJobData _modifierJobData;
        
        protected Animator _animator;
        protected Dictionary<string, AnimationStreamBone> _skeletonMap = new ();

        protected bool _hasQueuedModifiers;
        
        public virtual void QueueModifierUpdates()
        {
            _hasQueuedModifiers = true;
        }

        public virtual void UpdateProceduralAnimation(ProceduralAnimationSettings newSettings)
        {
            if (proceduralSettings == newSettings) return;

            proceduralSettings = newSettings;
            BuildModifierPlayables();
        }

        public virtual void UpdateAnimationModifier(AnimationModifierSettings newModifier)
        {
            if (newModifier == null) return;
            
            // 1. Queue modifier linking if we are changing procedural settings.
            if (_hasQueuedModifiers)
            {
                if (newModifier != null) _modifiersToLink.Add(newModifier);
                return;
            }
            
            Type modifierType = newModifier.GetType();
            
            foreach (var layer in _modifierLayers)
            {
                if (layer.setting.GetType() != modifierType) continue;
                layer.job.OnModifierUpdated(newModifier);
            }
        }

        public virtual void BuildPlayables()
        {
            _playableGraph = _animator.playableGraph;

            var sourcePlayable = AnimationScriptPlayable.Null;
            
            if (_modifierLayers.Count > 0)
            {
                // Re-create playables.
                int count = _modifierLayers.Count;
                for (int i = 0; i < count; i++)
                {
                    var modifierLayer = _modifierLayers[i];
                    modifierLayer.playable = modifierLayer.job.CreatePlayable(_playableGraph);
                    _modifierLayers[i] = modifierLayer;
                }

                // Chain new playables.
                for (int i = 1; i < count; ++i)
                {
                    _modifierLayers[i].playable.AddInput(_modifierLayers[i - 1].playable, 0);
                }

                sourcePlayable = _modifierLayers[count - 1].playable;
            }
            
            _proceduralAnimationOutput = AnimationPlayableOutput.Create(_playableGraph,
                "CAS Procedural Animation Output", _animator);
            _proceduralAnimationOutput.SetWeight(globalWeight);
            
            if (sourcePlayable.IsValid())
            {
                _proceduralAnimationOutput.SetSourcePlayable(sourcePlayable);
            }
            
            _proceduralAnimationOutput.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
        }

        public void InitializePlayableComponent(Animator animator, CharacterSkeleton skeleton)
        {
            _animator = animator;
            if (animator == null)
            {
                Debug.LogError("Procedural Animation Component: Animator not found.");
                return;
            }

            _skeleton = skeleton;
            if (_skeleton == null)
            {
                Debug.LogError("Procedural Animation Component: Character Skeleton not found.");
                return;
            }

            var hierarchy = _skeleton.GetTransformHierarchy().Where(t => t != null).ToArray();
            
            for (int i = 0; i < hierarchy.Length; i++)
            {
                _skeletonMap.TryAdd(hierarchy[i].transform.name, new AnimationStreamBone()
                {
                    index = i,
                    handle = animator.BindStreamTransform(hierarchy[i])
                });
            }
            
            _modifierJobData = new ModifierJobData()
            {
                animator = animator,
                rootHandle = animator.BindSceneTransform(transform),
                skeleton = _skeleton,
                streamBones = _skeletonMap,
                customPropHandles = new Dictionary<string, PropertyStreamHandle>()
            };
            
            BuildPlayables();
            if(proceduralSettings != null) BuildModifierPlayables();
        }

        public AnimationPlayableOutput GetOutput()
        {
            return _proceduralAnimationOutput;
        }

        /// <summary>
        /// Gets the first modifier job matching the requested type. Built-in modifier jobs are structs,
        /// so the returned value is a shallow copy.
        /// Use the returned job for read-only state inspection; do not invoke its lifecycle methods.
        /// </summary>
        public bool TryGetModifierJob<T>(out T job) where T : IAnimationModifierJob
        {
            foreach (var layer in _modifierLayers)
            {
                if (layer.job is T modifierJob)
                {
                    job = modifierJob;
                    return true;
                }
            }

            job = default;
            return false;
        }

        protected virtual void DisposeModifiers()
        {
            if (_proceduralAnimationOutput.IsOutputValid() && _modifierLayers.Count > 0)
            {
                _proceduralAnimationOutput.SetSourcePlayable(Playable.Null);
            }
            
            foreach (var modifierLayer in _modifierLayers)
            {
                modifierLayer.job.Dispose();
            }
            
            _modifierLayers.Clear();
        }
        
        protected virtual void BuildModifierPlayables()
        {
            if (proceduralSettings == null || proceduralSettings.modifiers == null)
            {
                DisposeModifiers();
                return;
            }
            
            List<AnimationModifierLayer> linkModifiers = new List<AnimationModifierLayer>();

            int count = _modifierLayers.Count;
            for (int i = 0; i < count; i++) linkModifiers.Add(_modifierLayers[i]);
            
            _modifierLayers.Clear();
            
            foreach (var setting in proceduralSettings.modifiers)
            {
                int pooledIndex = linkModifiers.FindIndex(0, match => 
                    object.ReferenceEquals(match.setting, setting));

                IAnimationModifierJob job;

                if (pooledIndex >= 0)
                {
                    job = linkModifiers[pooledIndex].job;
                    linkModifiers.RemoveAt(pooledIndex);
                }
                else
                {
                    job = setting.CreateAnimationJob();
                }
                
                job.Initialize(_modifierJobData, setting);

                var playable = job.CreatePlayable(_playableGraph);
                AnimationModifierLayer modifierLayer = new AnimationModifierLayer()
                {
                    job = job,
                    setting = setting,
                    playable = playable
                };

                modifierLayer.weightOverrides = new List<WeightOverride>();
                for (int i = 0; i < setting.weightOverrides.Count; i++)
                {
                    var weightOverride = setting.weightOverrides[i];
                    weightOverride.weight = weightOverride.weight.CreateProperty(gameObject);
                    modifierLayer.weightOverrides.Add(weightOverride);
                }
                
                _modifierLayers.Add(modifierLayer);
            }
            
            // 4. Chain new modifier playables.
            count = _modifierLayers.Count;

            if (count == 0)
            {
                _proceduralAnimationOutput.SetSourcePlayable(Playable.Null);
            }
            else
            {
                for (int i = 1; i < count; ++i)
                {
                    _modifierLayers[i].playable.AddInput(_modifierLayers[i - 1].playable, 0);
                }

                _proceduralAnimationOutput.SetSourcePlayable(_modifierLayers[count - 1].playable);
            }
            
            // 5. Dispose previous modifiers.
            foreach (var modifier in linkModifiers) modifier.job.Dispose();
        }

        private void Awake()
        {
            _skeleton = GetComponentInChildren<CharacterSkeleton>();
        }

        private void Start()
        {
#if UNITY_EDITOR
            SceneView.duringSceneGui += OnSceneGUI;
#endif
        }

        public virtual void PreUpdateProceduralAnimation()
        {
            if (!_playableGraph.IsValid()) return;
            
            int count = _modifierLayers.Count;
            for (int i = 0; i < count; i++) _modifierLayers[i].job.PreUpdateJobData();
        }
        
        public virtual void UpdatePlayableComponent()
        {
            if (!_playableGraph.IsValid()) return;
            if (proceduralSettings == null || proceduralSettings.modifiers == null) return;
            
            int count = _modifierLayers.Count;
            
            for (int i = 0; i < count; i++)
            {
                var modifierLayer = _modifierLayers[i];
                var modifierSettings = proceduralSettings.modifiers[i];
                float weight = modifierSettings.alpha;

                int overrideIndex = 0;
                foreach (var weightOverride in modifierLayer.weightOverrides)
                {
                    weight *= weightOverride.GetWeight(modifierSettings.weightOverrides[overrideIndex].weightModifier);
                    overrideIndex++;
                }

                weight = Mathf.Clamp01(weight);
                modifierLayer.job.UpdateJobData(modifierLayer.playable, weight);
            }
        }

        public void LateUpdatePlayableComponent()
        {
            if (_hasQueuedModifiers)
            {
                _hasQueuedModifiers = false;
                foreach (var setting in _modifiersToLink)
                {
                    UpdateAnimationModifier(setting);
                }
                _modifiersToLink.Clear();
            }
            
            foreach (var modifierLayer in _modifierLayers) modifierLayer.job.LateUpdate();
        }

        protected void OnDestroy()
        {
            DisposeModifiers();
            
#if UNITY_EDITOR
            SceneView.duringSceneGui -= OnSceneGUI;
#endif
        }

        public void SetAsset(ScriptableObject asset)
        {
            if (asset is ProceduralAnimationSettings newSettings) proceduralSettings = newSettings;
        }
        
#if UNITY_EDITOR
        public static CharacterAnimationComponent.CreateAssetDelegate<CharacterAnimationSettings> onCreateAsset;
        
        [ContextMenu("Create Procedural Asset")]
        public void CreateAnimationSettings()
        {
            onCreateAsset?.Invoke($"PA_{gameObject.name}");
        }

        private void OnValidate()
        {
            if (!_proceduralAnimationOutput.IsOutputValid()) return;
            _proceduralAnimationOutput.SetWeight(globalWeight);
        }
        
        protected void OnDrawGizmos()
        {
            if (!showModifierGizmos || proceduralSettings == null || proceduralSettings.modifiers == null) return;
            if (proceduralSettings.modifiers.Count != _modifierLayers.Count) return;

            int index = 0;
            foreach (var layer in _modifierLayers)
            {
                if(proceduralSettings.modifiers[index].showSceneGui) layer.job.OnDrawGizmos();
                index++;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showModifierGizmos || proceduralSettings == null || proceduralSettings.modifiers == null) return;
            if (proceduralSettings.modifiers.Count != _modifierLayers.Count) return;
            
            int index = 0;
            foreach (var layer in _modifierLayers)
            {
                if(proceduralSettings.modifiers[index].showSceneGui) layer.job.OnSceneGUI();
                index++;
            }
        }
#endif
    }
}
