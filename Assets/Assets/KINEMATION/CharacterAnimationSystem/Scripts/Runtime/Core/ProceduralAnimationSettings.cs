// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core
{
    [CreateAssetMenu(fileName = "NewProceduralSettings", menuName = "KINEMATION/CAS/Procedural Animation")]
    public class ProceduralAnimationSettings : ScriptableObject, IRigProvider
    {
        [Tooltip("Player prefab.")]
        public GameObject characterPrefab;
        [HideInInspector] public List<AnimationModifierSettings> modifiers = new ();
        
        public static Action onInitialized;

        private void OnInitializeOnLoad()
        {
            foreach (var modifier in modifiers) modifier.InitializeOnLoad();
        }

        private void OnEnable()
        {
            onInitialized += OnInitializeOnLoad;
        }
        
        private void OnDisable()
        {
            onInitialized -= OnInitializeOnLoad;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            onInitialized?.Invoke();
        }
        
#if UNITY_EDITOR
        private GameObject _cachedPrefab;

        public void UpdateCharacterPrefab()
        {
            foreach (var modifier in modifiers) modifier.characterPrefab = characterPrefab;
        }
        
        private void OnValidate()
        {
            if (_cachedPrefab == characterPrefab) return;
           
            UpdateCharacterPrefab();
            
            _cachedPrefab = characterPrefab;
        }
        
        [ContextMenu("Resolve Property Bindings", false, 0)]
        public void CreateAnimationSettings()
        {
            //todo: implement property resolve mechanism here
        }
#endif
        
        public KRigElement[] GetHierarchy()
        {
            if (characterPrefab == null) return null;

            CharacterSkeleton skeleton = characterPrefab.GetComponentInChildren<CharacterSkeleton>();
            if (skeleton == null) return null;
            
            return skeleton.GetHierarchy();
        }
    }
}