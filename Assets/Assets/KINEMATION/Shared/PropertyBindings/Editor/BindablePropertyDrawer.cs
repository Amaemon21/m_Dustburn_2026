// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KINEMATION.Shared.PropertyBindings.Editor
{
    [CustomPropertyDrawer(typeof(BindableProperty<>))]
    public class BindablePropertyDrawer : PropertyDrawer
    {
        private List<ComponentBinding> _bindings;
        private Rect _propertyRect;
        private AdvancedDropdownState _dropdownState = new AdvancedDropdownState();

        private SerializedProperty _objectTypeProp;
        private SerializedProperty _friendlyObjectTypeProp;
        private SerializedProperty _propertyPathProp;
        private SerializedProperty _isBoundProp;
        
        private SerializedObject _serializedObject;

        private Type _propertyType;
        private object[] _attributes;

        private bool _isResolved;

        private GameObject _context;
        
        private void OnPropertySelected(object[] data, ComponentBinding binding)
        {
            var path = binding.path;
            var propertyPath = path.Remove(0, path.IndexOf(".") + 1);

            if (binding.context != null)
            {
                Type targetObjectType = binding.context.GetType();
                
                (data[0] as SerializedProperty).stringValue = targetObjectType.AssemblyQualifiedName;
                (data[1] as SerializedProperty).stringValue = targetObjectType.Name;
            }
            
            (data[2] as SerializedProperty).stringValue = propertyPath;
            (data[3] as SerializedProperty).boolValue = true;

            _isResolved = true;
            _serializedObject.ApplyModifiedProperties();
        }
        
        private void HandlePropertySelection()
        {
            if (_context == null)
            {
                Debug.LogWarning("BindableProperty: Context not found!");
                return;
            }

            var components = _context.GetComponentsInChildren<MonoBehaviour>();
            _bindings = new List<ComponentBinding>();

            BindableSearchData searchData = new BindableSearchData()
            {
                propertyType = _propertyType,
                bindings = _bindings,
                fieldInfo = fieldInfo
            };

            // Search for MonoBehaviour properties.
            foreach (MonoBehaviour component in components)
            {
                if (component == null) continue;
                
                Type contextType = component.GetType();
                searchData.context = component;
                PropertyBindingsUtility.SearchBindableMembers(searchData, contextType, contextType.Name);
            }

            if (components.Length > 0)
            {
                // Search for Animator properties.
                foreach (var attr in _attributes)
                {
                    if (attr is not BindAnimatorAttribute) continue;
                    searchData.context = components[0];
                    PropertyBindingsUtility.SearchAnimatorParameters(searchData, _propertyType);
                    break;
                }
            }
            
            var data = new object[]
            {
                _objectTypeProp,
                _friendlyObjectTypeProp,
                _propertyPathProp,
                _isBoundProp
            };
            
            _dropdownState = null;
            var dropdown = new BindableDropdown(_dropdownState, _bindings, data);
            dropdown.onBindingSelected = OnPropertySelected;
            dropdown.Show(_propertyRect);
            dropdown.SetWindowSize(new Vector2(0f, 200f));
        }

        private void ResolveProperty()
        {
            string shortTypeName = _objectTypeProp.stringValue.Split(",")[0].Split(".")[^1];

            List<Behaviour> components = new List<Behaviour>();
            _context.GetComponents(components);

            foreach (var component in components)
            {
                Type componentType = component.GetType();
                
                if (!componentType.Name.EndsWith(shortTypeName)) continue;

                _objectTypeProp.stringValue = componentType.AssemblyQualifiedName;
                _serializedObject.ApplyModifiedProperties();

                _isResolved = PropertyBindingsUtility.IsPropertyResolved(componentType,
                    _propertyType, _propertyPathProp.stringValue);
                
                return;
            }
        }

        private void DrawPropertyField(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Generic)
            {
                EditorGUI.PropertyField(rect, property, label, true);
                return;
            }
            
            if (property.propertyType is SerializedPropertyType.Float or SerializedPropertyType.Integer)
            {
                BoundRangeAttribute rangeAttribute = null;

                foreach (var attr in _attributes)
                {
                    rangeAttribute = attr as BoundRangeAttribute;
                    if (rangeAttribute == null) continue;

                    if (property.propertyType == SerializedPropertyType.Float)
                    {
                        property.floatValue = EditorGUI.Slider(rect, label, property.floatValue,
                            rangeAttribute.min, rangeAttribute.max);
                        
                        return;
                    }

                    property.intValue = EditorGUI.IntSlider(rect, label, property.intValue,
                        (int) rangeAttribute.min, (int) rangeAttribute.max);
                    return;
                }
            }

            EditorGUI.PropertyField(rect, property, label);
        }

        private static void DrawBoundPropertyLabel(Rect rect, string bindingPath)
        {
            EditorGUI.SelectableLabel(rect, bindingPath);

            // SelectableLabel does not expose tooltip content, so add an invisible hover target on top.
            GUI.Label(rect, new GUIContent(string.Empty, bindingPath), GUIStyle.none);
        }
         
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            _attributes = fieldInfo.GetCustomAttributes(true);
            _serializedObject = property.serializedObject;
            
            _objectTypeProp = property.FindPropertyRelative("objectType");
            _friendlyObjectTypeProp = property.FindPropertyRelative("friendlyObjectType");
            _propertyPathProp = property.FindPropertyRelative("propertyPath");
            _isBoundProp = property.FindPropertyRelative("isBound");
            
            SerializedProperty defaultValueProp = property.FindPropertyRelative("defaultValue");

            _propertyType = fieldInfo.FieldType.GetGenericArguments()[0];
            _context = (property.serializedObject.targetObject as IBindableContext)?.GetContext();
            _isResolved = false;

            Type contextType = Type.GetType(_objectTypeProp.stringValue);
            if (contextType != null)
            {
                _isResolved = contextType == typeof(Animator)
                              || PropertyBindingsUtility.IsPropertyResolved(contextType, _propertyType,
                                  _propertyPathProp.stringValue);
            }
            
            float iconWidth = 20;
            bool showResolveIcon = _isBoundProp.boolValue && !_isResolved;

            Rect rect = position;
            rect.width -= iconWidth * (showResolveIcon ? 3f : 2f);

            _propertyRect = rect;

            if (_isBoundProp.boolValue)
            {
                string objectTypeName = _friendlyObjectTypeProp.stringValue;
                objectTypeName = objectTypeName.Remove(0, objectTypeName.IndexOf(".") + 1);

                bool drawLabel = true;
                foreach (var attr in _attributes)
                {
                    if (attr is CustomBindableDrawerAttribute customAttribute)
                    {
                        drawLabel = customAttribute.showLabel;
                        break;
                    }
                }

                if (drawLabel)
                {
                    Rect labelRect = rect;
                    labelRect.width = EditorGUIUtility.labelWidth;
                    
                    EditorGUI.HandlePrefixLabel(labelRect, labelRect, label);

                    rect.x += labelRect.width;
                    rect.width -= labelRect.width;
                }

                string bindingPath = $"{objectTypeName}.{_propertyPathProp.stringValue}";
                DrawBoundPropertyLabel(rect, bindingPath);
            }
            else
            {
                EditorGUIUtility.labelWidth += 1f;

                DrawPropertyField(rect, defaultValueProp, label);
                
                EditorGUIUtility.labelWidth -= 1f;
            }

            rect.x += rect.width + 1f;
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.width = iconWidth - 1f;

            if (showResolveIcon)
            {
                GUIStyle iconButtonStyle = new GUIStyle(GUI.skin.button);
                iconButtonStyle.padding = new RectOffset(2, 0, 2, 1);
                iconButtonStyle.margin = new RectOffset(0, 0, 0, 0);
                
                GUIContent icon = EditorGUIUtility.IconContent("console.warnicon");
                icon.tooltip = "This binding is no longer valid.";
                
                if (GUI.Button(rect, icon, iconButtonStyle)) ResolveProperty();
                
                rect.x += rect.width;
            }
            
            if (GUI.Button(rect, EditorGUIUtility.IconContent("Toolbar Plus")))
            {
                HandlePropertySelection();
            }
            
            rect.x += rect.width;
            if (GUI.Button(rect, EditorGUIUtility.IconContent("Toolbar Minus")))
            {
                _objectTypeProp.stringValue = _propertyPathProp.stringValue = string.Empty;
                _isBoundProp.boolValue = false;
                _serializedObject.ApplyModifiedProperties();
            }
            
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty isBound = property.FindPropertyRelative("isBound");
            float propHeight = base.GetPropertyHeight(property, label);

            if (!isBound.boolValue)
            {
                SerializedProperty defaultValueProp = property.FindPropertyRelative("defaultValue");
                propHeight = EditorGUI.GetPropertyHeight(defaultValueProp, true);
            }

            return propHeight;
        }
    }
}