// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;
using PropertyAttribute = UnityEngine.PropertyAttribute;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Setup
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomSetupNameAttribute : PropertyAttribute
    {
        public string presetName;

        public CustomSetupNameAttribute(string presetName)
        {
            this.presetName = presetName;
        }
    }

    [Serializable]
    public abstract class SetupSkeletonProperty : ScriptableObject, IRigProvider
    {
        public SerializedProperty property;
        public SerializedObject serializedObject;
        
        protected CharacterSkeleton _skeleton;
        protected string _propertyName;
        
        public virtual void Initialize(CharacterSkeleton skeleton, string propertyName)
        {
            _skeleton = skeleton;
            _propertyName = propertyName;
            serializedObject = new SerializedObject(this);
        }

        public void Update()
        {
            serializedObject?.Update();
        }
        
        public KRigElement[] GetHierarchy()
        {
            return _skeleton.GetHierarchy();
        }

        public void DrawProperty()
        {
            if (property == null)
            {
                EditorGUILayout.HelpBox("Invalid property!", MessageType.Error);
                return;
            }
            
            EditorGUILayout.PropertyField(property, new GUIContent(_propertyName));
        }
    }

    [Serializable]
    public class SetupRigElementProperty : SetupSkeletonProperty
    {
        public KRigElement element = new KRigElement(-1);

        public override void Initialize(CharacterSkeleton skeleton, string propertyName)
        {
            base.Initialize(skeleton, propertyName);
            property = serializedObject.FindProperty(nameof(element));
        }
    }
    
    [Serializable]
    public class SetupRigElementChainProperty : SetupSkeletonProperty
    {
        [CustomElementChainDrawer(true, false)]
        public KRigElementChain chain = new KRigElementChain();
        
        public override void Initialize(CharacterSkeleton skeleton, string propertyName)
        {
            base.Initialize(skeleton, propertyName);
            property = serializedObject.FindProperty(nameof(chain));
        }
    }
    
    public class CasSetupPreset
    {
        protected AnimationClip _basePose;
        protected AnimationClip _overlayPose;
        protected RuntimeAnimatorController _overlayAnimator;
        public CharacterAnimationSetupData setupData;
        
        protected LayeredBlendingSetupWidget _layeredBlendingWidget;
        
        public virtual void Initialize()
        {
            _layeredBlendingWidget = new LayeredBlendingSetupWidget(false, null,
                setupData.lookupPresets);
            _layeredBlendingWidget.index = setupData.defaultBoneLookupIndex;
            _layeredBlendingWidget.onPresetChanged = UpdateBoneReferences;
        }

        public virtual void UpdateBoneReferences()
        {
        }

        protected virtual bool ValidateAnimations()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animations", KEditorUtility.boldLabel);
            
            _basePose = ObjectField("Base Pose", _basePose, false);
            _overlayPose = ObjectField("Overlay Pose", _overlayPose, false);
            _overlayAnimator = ObjectField("Overlay Controller", _overlayAnimator, false);

            if (_basePose == null)
            {
                EditorGUILayout.HelpBox("Select a Base Pose! This is usually a standing idle pose.",
                    MessageType.Warning);
                return false;
            }

            if (_overlayPose == null && _overlayAnimator == null)
            {
                EditorGUILayout.HelpBox("Select an Overlay! This can be weapon, item or action pose/animator.",
                    MessageType.Warning);
                return false;
            }

            return true;
        }
        
        public virtual bool Validate()
        {
            _layeredBlendingWidget.OnGUI();
            return true;
        }

        public virtual void Apply()
        {
            _layeredBlendingWidget.CreateBoneChains(setupData.layeredBlending);
        }

        protected static T ObjectField<T>(string label, T objectRef, bool allowSceneObjects)
            where T: UnityEngine.Object
        {
            return EditorGUILayout.ObjectField(label, objectRef, typeof(T), allowSceneObjects) as T;
        }
        
        protected static Transform AddOrGetTransform(Transform parent, string name)
        {
            if (parent == null)
            {
                Debug.LogWarning($"Failed to add or create a Transform for {name}.");
                return null;
            }
            
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent);
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
            }

            return t;
        }
        
        protected static void AddModifier(ProceduralAnimationSettings settings, AnimationModifierSettings modifier)
        {
            modifier.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            settings.modifiers.Add(modifier);
            AssetDatabase.AddObjectToAsset(modifier, settings);
        }
    }
}