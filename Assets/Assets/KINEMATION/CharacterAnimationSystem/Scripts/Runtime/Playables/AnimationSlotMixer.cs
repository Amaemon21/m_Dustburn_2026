// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    [Serializable]
    public struct BlendTime
    {
        public EaseMode easeMode;
        [Min(0f)] public float blendInTime;
        [Min(0f)] public float blendOutTime;

        public BlendTime(float blendInTime, float blendOutTime, EaseMode easeMode)
        {
            this.blendInTime = blendInTime;
            this.blendOutTime = blendOutTime;
            this.easeMode = easeMode;
        }
    }

    public delegate void AnimationTimeEventDelegate(AnimationAsset animationAsset, float normalizedTime);

    public struct AnimationMixerEvent
    {
        public float normalizedTime;
        public AnimationTimeEventDelegate callback;

        public float cachedAbsoluteTime;
        public bool hasTriggered;

        public AnimationMixerEvent(AnimationTimeEventDelegate callback, float normalizedTime)
        {
            this.normalizedTime = normalizedTime;
            this.callback = callback;
            cachedAbsoluteTime = 0f;
            hasTriggered = false;
        }
    }
    
    public struct AnimationSlotPlayable
    {
        public AnimationClipPlayable playable;
        public BlendTime blendTime;
        public float cachedWeight;
        public bool autoBlendOut;

        public AnimationSlotPlayable(PlayableGraph graph, AnimationClip clip)
        {
            playable = AnimationClipPlayable.Create(graph, clip);
            blendTime = new BlendTime(0f, 0f, new EaseMode(EEaseFunc.Linear));
            cachedWeight = 0f;
            autoBlendOut = true;
        }
        
        public float GetLength()
        {
            return playable.IsValid() ? playable.GetAnimationClip().length : 0f;
        }

        public bool IsValid()
        {
            return playable.IsValid();
        }

        public void Release()
        {
            if (playable.IsValid()) playable.Destroy();
        }
    }
    
    public struct AnimationSlotMixer
    {
        public AnimationLayerMixerPlayable mixer;

        public float BlendInWeight { get; private set; }
        public float BlendOutWeight { get; private set; }
        public float MixerWeight { get; private set; }
        public bool IsActive => _isMixerActive;

        private float _cachedMixerWeight;

        private BlendTime _blendTime;
        private float _startTime;
        private float _endTime;
        private float _clipLength;

        private AnimationAsset _activeAnimation;
        private List<AnimationMixerEvent> _customEvents;
        private int _pendingEventCount;
        
        private int _activeIndex;
        
        private List<AnimationSlotPlayable> _playables;

        private bool _isMixerActive;
        private bool _blendingIn;
        private bool _autoBlendOut;

        private int _layerLevel;

        private PlayableGraph _graph;
        
        public AnimationSlotMixer(PlayableGraph graph, int inputCount, int layerLevel = 0)
        {
            _graph = graph;
            mixer = AnimationLayerMixerPlayable.Create(graph, inputCount + layerLevel);
            
            _playables = new List<AnimationSlotPlayable>();
            for (int i = 0; i < inputCount; i++)
            {
                _playables.Add(new AnimationSlotPlayable());
            }
            
            _activeIndex = -1;
            BlendInWeight = BlendOutWeight = MixerWeight = _cachedMixerWeight = 0f;
            
            _blendTime = new BlendTime(0f, 0f, new EaseMode(EEaseFunc.Linear));
            _startTime = _endTime = 0f;
            _clipLength = 0f;
            _activeAnimation = null;
            _customEvents = null;
            _pendingEventCount = 0;
            
            _isMixerActive = _blendingIn = _autoBlendOut = false;
            _layerLevel = layerLevel;
        }
        
        public void Play(AnimationAsset newAnimation, float startTime = 0f)
        {
            Play(newAnimation, startTime, null);
        }

        public void Play(AnimationAsset newAnimation, float startTime, AnimationMixerEvent[] customEvents)
        {
            BlendTime blendTime = newAnimation.blendTime;
            _startTime = 0f;
            if(startTime > 0f) _startTime = startTime;

            AnimationSlotPlayable animSlotPlayable = new AnimationSlotPlayable(_graph, newAnimation.clip)
            {
                blendTime = blendTime,
                autoBlendOut = newAnimation.autoBlendOut
            };

            animSlotPlayable.playable.SetTime(startTime);
            animSlotPlayable.playable.SetSpeed(newAnimation.playRate);

            if (newAnimation.clip.isHumanMotion)
            {
                animSlotPlayable.playable.SetApplyFootIK(false);
                animSlotPlayable.playable.SetApplyPlayableIK(false);
            }
            
            // Prepare the new playable and curves.
            UpdateActiveIndex();
            
            _playables[_activeIndex - _layerLevel] = animSlotPlayable;
            
            // Connect new playable.
            mixer.ConnectInput(_activeIndex, animSlotPlayable.playable, 0, 0f);

            AvatarMask mask = newAnimation.mask == null ? new AvatarMask() : newAnimation.mask;
            mixer.SetLayerMaskFromAvatarMask((uint) _activeIndex, mask);
            mixer.SetLayerAdditive((uint) _activeIndex, newAnimation.isAdditive);

            // Initialize blending properties.
            _blendTime = animSlotPlayable.blendTime;
            _clipLength = animSlotPlayable.GetLength();
            _endTime = _clipLength;
            BlendInWeight = BlendOutWeight = 0f;
            
            _blendingIn = _isMixerActive = true;
            _autoBlendOut = animSlotPlayable.autoBlendOut;

            _cachedMixerWeight = MixerWeight;
            _activeAnimation = newAnimation;
            CacheCustomEvents(customEvents, startTime);
        }
        
        public void Update()
        {
            if (!_isMixerActive)
            {
                return;
            }

            UpdateCustomEvents();
            
            if (_blendingIn)
            {
                BlendInPlayable();
                return;
            }

            if (!_autoBlendOut) return;
            BlendOutPlayable();
        }

        public void Stop(float blendOutTime)
        {
            if (!_isMixerActive) return;
            
            _blendingIn = false;
            _autoBlendOut = true;
            
            _blendTime.blendOutTime = blendOutTime;
            _endTime = GetPlayingTime();

            if (_activeIndex == _layerLevel) return;
            
            //If we have inactive playables, cache their weights.
            for (int i = _layerLevel; i < _activeIndex; i++)
            {
                var inactivePlayable = _playables[i - _layerLevel];
                inactivePlayable.cachedWeight = mixer.GetInputWeight(i);
                _playables[i - _layerLevel] = inactivePlayable;
            }
        }

        private void CacheCustomEvents(AnimationMixerEvent[] customEvents, float startTime)
        {
            ResetCustomEvents();
            if (customEvents == null || customEvents.Length == 0)
            {
                return;
            }

            if (_customEvents == null)
            {
                _customEvents = new List<AnimationMixerEvent>(customEvents.Length);
            }

            float eventStartTime = Mathf.Max(0f, startTime);
            for (int i = 0; i < customEvents.Length; i++)
            {
                var customEvent = customEvents[i];
                if (customEvent.callback == null) continue;

                customEvent.cachedAbsoluteTime = Mathf.Clamp01(customEvent.normalizedTime) * _clipLength;
                customEvent.hasTriggered = customEvent.cachedAbsoluteTime < eventStartTime;

                _customEvents.Add(customEvent);
                if (!customEvent.hasTriggered) _pendingEventCount++;
            }
        }

        private void ResetCustomEvents()
        {
            _pendingEventCount = 0;
            if (_customEvents != null) _customEvents.Clear();
        }

        private void UpdateCustomEvents()
        {
            if (_customEvents == null || _pendingEventCount == 0) return;

            float playingTime = GetPlayingTime();
            float normalizedTime = GetNormalizedTime();

            for (int i = 0; i < _customEvents.Count; i++)
            {
                var customEvent = _customEvents[i];
                if (customEvent.hasTriggered) continue;
                if (customEvent.cachedAbsoluteTime > _endTime) continue;
                if (playingTime < customEvent.cachedAbsoluteTime) continue;

                customEvent.hasTriggered = true;
                _customEvents[i] = customEvent;
                _pendingEventCount--;

                customEvent.callback?.Invoke(_activeAnimation, normalizedTime);
                if (_pendingEventCount == 0) return;
            }
        }

        private float GetPlayingTime()
        {
            return GetActivePlayable().IsValid() ? (float) GetActivePlayable().playable.GetTime() : 0f;
        }

        private float GetNormalizedTime()
        {
            if (Mathf.Approximately(_clipLength, 0f)) return 0f;
            return Mathf.Clamp01(GetPlayingTime() / _clipLength);
        }

        private AnimationSlotPlayable GetActivePlayable()
        {
            return _playables[_activeIndex - _layerLevel];
        }

        private void UpdateActiveIndex()
        {
            if (_activeIndex == -1)
            {
                _activeIndex = _layerLevel;
                return;
            }
            
            // Try to use the next slot
            if (_activeIndex + 1 < mixer.GetInputCount())
            {
                _activeIndex++;
                // Save current weights
                for (int i = _layerLevel; i < _activeIndex; i++)
                {
                    var clip = _playables[i - _layerLevel];
                    clip.cachedWeight = mixer.GetInputWeight(i);
                    _playables[i - _layerLevel] = clip;
                }
                return;
            }

            _playables[0].Release();
            mixer.DisconnectInput(_layerLevel);
            // Reconnect
            for (int i = _layerLevel; i < mixer.GetInputCount() - 1; i++)
            {
                float inputWeight = mixer.GetInputWeight(i + 1);
                AnimationSlotPlayable clip = _playables[i + 1 - _layerLevel];
                clip.cachedWeight = inputWeight;
                _playables[i - _layerLevel] = clip;
                
                var source = mixer.GetInput(i + 1);
                mixer.DisconnectInput(i + 1);
                mixer.ConnectInput(i, source, 0, inputWeight);
            }
            
            _activeIndex = mixer.GetInputCount() - 1;
            mixer.DisconnectInput(_activeIndex);
        }
        
        private void BlendOutInactive()
        {
            for (int i = _layerLevel; i < _activeIndex; i++)
            {
                if (!_blendingIn)
                {
                    mixer.DisconnectInput(i);
                    _playables[i - _layerLevel].Release();
                    continue;
                }

                float weight = _playables[i - _layerLevel].cachedWeight;
                weight = Mathf.Lerp(weight, 0f, BlendInWeight);
                mixer.SetInputWeight(i, weight);
            }
        }
        
        private void BlendInPlayable()
        {
            float alpha = 1f;
            if (!Mathf.Approximately(_blendTime.blendInTime, 0f))
            {
                alpha = (GetPlayingTime() - _startTime) / _blendTime.blendInTime;
            }
            
            BlendInWeight = Mathf.Clamp01(alpha);
            MixerWeight = Mathf.Lerp(_cachedMixerWeight, 1f, BlendInWeight);
            
            mixer.SetInputWeight(_activeIndex, BlendInWeight);

            if (Mathf.Approximately(BlendInWeight, 1f))
            {
                _blendingIn = false;
                _cachedMixerWeight = MixerWeight;
            }
            
            BlendOutInactive();
        }

        private void BlendOutPlayable()
        {
            if (GetPlayingTime() < _endTime) return;
            
            float alpha = 1f;
            if (!Mathf.Approximately(_blendTime.blendOutTime, 0f))
            {
                alpha = (GetPlayingTime() - _endTime) / _blendTime.blendOutTime;
            }
            
            BlendOutWeight = Mathf.Clamp01(alpha);
            MixerWeight = Mathf.Lerp(_cachedMixerWeight, 0f, BlendOutWeight);
            mixer.SetInputWeight(_activeIndex, Mathf.Lerp(BlendInWeight, 0f, BlendOutWeight));
            
            // In case of force blending out.
            if (_activeIndex > _layerLevel)
            {
                for (int i = _layerLevel; i < _activeIndex; i++)
                {
                    float cache = _playables[i - _layerLevel].cachedWeight;
                    float inactiveWeight = Mathf.Lerp(cache, 0f, BlendOutWeight);
                    mixer.SetInputWeight(i, inactiveWeight);
                }
            }

            if (!Mathf.Approximately(BlendOutWeight, 1f)) return;

            for (int i = _layerLevel; i <= _activeIndex; i++)
            {
                mixer.DisconnectInput(i);
                _playables[i - _layerLevel].Release();
            }
            
            _activeIndex = -1;
            BlendOutWeight = 1f;
            
            _isMixerActive = false;
            _activeAnimation = null;
            _clipLength = 0f;
            ResetCustomEvents();
        }
    }
}