// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.BoneLookups;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    public class LayeredBlendingSetupWidget
    {
        public Action onButtonPressed;
        public Action onPresetChanged;
        public int index = 0;

        private LayeredBlendingComponent _layeredBlending;
        private CharacterSkeleton _skeleton;
        
        private bool _showConfirmation;
        private string[] _options;
        private List<CasBoneLookupPreset> _presets;

        public LayeredBlendingSetupWidget(bool showConfirmation = false, 
            LayeredBlendingComponent layeredBlending = null, List<CasBoneLookupPreset> bonePresets = null)
        {
            _layeredBlending = layeredBlending;
            _skeleton = layeredBlending == null
                ? null
                : layeredBlending.GetComponentInChildren<CharacterSkeleton>();
            _presets = bonePresets ?? BoneLookupUtility.GetBoneLookupPresets(_skeleton);
            _options = _presets.Select(preset => preset.GetName()).ToArray();
            _showConfirmation = showConfirmation;
        }

        public CasBoneLookupPreset GetSelectedPreset()
        {
            return _presets[index];
        }
        
        private void AddCompositeChain(string chainName, string property, CasBoneLookupPreset preset, 
            Func<CasBoneLookupPreset, CasBoneNameLookups> lookupSelector)
        {
            var layeredChains = new List<KRigElementChain>();
            
            foreach (var chain in _skeleton.ElementChains)
            {
                if (!BoneLookupUtility.IsNameMatching(chain.chainName, lookupSelector(preset))) continue;
                layeredChains.Add(chain);
            }
            
            BindableProperty<float> baseWeight = new BindableProperty<float>(0f);
            BindableProperty<float> additiveWeight = new BindableProperty<float>(0f);
            BindableProperty<float> localWeight = new BindableProperty<float>(0f);

            baseWeight.Bind(_layeredBlending, property + ".baseWeight");
            additiveWeight.Bind(_layeredBlending, property + ".additiveWeight");
            localWeight.Bind(_layeredBlending, property + ".localWeight");

            // add final layered blend
            _layeredBlending.layeredBlends.Add(new LayeredBlend()
            {
                layer = AnimationLayeringUtility.MergeChains(chainName, layeredChains),
                baseWeight = baseWeight,
                additiveWeight = additiveWeight,
                localWeight = localWeight
            });
        }

        public void CreateBoneChains(LayeredBlendingComponent layeredBlending)
        {
            if (layeredBlending == null)
            {
                Debug.LogWarning("Layered Blending Setup: Layered Blending Component is null!");
                return;
            }

            _layeredBlending = layeredBlending;
            _skeleton = layeredBlending.GetComponentInChildren<CharacterSkeleton>();
            
            if (_skeleton == null)
            {
                Debug.LogWarning("Layered Blending Setup: Skeleton not found!");
                return;
            }
            
            layeredBlending.layeredBlends.Clear();
            CasBoneLookupPreset lookupPreset = GetSelectedPreset();
            
            AddCompositeChain("LowerBody", nameof(layeredBlending.Layering_LowerBody), lookupPreset, 
                preset => preset.GetLowerBodyLookups());
            AddCompositeChain("Spine", nameof(layeredBlending.Layering_Spine), lookupPreset, 
                preset => preset.GetSpineLookups());
            AddCompositeChain("Head", nameof(layeredBlending.Layering_Head), lookupPreset, 
                preset => preset.GetHeadLookups());
            AddCompositeChain("RightArm", nameof(layeredBlending.Layering_Arm_R), lookupPreset, 
                preset => preset.GetRightArmLookups());
            AddCompositeChain("LeftArm", nameof(layeredBlending.Layering_Arm_L), lookupPreset, 
                preset => preset.GetLeftArmLookups());
            AddCompositeChain("Fingers", nameof(layeredBlending.Layering_Fingers), lookupPreset, 
                preset => preset.GetFingersLookups());
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Character Model", KEditorUtility.boldLabel);
            
            int prevIndex = index;
            index = EditorGUILayout.Popup("Skeleton Type", index, _options);
            if (prevIndex != index) onPresetChanged?.Invoke();

            if (_showConfirmation)
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("OK"))
                {
                    CreateBoneChains(_layeredBlending);
                    onButtonPressed?.Invoke();
                }

                if (GUILayout.Button("Cancel"))
                {
                    onButtonPressed?.Invoke();
                }

                EditorGUILayout.EndHorizontal();
            }
        }
    }
    
    public class LayeredBlendingSetupWindow : EditorWindow
    {
        private LayeredBlendingSetupWidget _widget;
        private static readonly string SessionSaveKey = "LayeredBlendingSetupWindow:tool";
        
        public static void ShowWindow(LayeredBlendingComponent layeredBlending)
        {
            var window = CreateInstance<LayeredBlendingSetupWindow>();
            window._widget = new LayeredBlendingSetupWidget(true, layeredBlending);
            window._widget.onButtonPressed = window.Close;

            string json = SessionState.GetString(SessionSaveKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                window._widget.index = JsonUtility.FromJson<int>(json);
            }
            
            window.titleContent = new GUIContent("Layered Blending Setup");
            window.maxSize = new Vector2(400f, 100f);
            
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(CasEditorUtility.windowUtilityStyle);
            int prevIndex = _widget.index;
            _widget.OnGUI();

            if (prevIndex != _widget.index)
            {
                var json = JsonUtility.ToJson(_widget.index);
                SessionState.SetString(SessionSaveKey, json);
            }
            
            EditorGUILayout.EndVertical();
        }
    }
    
    public struct AnimatedPropertyVector
    {
        public string name;
        public List<SerializedProperty> properties;
    }
    
    [CustomEditor(typeof(LayeredBlendingComponent), true)]
    public class LayeredBlendingEditor : UnityEditor.Editor
    {
        private List<AnimatedPropertyVector> _propertyVectors = new ();
        private GUIStyle _boxStyle;
        private GUIStyle _boldFoldoutStyle;
        private bool _showProperties;

        private static LayeredBlendingComponent _layeredBlending;
        private GUIStyle _buttonStyle;
        
        private void OnEnable()
        {
            _layeredBlending = target as LayeredBlendingComponent;
            
            if (!Application.isPlaying) return;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var type = target.GetType();
            var fields = type.GetFields(flags);

            foreach (var field in fields)
            {
                if (field.FieldType == typeof(LayeringVectorParameter))
                {
                    SerializedProperty property = serializedObject.FindProperty(field.Name);
                    
                    _propertyVectors.Add(new AnimatedPropertyVector()
                    {
                        name = field.Name,
                        properties = new List<SerializedProperty>
                        {
                            property.FindPropertyRelative("baseWeight"),
                            property.FindPropertyRelative("additiveWeight"),
                            property.FindPropertyRelative("localWeight"),
                        }
                    });
                    continue;
                }

                if (field.FieldType != typeof(LayeringFloatParameter)) continue;
                
                _propertyVectors.Add(new AnimatedPropertyVector()
                {
                    name = field.Name,
                    properties = new List<SerializedProperty>
                    {
                        serializedObject.FindProperty(field.Name).FindPropertyRelative("value")
                    }
                });
            }
            
            _boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(5, 0, 5, 5)
            };
            
            _boldFoldoutStyle = new GUIStyle(EditorStyles.foldout);
            _boldFoldoutStyle.fontStyle = FontStyle.Bold;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space();
                
                if (GUILayout.Button("Generate Layered Blends"))
                {
                    LayeredBlendingSetupWindow.ShowWindow(_layeredBlending);
                }
                return;
            }

            _showProperties = EditorGUILayout.Foldout(_showProperties, "Show Layering Values", 
                true, _boldFoldoutStyle);
            if (!_showProperties) return;
            
            foreach (var propertyVector in _propertyVectors)
            {
                EditorGUILayout.LabelField(propertyVector.name);
                
                EditorGUILayout.BeginVertical(_boxStyle);

                GUI.enabled = false;
                foreach (var prop in propertyVector.properties)
                {
                    EditorGUILayout.FloatField(prop.name, prop.floatValue);
                }
                GUI.enabled = true;
                
                EditorGUILayout.EndVertical();
            }
        }
    }
}