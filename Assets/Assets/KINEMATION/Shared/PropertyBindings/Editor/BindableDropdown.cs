// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KINEMATION.Shared.PropertyBindings.Editor
{
    public class BindableDropdownItem : AdvancedDropdownItem
    {
        public ComponentBinding binding;
        
        public BindableDropdownItem(string name, ComponentBinding binding) : base(name)
        {
            this.binding = binding;
        }
    }
    
    public class BindableDropdown : AdvancedDropdown
    {
        public Action<object[], ComponentBinding> onBindingSelected;
        private List<ComponentBinding> _options;
        private object[] _data;
        
        private const BindingFlags PrivatePropertyFlags =
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty;
        
        private const BindingFlags PrivateFieldFlags =
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField;
        
        public BindableDropdown(AdvancedDropdownState state, List<ComponentBinding> options, object[] data = null) : base(state)
        {
            _options = options;
            _data = data;
        }

        public void SetWindowSize(Vector2 minSize)
        {
            var window = EditorWindow.focusedWindow;

            if(window == null)
            {
                Debug.LogWarning("EditorWindow.focusedWindow was null.");
                return;
            }

            if(!string.Equals(window.GetType().Namespace, typeof(AdvancedDropdown).Namespace))
            {
                Debug.LogWarning("EditorWindow.focusedWindow " + EditorWindow.focusedWindow.GetType().FullName + " was not in expected namespace.");
                return;
            }
            
            Vector2 originalMinSize = window.minSize;
            if (minSize.x >= 0f) originalMinSize.x = minSize.x;
            if (minSize.y >= 0f) originalMinSize.y = minSize.y;
            window.minSize = originalMinSize;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var windowType = Type.GetType("UnityEditor.IMGUI.Controls.AdvancedDropdownWindow,UnityEditor");
            if (windowType != null)
            {
                var field = GetType().GetField("m_WindowInstance", PrivateFieldFlags);
                var window = field?.GetValue(this);
                
                var property = windowType.GetProperty("isSearchFieldDisabled", PrivatePropertyFlags);
                if (property != null && window != null) property.SetValue(window, true);
            }
            else
            {
                Debug.Log("Window type not found");
            }
            
            var root = new AdvancedDropdownItem("Select binding");
            Dictionary<string, BindableDropdownItem> categoryLookup = new Dictionary<string, BindableDropdownItem>();
            
            foreach (var option in _options)
            {
                string[] parts = option.path.Split('.');
                AdvancedDropdownItem currentParent = root;

                string fullPath = "";
                int partsCount = parts.Length;

                for (int i = 0; i < partsCount; i++)
                {
                    var part = parts[i];
                    fullPath = string.IsNullOrEmpty(fullPath) ? part : $"{fullPath}.{part}";

                    if (!categoryLookup.ContainsKey(fullPath))
                    {
                        var newItem = new BindableDropdownItem(part, option);
                        categoryLookup[fullPath] = newItem;
                        currentParent.AddChild(newItem);
                    }

                    currentParent = categoryLookup[fullPath];
                }
            }

            return root;
        }
        
        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            onBindingSelected?.Invoke(_data, ((BindableDropdownItem) item).binding);
        }
    }
}