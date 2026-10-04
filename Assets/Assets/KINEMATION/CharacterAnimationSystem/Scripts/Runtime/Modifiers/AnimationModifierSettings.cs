// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers
{
    [Serializable]
    public struct WeightModifier
    {
        public bool isMask;
        [Min(0f)] public float inRangeMin;
        [Min(0f)] public float inRangeMax;

        public WeightModifier(float inRangeMin, float inRangeMax, bool isMask)
        {
            this.isMask = isMask;
            this.inRangeMin = inRangeMin;
            this.inRangeMax = inRangeMax;
        }

        public float ProcessWeight(float weight)
        {
            if (!Mathf.Approximately(inRangeMin, inRangeMax))
            {
                weight = Mathf.Lerp(0f, 1f, (weight - inRangeMin) / (inRangeMax - inRangeMin));
            }

            weight = Mathf.Clamp01(weight);
            return isMask ? 1f - weight : weight;
        }
    }
    
    [Serializable]
    public struct WeightOverride
    {
        [BoundRange(0f, 1f), BindAnimator] public BindableProperty<float> weight;
        public WeightModifier weightModifier;
        
        public float GetWeight(WeightModifier modifier)
        {
            float weightValue = weight.GetValue();
            
            if (!Mathf.Approximately(modifier.inRangeMin, modifier.inRangeMax))
            {
                weightValue = Mathf.Lerp(0f, 1f, 
                    (weightValue - modifier.inRangeMin) / (modifier.inRangeMax - modifier.inRangeMin));
            }

            weightValue = Mathf.Clamp01(weightValue);
            return modifier.isMask ? 1f - weightValue : weightValue;
        }
    }
    
    public class AnimationModifierSettings : ScriptableObject, IRigProvider, IBindableContext
    {
        protected const string ModifiersMenuPath = "KINEMATION/CAS/Modifiers/";
        
        [ShowStandalone] public GameObject characterPrefab;
        [Range(0f, 1f)] public float alpha = 1f;
        [Unfold] public List<WeightOverride> weightOverrides = new List<WeightOverride>();
        
        public virtual IAnimationModifierJob CreateAnimationJob()
        {
            return null;
        }

        public virtual void InitializeOnLoad()
        {
            int count = weightOverrides.Count;
            for (int i = 0; i < count; i++)
            {
                var weightOverride = weightOverrides[i];
                weightOverride.weight.Initialize(characterPrefab);
                weightOverrides[i] = weightOverride;
            }
        }
        
        public GameObject GetContext()
        {
            return characterPrefab;
        }
        
        public KRigElement[] GetHierarchy()
        {
            if (characterPrefab == null)
            {
                Debug.LogWarning($"{name}: Character Prefab is null!");
                return null;
            }

            var rigProvider = characterPrefab.GetComponentInChildren<IRigProvider>();
            if (rigProvider == null)
            {
                Debug.LogWarning($"{name}: Character Prefab has no {nameof(IRigProvider)} in children!");
                return null;
            }

            return rigProvider.GetHierarchy();
        }
        
#if UNITY_EDITOR
        [HideInInspector] public bool showSceneGui;
#endif
    }
}