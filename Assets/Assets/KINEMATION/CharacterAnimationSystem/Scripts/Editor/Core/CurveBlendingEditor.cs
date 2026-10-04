// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Editor.CurveImporter;
using KINEMATION.Shared.PropertyBindings.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    public struct FloatBlendParameter
    {
        public string name;
        public string path;
        public float value;
        public bool isActive;
        public bool useSlider;
        public bool useCurve;
    }

    public struct VectorBlendParameter
    {
        public string name;
        public List<FloatBlendParameter> parameters;
        public Type targetType;
    }

    public struct CurveBlendingEditorState
    {
        public AnimationClip clip;
        public Vector2 scrollPosition;
        public GameObject characterPrefab;
        public RuntimeAnimatorController controller;
    }
    
    public class CurveBlendingEditor : EditorWindow
    {
        private static readonly string SessionSaveKey = "CurveBlendingEditor:tool";
        private static readonly Type LayeredBlendingType = typeof(LayeredBlendingComponent);
        private static IReadOnlyCollection<CustomEditorCurveData> _copiedCurveProperties;
        private static readonly List<string> HumanoidCurvePostfixes = new List<string>()
        {
            "Jaw Close",
            " Stretch",
            " Stretched",
            "Front-Back",
            "Left-Right",
            " In-Out",
            " Down-Up",
            " Up-Down",
            " Spread",
            ".Spread"
        };
        
        private AnimationClip _clip;
        private Vector2 _scrollPosition;

        private List<VectorBlendParameter> _blendVectors = new List<VectorBlendParameter>();
        
        private AdvancedDropdownState _dropdownState = new AdvancedDropdownState();
        private BindableSearchData _searchData;
        private Rect _buttonRect;
        
        private GameObject _characterPrefab;
        private RuntimeAnimatorController _controller;
        
        private bool IsAnimatorInternalCurve(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return false;
            
            if (binding.propertyName.StartsWith("RootT") || binding.propertyName.StartsWith("RootQ"))
            {
                return true;
            }

            foreach (var postfix in HumanoidCurvePostfixes)
            {
                if (binding.propertyName.EndsWith(postfix)) return true;
            }

            return binding.propertyName.EndsWith(".x") || binding.propertyName.EndsWith(".y")
                                                       || binding.propertyName.EndsWith(".z")
                                                       || binding.propertyName.EndsWith(".w");
        }
        
        private void OnEnable()
        {
            string json = SessionState.GetString(SessionSaveKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            
            CurveBlendingEditorState state = JsonUtility.FromJson<CurveBlendingEditorState>(json);
            _characterPrefab = state.characterPrefab;
            _controller = state.controller;
            _scrollPosition = state.scrollPosition;
            _clip = state.clip;
            InitializeParameters(_clip);
        }

        private void InitializeParameters(AnimationClip clip)
        {
            _blendVectors.Clear();
            if (clip == null) return;

            _clip = clip;

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var fields = LayeredBlendingType.GetFields(flags);

            foreach (var field in fields)
            {
                if (field.FieldType == typeof(LayeringVectorParameter))
                {
                    var layerName = field.Name;
                    var weights = field.FieldType.GetFields(flags);

                    VectorBlendParameter vectorBlend = new VectorBlendParameter()
                    {
                        name = layerName,
                        parameters = new List<FloatBlendParameter>(),
                        targetType = LayeredBlendingType
                    };

                    foreach (var weight in weights)
                    {
                        FloatBlendParameter param = new FloatBlendParameter();
                        param.name = $"{weight.Name}";
                        param.path = $"{layerName}.{param.name}";

                        var curve = GetCurve(param.path, LayeredBlendingType);

                        if (curve != null)
                        {
                            param.value = curve.Evaluate(0f);
                            param.isActive = true;
                            param.useSlider = param.value >= 0f && param.value <= 1f;
                        }
                        else
                        {
                            param.value = 0f;
                            param.isActive = false;
                        }

                        vectorBlend.parameters.Add(param);
                    }

                    _blendVectors.Add(vectorBlend);
                    continue;
                }

                if (field.FieldType != typeof(LayeringFloatParameter)) continue;

                FloatBlendParameter parameter = new FloatBlendParameter()
                {
                    name = "value",
                    path = $"{field.Name}.value",
                    value = 0f,
                    isActive = false
                };

                var floatCurve = GetCurve(parameter.path, LayeredBlendingType);
                if (floatCurve != null)
                {
                    parameter.value = floatCurve.Evaluate(0f);
                    parameter.useSlider = parameter.value >= 0f && parameter.value <= 1f;
                    parameter.isActive = true;
                }

                _blendVectors.Add(new VectorBlendParameter()
                {
                    name = field.Name,
                    parameters = new List<FloatBlendParameter>() {parameter},
                    targetType = LayeredBlendingType
                });
            }

            var bindings = AnimationUtility.GetCurveBindings(_clip);
            foreach (var binding in bindings)
            {
                if (binding.type == typeof(Transform) || binding.type == LayeredBlendingType
                                                      || LayeredBlendingType.IsAssignableFrom(binding.type)
                                                      || IsAnimatorInternalCurve(binding))
                {
                    continue;
                }
                
                AnimationCurve curve = AnimationUtility.GetEditorCurve(_clip, binding);
                FloatBlendParameter parameter = new FloatBlendParameter()
                {
                    name = binding.propertyName,
                    path = binding.propertyName,
                    value = curve.Evaluate(0f),
                    isActive = true
                };
                
                parameter.useSlider = parameter.value >= 0f && parameter.value <= 1f;
                
                _blendVectors.Add(new VectorBlendParameter()
                {
                    name = binding.type.Name,
                    targetType = binding.type,
                    parameters = new List<FloatBlendParameter>() { parameter }
                });
            }
        }

        [MenuItem("Assets/Edit Curve Properties", priority = 1000)]
        private static void OpenWindow()
        {
            // 1. Get the Animation Clip.
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            
            // 2. Erase the saved data.
            SessionState.EraseString(SessionSaveKey);
            
            // 3. Show the window and initialize parameters.
            var window = GetWindow<CurveBlendingEditor>(true, "Curve Property Editor");
            window.minSize = new Vector2(400f, 500f);
            window.InitializeParameters(clip);
            window.Show();
            
            Selection.activeObject = null;
        }

        [MenuItem("Assets/Edit Curve Properties", validate = true, priority = 1000)]
        private static bool ValidateSelection()
        {
            return KEditorUtility.GetAnimationClipFromSelection() != null;
        }

        [MenuItem("Assets/Copy Curve Properties", priority = 1001)]
        private static void CopyCurveProperties()
        {
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            _copiedCurveProperties = CurveProcessorUtility.CollectCurves(clip);
        }

        [MenuItem("Assets/Copy Curve Properties", validate = true, priority = 1001)]
        private static bool ValidateCopyCurveProperties()
        {
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            return CurveProcessorUtility.TryGetModelImporter(clip, out _);
        }

        [MenuItem("Assets/Paste Curve Properties", priority = 1002)]
        private static void PasteCurveProperties()
        {
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            if (_copiedCurveProperties == null ||
                !CurveProcessorUtility.TryGetModelImporter(clip, out ModelImporter importer))
            {
                return;
            }

            if (CurveProcessorUtility.CopyCurvesToImporter(_copiedCurveProperties, importer, clip.name))
            {
                importer.SaveAndReimport();
            }
        }

        [MenuItem("Assets/Paste Curve Properties", validate = true, priority = 1002)]
        private static bool ValidatePasteCurveProperties()
        {
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            return _copiedCurveProperties != null &&
                   CurveProcessorUtility.TryGetModelImporter(clip, out _);
        }

        private void OnGUI()
        {
            GUIStyle generalStyle = new GUIStyle()
            {
                padding = new RectOffset(10, 0, 5, 5)
            };

            EditorGUILayout.BeginVertical(generalStyle);
            
            _characterPrefab = (GameObject) EditorGUILayout.ObjectField("Character Prefab", _characterPrefab, 
                typeof(GameObject), false);
            
            _controller = (RuntimeAnimatorController) EditorGUILayout.ObjectField("Animator Controller", _controller, 
                typeof(RuntimeAnimatorController), false);

            _clip = (AnimationClip)EditorGUILayout.ObjectField("Selected Animation Clip", _clip, 
                typeof(AnimationClip), false);
            if (_clip == null)
            {
                EditorGUILayout.HelpBox("No Animation Clip selected.", MessageType.Warning);
                return;
            }
            
            EditorGUILayout.Space();
            
            DrawParameters();
            DrawAddCustomParameterButton();
            
            EditorGUILayout.EndVertical();
        }

        private void OnDisable()
        {
            CurveBlendingEditorState state = new CurveBlendingEditorState()
            {
                characterPrefab = _characterPrefab,
                clip = _clip,
                controller = _controller,
                scrollPosition = _scrollPosition
            };
            
            var json = JsonUtility.ToJson(state);
            SessionState.SetString(SessionSaveKey, json);

            TrySavingToFBX();
        }

        private void TrySavingToFBX()
        {
            if (!CurveProcessorUtility.TryGetModelImporter(_clip, out ModelImporter importer))
            {
                return;
            }

            List<CustomEditorCurveData> curves = new List<CustomEditorCurveData>();
            foreach (var vectorParameter in _blendVectors)
            {
                foreach (var floatParameter in vectorParameter.parameters)
                {
                    if (!floatParameter.isActive) continue;

                    curves.Add(new CustomEditorCurveData()
                    {
                        curve = GetCurve(floatParameter.path, vectorParameter.targetType),
                        propertyName = floatParameter.path,
                        targetTypeName = vectorParameter.targetType.AssemblyQualifiedName
                    });
                }
            }

            CurveProcessorUtility.WriteCurvesToImporter(_clip, importer, curves);
        }

        private void DrawFloatParameter(ref FloatBlendParameter parameter, Type targetType, (int, int) index)
        {
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = parameter.isActive;
                    
            float cachedValue = parameter.value;

            if (parameter.useCurve)
            {
                var newCurve = EditorGUILayout.CurveField(parameter.name, GetCurve(parameter.path, targetType));
                AddCurve(_clip, parameter.path, newCurve, targetType);
            }
            else if (parameter.useSlider)
            {
                parameter.value = EditorGUILayout.Slider(parameter.name, parameter.value, 0f, 1f);
            }
            else
            {
                parameter.value = EditorGUILayout.FloatField(parameter.name, parameter.value);
            }
            
            Event e = Event.current;

            if (e.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(e.mousePosition))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Curve Mode"), false,
                    () =>
                    {
                        var parameter = _blendVectors[index.Item1].parameters[index.Item2];
                        parameter.useCurve = true;
                        _blendVectors[index.Item1].parameters[index.Item2] = parameter;
                    });
                menu.AddItem(new GUIContent("Value Mode"), false,
                    () =>
                    {
                        var parameter = _blendVectors[index.Item1].parameters[index.Item2];
                        parameter.useCurve = false;
                        _blendVectors[index.Item1].parameters[index.Item2] = parameter;
                    });
                menu.ShowAsContext();
                
                e.Use();
            }

            parameter.useSlider = EditorGUILayout.Toggle(parameter.useSlider, GUILayout.Width(15));

            if (parameter.isActive && !Mathf.Approximately(cachedValue, parameter.value))
            {
                AddCurve(_clip, $"{parameter.path}", parameter.value, targetType);
            }

            GUI.enabled = !parameter.isActive;
            if (GUILayout.Button("+", GUILayout.Width(30)))
            {
                AddCurve(_clip, $"{parameter.path}", parameter.value, targetType);
                parameter.isActive = true;
                parameter.useSlider = true;
            }

            GUI.enabled = parameter.isActive;
            if (GUILayout.Button("-", GUILayout.Width(30)))
            {
                RemoveCurve(_clip, $"{parameter.path}", targetType);
                parameter.useCurve = false;
                parameter.isActive = false;
            }

            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawParameters()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            GUIStyle contentStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(5, 0, 5, 5)
            };
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
            };

            int blendIndex = 0;
            foreach (var vectorBlend in _blendVectors)
            {
                EditorGUILayout.LabelField(vectorBlend.name, labelStyle);

                EditorGUILayout.BeginVertical(contentStyle);
                
                for (int i = 0; i < vectorBlend.parameters.Count; i++)
                {
                    var parameter = vectorBlend.parameters[i];
                    DrawFloatParameter(ref parameter, vectorBlend.targetType, (blendIndex, i));
                    vectorBlend.parameters[i] = parameter;
                }

                blendIndex++;
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
        
        private void OnParameterSelected(object[] data, ComponentBinding binding)
        {
            int lastIndex = binding.path.LastIndexOf(".");
            
            string parameterName = binding.path.Substring(lastIndex + 1);
            string parameterPath = binding.path.Substring(binding.path.IndexOf(".") + 1);

            VectorBlendParameter parameter = new VectorBlendParameter()
            {
                name = binding.path.Substring(0, lastIndex),
                parameters = new List<FloatBlendParameter>()
                {
                    new FloatBlendParameter()
                    {
                        name = parameterName,
                        path = parameterPath,
                        value = 0f,
                        isActive = true,
                    }
                },
                targetType = binding.context == null ? typeof(Animator) : binding.context.GetType()
            };
            
            AddCurve(_clip, parameterPath, 0f, parameter.targetType);
            _blendVectors.Add(parameter);
        }

        private void AddCustomParameter()
        {
            if (_characterPrefab == null && _controller == null)
            {
                return;
            }
            
            _searchData = new BindableSearchData()
            {
                propertyType = typeof(float),
                bindings = new List<ComponentBinding>(),
                fieldInfo = null
            };

            if (_characterPrefab != null)
            {
                Type blendingType = typeof(LayeredBlendingComponent);
                var components = _characterPrefab.GetComponents<MonoBehaviour>();
                foreach (var component in components)
                {
                    Type contextType = component.GetType();
                    if (contextType == blendingType || blendingType.IsAssignableFrom(contextType)) continue;

                    _searchData.context = component;
                    PropertyBindingsUtility.SearchBindableMembers(_searchData, contextType, contextType.Name);
                }
            }

            if (_controller != null)
            {
                PropertyBindingsUtility.SearchAnimatorParameters(_searchData, typeof(float), _controller);
            }
            
            _dropdownState = null;
            
            var dropdown = new BindableDropdown(_dropdownState, _searchData.bindings)
            {
                onBindingSelected = OnParameterSelected
            };
            
            dropdown.Show(_buttonRect);
            dropdown.SetWindowSize(new Vector2(0f, 200f));
        }

        private void DrawAddCustomParameterButton()
        {
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            _buttonRect = GUILayoutUtility.GetRect(new GUIContent("Add Custom Parameter"), GUI.skin.button,
                GUILayout.Width(300));

            if (GUI.Button(_buttonRect, "Add Custom Parameter"))
            {
                AddCustomParameter();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private AnimationCurve GetCurve(string propertyName, Type valueType)
        {
            EditorCurveBinding binding = new EditorCurveBinding()
            {
                path = string.Empty,
                type = valueType,
                propertyName = propertyName
            };

            return AnimationUtility.GetEditorCurve(_clip, binding);
        }

        public static void AddCurve(AnimationClip clip, string propertyName, float value, Type targetType)
        {
            var curve = AnimationCurve.Constant(0f, clip.length, value);
            clip.SetCurve(string.Empty, targetType, propertyName, curve);
        }
        
        public static void AddCurve(AnimationClip clip, string propertyName, AnimationCurve curve, Type targetType)
        {
            clip.SetCurve(string.Empty, targetType, propertyName, curve);
        }

        public static void RemoveCurve(AnimationClip clip, string propertyName, Type targetType)
        {
            clip.SetCurve(string.Empty, targetType, propertyName, null);
        }
    }
}
