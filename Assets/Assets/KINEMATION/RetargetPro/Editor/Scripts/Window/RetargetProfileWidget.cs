// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Editor;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    public class RetargetProfileWidget
    {
        public Action onComponentAdded;
        public Action onComponentPasted;
        public Action onComponentRemoved;
        
        private SerializedObject _serializedObject;
        private SerializedProperty _componentsProperty;
        private ScriptableObject _asset;
        
        private Type _collectionType;
        
        private List<UnityEditor.Editor> _editors;

        private bool _isInitialized;
        private ReorderableList _componentsList;
        
        private GUIStyle _elementButtonStyle;
        private GUIStyle _rightAlignedHeaderStyle;

        private AdvancedDropdownState _dropdownState;
        private Type[] _componentTypes;
        private List<string> _typeOptions;
        
        private int _editorIndex = -1;

        private RetargetProfile _retargetProfile;
        private string _remapStatus;
        private MessageType _remapStatusType = MessageType.Info;
        private bool _structureEditingDisabled;

        private const string PreviewStructureEditDisabledMessage =
            "Adding and deleting retarget features is disabled while retarget preview is active. " +
            "Click Pose or Reset Preview before changing the feature list.";

        public RetargetProfileWidget(RetargetProfile profile)
        {
            _retargetProfile = profile;
            _dropdownState = new AdvancedDropdownState();
        }

        public void AddComponent(Type type)
        {
            if (_structureEditingDisabled)
            {
                return;
            }

            _serializedObject.Update();
            
            ScriptableObject newComponent = CreateNewComponent(type);
            
            Undo.RegisterCreatedObjectUndo(newComponent, "Add Component");
            AssetDatabase.AddObjectToAsset(newComponent, _asset);
            
            _componentsProperty.arraySize++;
            var componentProp = _componentsProperty.GetArrayElementAtIndex(_componentsProperty.arraySize - 1);
            componentProp.objectReferenceValue = newComponent;

            _editors.Add(UnityEditor.Editor.CreateEditor(newComponent));
            _serializedObject.ApplyModifiedProperties();
            
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssetIfDirty(_asset);

            var newFeature = _retargetProfile.retargetFeatures[^1];
            newFeature.sourceRig = _retargetProfile.sourceRig;
            newFeature.targetRig = _retargetProfile.targetRig;
            newFeature.OnFeatureAdded();
            EditorUtility.SetDirty(newFeature);
            EditorUtility.SetDirty(_retargetProfile);
            AssetDatabase.SaveAssetIfDirty(newFeature);
            AssetDatabase.SaveAssetIfDirty(_retargetProfile);

            onComponentAdded?.Invoke();
            _editorIndex = -1;
            if (_componentsList != null)
            {
                _componentsList.index = -1;
            }
        }
        
        private ScriptableObject CreateNewComponent(Type type)
        {
            var instance = ScriptableObject.CreateInstance(type);
            instance.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            instance.name = type.Name;
            return instance;
        }

        private static string GetComponentTypePath(Type type)
        {
            string typePath = type.Name;
            var attributes = type.GetCustomAttributes(true);

            foreach (var attr in attributes)
            {
                if (attr is not ScriptableComponentGroupAttribute groupAttr) continue;
                typePath = $"{groupAttr.group}.{groupAttr.shortName}";
            }

            return typePath;
        }

        private void OnTypeSelected(int selected, string _)
        {
            AddComponent(_componentTypes[selected]);
        }

        public void RemoveComponent(int index)
        {
            if (_structureEditingDisabled)
            {
                return;
            }

            if (_editors == null || index < 0 || index >= _editors.Count) return;
            _editors.RemoveAt(index);
            _serializedObject.Update();
            
            _editorIndex = -1;
            
            var property = _componentsProperty.GetArrayElementAtIndex(index);
            var component = property.objectReferenceValue;
            
            property.objectReferenceValue = null;
            _componentsProperty.DeleteArrayElementAtIndex(index);
            
            _serializedObject.ApplyModifiedProperties();
            if (component != null) Undo.DestroyObjectImmediate(component);

            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();

            onComponentRemoved?.Invoke();
            if (_componentsList != null)
            {
                _componentsList.index = -1;
            }
        }
        
        private void CopyComponent(Object component)
        {
            string typeName = component.GetType().AssemblyQualifiedName;
            string typeData = JsonUtility.ToJson(component);
            EditorGUIUtility.systemCopyBuffer = $"{typeName}|{typeData}";
        }

        private bool CanPaste(Object component)
        {
            if (string.IsNullOrWhiteSpace(EditorGUIUtility.systemCopyBuffer)) return false;

            string clipboard = EditorGUIUtility.systemCopyBuffer;
            int separator = clipboard.IndexOf('|');

            if (separator < 0) return false;

            return component.GetType().AssemblyQualifiedName == clipboard.Substring(0, separator);
        }

        private void PasteComponent(Object component)
        {
            string clipboard = EditorGUIUtility.systemCopyBuffer;
            string typeData = clipboard.Substring(clipboard.IndexOf('|') + 1);
            Undo.RecordObject(component, "Paste Settings");
            JsonUtility.FromJsonOverwrite(typeData, component);
        }

        private void OnContextMenuSelection(object userData, string[] options, int selected)
        {
            int index = (int) userData;
            Object component = _componentsProperty.GetArrayElementAtIndex(index).objectReferenceValue;

            int optionIndex = 0;
            if (selected == optionIndex++)
            {
                CopyComponent(component);
                return;
            }

            if (selected == optionIndex++)
            {
                if (!CanPaste(component)) return;

                PasteComponent(component);
                onComponentPasted?.Invoke();
                return;
            }

            if (_structureEditingDisabled)
            {
                return;
            }

            RemoveComponent(index);
        }

        private void SetupReorderableList()
        {
            _componentsList = new ReorderableList(_serializedObject, _componentsProperty, true, 
                false, false, true);
            _componentsList.index = -1;

            _componentsList.drawElementBackgroundCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= _componentsProperty.arraySize)
                {
                    return;
                }

                rect.y += 1f;
                rect.height -= 2f;

                Color rowColor = index % 2 == 0
                    ? new Color(0.22f, 0.22f, 0.22f, 0.9f)
                    : new Color(0.30f, 0.30f, 0.30f, 0.9f);

                if (isActive || isFocused)
                {
                    rowColor = new Color(0.36f, 0.36f, 0.36f, 0.95f);
                }

                EditorGUI.DrawRect(rect, rowColor);
            };

            _componentsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (_retargetProfile == null || _retargetProfile.retargetFeatures == null ||
                    index < 0 || index >= _retargetProfile.retargetFeatures.Count)
                {
                    return;
                }

                RetargetFeature feature = _retargetProfile.retargetFeatures[index];
                if (feature == null)
                {
                    EditorGUI.LabelField(rect, "Missing Retarget Feature (remove this entry)");
                    return;
                }
                BasicRetargetFeature basicFeature = feature as BasicRetargetFeature;

                GetFeatureColumnRects(rect, out Rect enableRect, out Rect sourceRect, out Rect targetRect,
                    out Rect actionsRect);

                Rect toggleRect = new Rect(enableRect.x + 2f, rect.y + 1f, 14f, Mathf.Max(0f, rect.height - 2f));

                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUI.Toggle(toggleRect, feature.featureWeight > 0f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(feature, "Toggle Retarget Feature");
                    feature.featureWeight = enabled ? 1f : 0f;
                    EditorUtility.SetDirty(feature);
                }

                feature.drawGizmos = _editorIndex == index;

                float iconWidth = 25f;
                Rect mapRect = new Rect(actionsRect.xMax - iconWidth, rect.y + 2f, iconWidth,
                    Mathf.Max(0f, rect.height - 3f));

                var iconContent = EditorGUIUtility.IconContent("Refresh");
                iconContent.tooltip = "Auto map bone chains.";

                if (GUI.Button(mapRect, iconContent))
                {
                    Undo.RecordObject(feature, "Auto Map Retarget Feature");
                    feature.MapChains();
                    EditorUtility.SetDirty(feature);
                    EditorUtility.SetDirty(_retargetProfile);
                    AssetDatabase.SaveAssetIfDirty(feature);
                    AssetDatabase.SaveAssetIfDirty(_retargetProfile);
                }
                
                if (basicFeature != null)
                {
                    DrawChainLabel(sourceRect, rect, basicFeature.sourceChain,
                        "Source chain is empty. Press the Refresh icon in Actions or assign the chain manually.");
                    DrawChainLabel(targetRect, rect, basicFeature.targetChain,
                        "Target chain is empty. Press the Refresh icon in Actions or assign the chain manually.");
                }
                else
                {
                    Rect displayRect = new Rect(sourceRect.x, rect.y, targetRect.xMax - sourceRect.x, rect.height);
                    if (!feature.GetStatus())
                    {
                        Rect warningRect = GetWarningRect(displayRect, rect);
                        DrawWarningIcon(warningRect, feature.GetErrorMessage());
                        displayRect.width = Mathf.Max(0f, warningRect.x - displayRect.x - 2f);
                    }

                    EditorGUI.LabelField(displayRect, feature.GetDisplayName());
                }
                
                if (Event.current.type == EventType.MouseUp && Event.current.button == 1 
                    && rect.Contains(Event.current.mousePosition))
                {
                    List<GUIContent> menuOptions = new List<GUIContent>
                    {
                        new GUIContent("Copy"),
                        new GUIContent("Paste")
                    };

                    if (!_structureEditingDisabled)
                    {
                        menuOptions.Add(new GUIContent("Delete"));
                    }

                    EditorUtility.DisplayCustomMenu(new Rect(Event.current.mousePosition, Vector2.zero), 
                        menuOptions.ToArray(), -1, OnContextMenuSelection, index);
                }
            };

            _componentsList.onReorderCallbackWithDetails = (ReorderableList list, int oldIndex, int newIndex) =>
            {
                UnityEditor.Editor editorToMove = _editors[oldIndex];
                
                _editors.RemoveAt(oldIndex);
                if (newIndex > oldIndex)
                {
                    if (newIndex > _editors.Count - 1)
                    {
                        _editors.Add(editorToMove);
                        return;
                    }
                }
                
                _editors.Insert(newIndex, editorToMove);
            };

            _componentsList.onSelectCallback = list =>
            {
                _editorIndex = list.index;
            };

            _componentsList.onRemoveCallback = list =>
            {
                RemoveComponent(list.index);
            };

            _componentsList.onCanRemoveCallback = list =>
                !_structureEditingDisabled && list.index >= 0 && list.index < _componentsProperty.arraySize;
        }

        public void SetStructureEditingDisabled(bool disabled)
        {
            _structureEditingDisabled = disabled;
        }
        
        public void Init(SerializedObject serializedObject)
        {
            _serializedObject = serializedObject;
            _componentsProperty = _serializedObject.FindProperty("retargetFeatures");
            _collectionType = typeof(RetargetFeature);
            
            Assert.IsNotNull(_componentsProperty);
            Assert.IsNotNull(_collectionType);
            
            _asset = _serializedObject.targetObject as ScriptableObject;
            if (_asset == null)
            {
                Debug.LogError($"{_serializedObject.targetObject.name} is not a Scriptable Object!");
                return;
            }

            if (!_componentsProperty.isArray)
            {
                Debug.LogError($"{_componentsProperty.displayName} is not an array!");
                return;
            }

            List<KeyValuePair<string, Type>> typeEntries = new List<KeyValuePair<string, Type>>();
            var allCollectionTypes = TypeCache.GetTypesDerivedFrom(_collectionType).ToArray();

            foreach (var type in allCollectionTypes)
            {
                if(type.IsAbstract) continue;
                typeEntries.Add(new KeyValuePair<string, Type>(GetComponentTypePath(type), type));
            }

            typeEntries = typeEntries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();
            _componentTypes = typeEntries.Select(entry => entry.Value).ToArray();
            _typeOptions = typeEntries.Select(entry => entry.Key).ToList();
            _editors = new List<UnityEditor.Editor>();

            // Create editors for the current components.
            int arraySize = _componentsProperty.arraySize;
            for (int i = 0; i < arraySize; i++)
            {
                SerializedProperty element = _componentsProperty.GetArrayElementAtIndex(i);
                _editors.Add(UnityEditor.Editor.CreateEditor(element.objectReferenceValue));
            }
            
            SetupReorderableList();
            _isInitialized = true;
        }

        public bool RequestRemapAllChains(out string message)
        {
            message = string.Empty;
            if (!_isInitialized || _retargetProfile == null)
            {
                return false;
            }

            bool success = RemapAllChains();
            message = _remapStatus;
            return success;
        }

        public bool TrySelectFeature(RetargetFeature feature)
        {
            if (!_isInitialized || _retargetProfile == null || feature == null || _retargetProfile.retargetFeatures == null)
            {
                return false;
            }

            int count = _retargetProfile.retargetFeatures.Count;
            for (int i = 0; i < count; i++)
            {
                if (_retargetProfile.retargetFeatures[i] != feature)
                {
                    continue;
                }

                _editorIndex = i;
                if (_componentsList != null)
                {
                    _componentsList.index = i;
                }

                return true;
            }

            return false;
        }

        private void GetFeatureColumnRects(Rect rect, out Rect enableRect, out Rect sourceRect, out Rect targetRect,
            out Rect actionsRect)
        {
            const float toggleColumnWidth = 48f;
            const float actionsColumnWidth = 60f;
            const float spacing = 6f;

            enableRect = new Rect(rect.x, rect.y, toggleColumnWidth, rect.height);
            actionsRect = new Rect(rect.xMax - actionsColumnWidth, rect.y, actionsColumnWidth, rect.height);

            float labelsWidth = Mathf.Max(20f, actionsRect.x - (enableRect.xMax + spacing));
            float sourceWidth = Mathf.Max(20f, labelsWidth * 0.5f - spacing * 0.5f);

            sourceRect = new Rect(enableRect.xMax + spacing, rect.y, sourceWidth, rect.height);
            targetRect = new Rect(sourceRect.xMax + spacing, rect.y,
                Mathf.Max(20f, actionsRect.x - (sourceRect.xMax + spacing)), rect.height);
        }

        private static void DrawChainLabel(Rect labelRect, Rect rowRect, KRigElementChain chain, string warningTooltip)
        {
            bool isEmpty = IsChainEmpty(chain);
            if (isEmpty)
            {
                Rect warningRect = GetWarningRect(labelRect, rowRect);
                DrawWarningIcon(warningRect, warningTooltip);
                labelRect.width = Mathf.Max(0f, warningRect.x - labelRect.x - 2f);
            }

            EditorGUI.LabelField(labelRect, GetChainDisplayName(chain));
        }

        private static Rect GetWarningRect(Rect anchorRect, Rect rowRect)
        {
            return new Rect(anchorRect.xMax - 18f, rowRect.y + 3f, 16f, Mathf.Max(0f, rowRect.height - 6f));
        }

        private static void DrawWarningIcon(Rect warningRect, string tooltip)
        {
            GUIContent iconContent = EditorGUIUtility.IconContent("console.warnicon");
            iconContent.tooltip = tooltip;
            GUI.Label(warningRect, iconContent);
        }

        private static bool IsChainEmpty(KRigElementChain chain)
        {
            return chain == null || chain.elementChain == null || chain.elementChain.Count == 0;
        }

        private static string GetChainDisplayName(KRigElementChain chain)
        {
            return string.IsNullOrEmpty(chain?.chainName) ? "None" : chain.chainName;
        }

        private void DrawListColumnHeaders()
        {
            Rect headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetFeatureColumnRects(headerRect, out Rect enableRect, out Rect sourceRect, out Rect targetRect,
                out Rect actionsRect);

            if (_rightAlignedHeaderStyle == null)
            {
                _rightAlignedHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }

            sourceRect.x += 20f;
            targetRect.x += 4f;
            
            EditorGUI.LabelField(enableRect, "ENABLE", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(sourceRect, "SOURCE CHAIN", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(targetRect, "TARGET CHAIN", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(actionsRect, "ACTIONS", _rightAlignedHeaderStyle);
        }

        private void DrawWidgetGUI()
        {
            if (!string.IsNullOrEmpty(_remapStatus))
            {
                EditorGUILayout.HelpBox(_remapStatus, _remapStatusType);
                EditorGUILayout.Space();
            }

            if (_structureEditingDisabled)
            {
                EditorGUILayout.HelpBox(PreviewStructureEditDisabledMessage, MessageType.Info);
                EditorGUILayout.Space();
            }

            DrawListColumnHeaders();

            float listHeight = Mathf.Max(250f, _componentsList.GetHeight());
            Rect listRect = GUILayoutUtility.GetRect(0f, listHeight, GUILayout.ExpandWidth(true));
            _componentsList.DoList(listRect);

            GUILayout.Space(2f);

            Rect addButtonRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(_structureEditingDisabled))
            {
                if (GUI.Button(addButtonRect, "Add Retarget Feature", EditorStyles.miniButton))
                {
                    var dropdown = new ScriptableComponentDropdown(_dropdownState, _typeOptions);
                    dropdown.onTypeSelected = OnTypeSelected;
                    dropdown.Show(addButtonRect);
                    dropdown.SetWindowSize(new Vector2(0f, 140f));
                }
            }
            
            if (_editorIndex < 0 || _editors.Count == 0 || _editorIndex >= _editors.Count)
            {
                return;
            }
            
            EditorGUILayout.Space();

            var style = new GUIStyle(GUI.skin.box);
            style.padding = new RectOffset(12, 12, 12, 12);
            EditorGUILayout.BeginVertical(style);
            UnityEditor.Editor selectedEditor = _editors[_editorIndex];
            if (selectedEditor == null)
            {
                EditorGUILayout.HelpBox("This Retarget Feature subasset is missing. Remove the entry and recreate it.",
                    MessageType.Error);
            }
            else
            {
                selectedEditor.OnInspectorGUI();
            }
            EditorGUILayout.EndVertical();
        }

        public bool OnGUI()
        {
            if (!_isInitialized || _retargetProfile == null) return false;

            bool wideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.wideMode = true;

            _serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawWidgetGUI();
            bool serializedChanged = _serializedObject.ApplyModifiedProperties();
            bool guiChanged = EditorGUI.EndChangeCheck();
            bool changed = serializedChanged || guiChanged;
            if (changed)
            {
                PersistProfileAndFeatures();
            }

            EditorGUIUtility.wideMode = wideMode;
            return changed;
        }

        private void PersistProfileAndFeatures()
        {
            if (_retargetProfile == null)
            {
                return;
            }

            EditorUtility.SetDirty(_retargetProfile);
        }

        private bool RemapAllChains()
        {
            bool success = RetargetProfileMappingUtility.TryRebuildProfileMappings(_retargetProfile, out _remapStatus);
            _remapStatusType = success ? MessageType.Info : MessageType.Warning;

            _serializedObject.Update();
            Init(_serializedObject);
            return success;
        }
    }
}
