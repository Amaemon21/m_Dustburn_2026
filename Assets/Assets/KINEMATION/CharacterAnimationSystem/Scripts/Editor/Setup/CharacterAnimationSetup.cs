// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.BoneLookups;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Setup
{
    public struct CharacterAnimationSetupData
    {
        public GameObject characterModel;
        public CharacterSkeleton skeleton;
        public CharacterAnimationComponent characterAnimation;
        public LayeredBlendingComponent layeredBlending;
        public int defaultBoneLookupIndex;
        public List<CasBoneLookupPreset> lookupPresets;
    }
    
    public class CharacterAnimationSetupWindow : EditorWindow
    {
        private const string SetupCompleteTitle = "Character Animation Setup";
        private const string SetupCompleteMessage = "Setup complete.";

        public CharacterAnimationSetupData setupData;
        
        private List<Type> _presetTypes;
        private string[] _presetNameOptions;
        private int _selectedPresetIndex = -1;

        private CasSetupPreset _activePreset;
        private Vector2 _scrollPosition;
        
        private void OnPresetSelected()
        {
            _activePreset = Activator.CreateInstance(_presetTypes[_selectedPresetIndex]) as CasSetupPreset;
            if (_activePreset == null) return;

            _activePreset.setupData = setupData;
            _activePreset.Initialize();
        }

        private void OnEnable()
        {
            var foundPresetTypes = TypeCache.GetTypesDerivedFrom<CasSetupPreset>().ToList();
            _presetTypes = new List<Type>();
            
            List<string> typeNames = new List<string>();
            
            for (int i = foundPresetTypes.Count - 1; i >= 0; i--)
            {
                var presetType = foundPresetTypes[i];
                string typeName = presetType.Name;

                var attributes = presetType.GetCustomAttributes(true);
                foreach (var attribute in attributes)
                {
                    var presetAttribute = attribute as CustomSetupNameAttribute;
                    if (presetAttribute == null) continue;

                    typeName = presetAttribute.presetName;
                    break;
                }

                typeNames.Add(typeName);
                _presetTypes.Add(presetType);
            }
            
            _presetNameOptions = typeNames.ToArray();
        }

        private void ApplyPreset()
        {
            if (_activePreset == null) return;
            
            GameObject model = setupData.characterModel;

            setupData.characterAnimation =
                CharacterAnimationSetup.AddOrGetComponent<CharacterAnimationComponent>(model);
            CharacterAnimationSetup.AddOrGetComponent<ProceduralAnimationComponent>(model);

            LayeredBlendingComponent layeredBlending = model.GetComponent<LayeredBlendingComponent>();
            if (layeredBlending == null)
            {
                layeredBlending = model.AddComponent<LayeredBlendingComponent>();
            }
            
            if (_activePreset != null)
            {
                setupData.layeredBlending = layeredBlending;
                _activePreset.setupData = setupData;
                _activePreset.Apply();
                ShowSetupCompletedPopup();
            }
        }

        private void ShowSetupCompletedPopup()
        {
            Close();
            EditorUtility.DisplayDialog(SetupCompleteTitle, SetupCompleteMessage, "OK");
        }

        private void OnPresetGUI()
        {
            if (_activePreset != null || _selectedPresetIndex == 0)
            {
                EditorGUILayout.Space();
                
                bool isValid = _activePreset?.Validate() ?? true;
                GUI.enabled = isValid;

                if (isValid) EditorGUILayout.HelpBox("Good to go!", MessageType.Info);

                if (GUILayout.Button("Apply Preset")) ApplyPreset();
                GUI.enabled = true;

                return;
            }
            
            EditorGUILayout.HelpBox("Select one of the presets.", MessageType.Info);
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, CasEditorUtility.windowUtilityStyle);
            
            int prevPreset = _selectedPresetIndex;
            _selectedPresetIndex = EditorGUILayout.Popup("Preset", _selectedPresetIndex, _presetNameOptions);

            if (prevPreset != _selectedPresetIndex)
            {
                OnPresetSelected();
            }
            
            OnPresetGUI();
            
            EditorGUILayout.EndScrollView();
        }
    }
    
    public class CharacterAnimationSetup
    {
        private static Transform FindRootBone(GameObject gameObject)
        {
            if(gameObject == null) return null;
            
            Transform transform = gameObject.transform;
            Transform rootBone = null;
            SkinnedMeshRenderer[] meshes = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();

            if (meshes.Length > 0)
            {
                foreach (var mesh in meshes)
                {
                    if (rootBone == null || rootBone.IsChildOf(mesh.rootBone)) rootBone = mesh.rootBone;
                }
            }

            if (rootBone != null)
            {
                while (rootBone.parent != transform) rootBone = rootBone.parent;
                return rootBone;
            }
            
            for (int i = 0; i < transform.childCount; i++)
            {
                var childName = transform.GetChild(i).name.ToLower();
                if (childName.Contains("skeleton") || childName.Contains("root") || childName.Contains("armature") 
                    || childName.Contains("hip"))
                {
                    rootBone = transform.GetChild(i);
                    break;
                }
            }

            return rootBone;
        }

        public static T AddOrGetComponent<T>(GameObject gameObject) where T : Component
        {
            var component = gameObject.GetComponent<T>();
            if (component == null) component = gameObject.AddComponent<T>();
            return component;
        }
        
        [MenuItem("GameObject/KINEMATION/Setup CAS", true)]
        private static bool ValidateCharacterAnimationSetup()
        {
            var gameObject = Selection.activeObject as GameObject;
            return gameObject != null;
        }

        [MenuItem("GameObject/KINEMATION/Setup CAS", false, 0)]
        private static void PerformCharacterAnimationSetup()
        {
            Transform rootBone = FindRootBone(Selection.activeObject as GameObject);
            if (rootBone == null) return;

            GameObject characterModel = rootBone.parent == null ? rootBone.gameObject : rootBone.parent.gameObject;
            
            CharacterAnimationSetupData setupData = new CharacterAnimationSetupData
            {
                characterModel = characterModel,
                skeleton = AddOrGetComponent<CharacterSkeleton>(rootBone.gameObject)
            };
            
            setupData.skeleton.UpdateSkeleton();
            setupData.lookupPresets = BoneLookupUtility.GetBoneLookupPresets(setupData.skeleton);
            setupData.defaultBoneLookupIndex = BoneLookupUtility.GetDefaultBoneLookupIndex(
                setupData.skeleton, setupData.lookupPresets);
            
            var window = EditorWindow.CreateWindow<CharacterAnimationSetupWindow>();
            window.minSize = new Vector2(400f, 420f);
            window.setupData = setupData;
            window.titleContent.text = "Character Animation Setup";
            window.Show();
        }
    }
}