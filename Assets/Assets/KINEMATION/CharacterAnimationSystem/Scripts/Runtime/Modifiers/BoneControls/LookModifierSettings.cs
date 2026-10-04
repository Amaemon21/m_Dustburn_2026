// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls
{
    [Serializable]
    public struct LookLayerElement
    {
        public KRigElement rigElement;
        public Vector2 clampedAngle;
        [HideInInspector] public Vector2 cachedClampedAngle;
    }
    
    [ScriptableComponentGroup("Bone Controls", "Look Modifier")]
    public class LookModifierSettings : AnimationModifierSettings
    {
        [Tab("Look")]
        [Header("Inputs")]
        [BoundRange(-90f, 90f)] public BindableProperty<float> pitchInput = new (0f);
        [BoundRange(-90f, 90f)] public BindableProperty<float> yawInput = new (0f);
        [Tooltip("Whether to accumulate yaw input every frame.")] public bool useYawAsDelta = false;
        [BoundRange(-90f, 90f)] public BindableProperty<float> rollInput = new (0f);
        
        [Header("Bones")]
        public List<LookLayerElement> pitchOffsetElements = new List<LookLayerElement>();
        public List<LookLayerElement> yawOffsetElements = new List<LookLayerElement>();
        public List<LookLayerElement> rollOffsetElements = new List<LookLayerElement>();
        
        [Tab("Turn In Place")]
        [Header("Turns")]
        [Tooltip("Toggles turning in place.")]
        public bool enableTurnInPlace = true;
        [Range(0f, 90f)] public float angleThreshold = 45f;
        public AnimationCurve turnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Min(0f)] public float turnSpeed = 1.7f;
        
        [Tooltip("Humanoid only. These bone poses will be preserved when turning.")]
        [CustomElementChainDrawer(true, false)]
        public KRigElementChain bonesToPreserve = new KRigElementChain();
        
        [Header("Animator Turn States")]
        public string turnRightState = "TurnRight";
        public string turnLeftState = "TurnLeft";
        
        public static void ApplyAngleDistribution(ref List<LookLayerElement> collection)
        {
            int count = collection.Count;
            int adjustStartIndex = 0;
            
            Vector2 angleToDistribute = Vector2.zero;

            bool bShallDistribute = false;
            bool bDistributeForX = false;
            
            for (int i = 0; i < count; i++)
            {
                var element = collection[i];
                
                angleToDistribute.x += Mathf.Abs(element.clampedAngle.x);
                angleToDistribute.y += Mathf.Abs(element.clampedAngle.y);
                
                if (!Mathf.Approximately(element.cachedClampedAngle.x,element.clampedAngle.x))
                {
                    adjustStartIndex = i + 1;
                    bShallDistribute = true;
                    bDistributeForX = true;
                    break;
                }

                if (!Mathf.Approximately(element.cachedClampedAngle.y, element.clampedAngle.y))
                {
                    adjustStartIndex = i + 1;
                    bShallDistribute = true;
                    break;
                }
            }

            if (bShallDistribute)
            {
                for (int i = adjustStartIndex; i < count; i++)
                {
                    var element = collection[i];

                    if (bDistributeForX)
                    {
                        element.clampedAngle.x = (90f - angleToDistribute.x) / (count - adjustStartIndex);
                    }
                    else
                    {
                        element.clampedAngle.y = (90f - angleToDistribute.y) / (count - adjustStartIndex);
                    }
                    
                    collection[i] = element;
                }
            }
            
            for (int i = 0; i < count; i++)
            {
                var element = collection[i];
                element.cachedClampedAngle.x = element.clampedAngle.x;
                element.cachedClampedAngle.y = element.clampedAngle.y;
                collection[i] = element;
            }
        }

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new LookModifierJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            
            pitchInput.Initialize(characterPrefab);
            yawInput.Initialize(characterPrefab);
            rollInput.Initialize(characterPrefab);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyAngleDistribution(ref pitchOffsetElements);
            ApplyAngleDistribution(ref yawOffsetElements);
            ApplyAngleDistribution(ref rollOffsetElements);
        }
#endif
    }
}