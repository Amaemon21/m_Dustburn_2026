// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables
{
    [AddComponentMenu(CasNames.Path_ComponentMenu + "Simple Animation Player")]
    public class SimpleAnimationPlayer : MonoBehaviour
    {
        [SerializeField] protected AnimationClip defaultClipPose;
        [SerializeField] protected bool useAnimator = true;
        
        protected Animator _animator;
        protected AnimationSlotMixer _slotMixer;
        
        protected bool _isInitialized;
        protected bool _isGraphValid;

        protected bool _isActive;
        protected PlayableGraph _playableGraph;

        public void PlayBasePlayable(AnimationClip clip)
        {
            if (clip == null) return;

            if (_slotMixer.mixer.GetInput(0).IsValid())
            {
                _slotMixer.mixer.DisconnectInput(0);
            }

            var clipPlayable = AnimationClipPlayable.Create(_playableGraph, clip);
            clipPlayable.SetSpeed(0f);
            clipPlayable.SetDuration(clip.length);
            _slotMixer.mixer.ConnectInput(0, clipPlayable, 0, 1f);
        }
        
        public bool PlayAnimation(AnimationAsset newAnimation, float startTime = 0f)
        {
            if (!_isActive || !IsValidAndInitialized() || newAnimation == null || newAnimation.clip == null)
            {
                return false;
            }
            
            _slotMixer.Play(newAnimation, startTime);
            return true;
        }

        protected virtual void Initialize()
        {
            if (_animator == null) return;
            
            BuildPlayable();
            
            _isInitialized = true;
            _isGraphValid = true;
        }

        protected virtual void BuildPlayable()
        {
            if (useAnimator)
            {
                _playableGraph = _animator.playableGraph;
                _slotMixer = new AnimationSlotMixer(_playableGraph, 3, 1);
                var sourcePlayable = _playableGraph.GetOutput(0).GetSourcePlayable();
                _slotMixer.mixer.ConnectInput(0, sourcePlayable, 0, 1f);
            }
            else
            {
                _playableGraph = PlayableGraph.Create("CAS Simple Animation Graph");
                _slotMixer = new AnimationSlotMixer(_playableGraph, 3, 1);
                PlayBasePlayable(defaultClipPose);
            }
            
            var output = AnimationPlayableOutput.Create(_playableGraph, "Animation Player", _animator);
            output.SetSourcePlayable(_slotMixer.mixer);
            
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _playableGraph.Play();
        }
        
        protected bool IsValidAndInitialized()
        {
            return _isInitialized && _isGraphValid;
        }
        
        protected void Awake()
        {
            _animator = GetComponent<Animator>();
            Initialize();
        }

        protected void Update()
        {
            if (_isGraphValid && !_playableGraph.IsValid()) _isGraphValid = false;
            _slotMixer.Update();
        }

        protected void OnDestroy()
        {
            if(!useAnimator) _playableGraph.Destroy();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || _isActive) return;
            
            _isActive = true;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying || !_isActive) return;

            if(_slotMixer.IsActive) _slotMixer.Stop(0f);
            _isActive = false;
        }
    }
}