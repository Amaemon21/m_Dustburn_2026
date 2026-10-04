// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [ScriptableComponentGroup("Bone Controls", "Movement Turns")]
    public class PivotModifierSettings : AnimationModifierSettings
    {
        [Header("Inputs")]
        [Tooltip("Bind player movement input here for more accurate results.")]
        public BindableProperty<Vector2> moveInput = new BindableProperty<Vector2>(Vector2.zero);
        
        [Header("Bone References")]
        public KRigElement pelvis = new KRigElement(-1);
        public List<LookLayerElement> spineBones = new List<LookLayerElement>();
        
        [Header("Animation")]
        public AnimationCurve rotationCurve = new AnimationCurve(new []
        {
            new Keyframe(0f, 0f),
            new Keyframe(0.2f, 1f),
            new Keyframe(0.5f, 0f)
        });
        
        public AnimationCurve translationCurve = new AnimationCurve(new []
        {
            new Keyframe(0f, 0f),
            new Keyframe(0.15f, 0.1f),
            new Keyframe(0.5f, 0f)
        });
        
        [Min(0f)] public float rotationSmoothing = 5f;
        [Min(0f)] public float positionSmoothing = 5f;
        
        [Min(0f)] public float playbackSpeed = 1f;
        
        [Header("Leaning")]
        [Range(0, 90f)] public float maxLeanAngle = 20f;
        [Min(0f)] public float leanIntensity = 1f;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new PivotModifierJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            moveInput.Initialize(characterPrefab);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            LookModifierSettings.ApplyAngleDistribution(ref spineBones);
        }
#endif
    }
}