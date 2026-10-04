// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;

using System;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime.Features
{
    public enum RetargetFeatureWeightMode
    {
        Value,
        Curve
    }

    public sealed class RetargetFeatureWeightAttribute : PropertyAttribute
    {
    }

#if UNITY_EDITOR
    public enum RetargetFeatureInitializationMessageChannel
    {
        Notification,
        Help
    }

    public readonly struct RetargetFeatureInitializationMessage
    {
        public readonly RetargetFeatureInitializationMessageChannel channel;
        public readonly MessageType type;
        public readonly RetargetFeature feature;
        public readonly string featureDisplayName;
        public readonly string text;

        public RetargetFeatureInitializationMessage(RetargetFeatureInitializationMessageChannel channel,
            MessageType type, RetargetFeature feature, string featureDisplayName, string text)
        {
            this.channel = channel;
            this.type = type;
            this.feature = feature;
            this.featureDisplayName = string.IsNullOrWhiteSpace(featureDisplayName)
                ? string.Empty
                : featureDisplayName.Trim();
            this.text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
    }
#endif

    [Serializable]
    public abstract class RetargetFeature : ScriptableObject, IRigUser, IRigProvider
    {
        [HideInInspector] public KRig sourceRig;
        [HideInInspector] public KRig targetRig;
        
        [Header("Feature")]
        [Tooltip("Overall influence of this feature. Right-click to switch between a value and an animation-time curve.")]
        [RetargetFeatureWeight] public float featureWeight = 1f;
        [HideInInspector] public RetargetFeatureWeightMode featureWeightMode;
        [HideInInspector] public AnimationCurve featureWeightCurve = new AnimationCurve();
#if UNITY_EDITOR
        [NonSerialized] public bool drawGizmos = false;
#endif

        public float GetFeatureWeight(float time = 0f)
        {
            if (featureWeightMode != RetargetFeatureWeightMode.Curve || featureWeightCurve == null)
            {
                return featureWeight;
            }

            if (featureWeight <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(featureWeightCurve.Evaluate(time));
        }
        
        public virtual RetargetFeatureState CreateFeatureState()
        {
            return null;
        }

        public KRig GetRigAsset()
        {
            return targetRig;
        }
        
#if UNITY_EDITOR
        public virtual bool GetStatus()
        {
            return true;
        }

        public virtual string GetErrorMessage()
        {
            return "";
        }

        public virtual void CollectInitializationMessages(RetargetFeatureState state,
            List<RetargetFeatureInitializationMessage> messages)
        {
            if (messages == null)
            {
                return;
            }

            if (!GetStatus())
            {
                AddInitializationHelpMessage(messages, GetErrorMessage(), MessageType.Warning);
                return;
            }

            if (state != null && !state.IsValid())
            {
                AddInitializationHelpMessage(messages,
                    "Configured bones could not be resolved on the preview rigs. Rebuild the profile rigs or remap the feature.",
                    MessageType.Warning);
            }
        }

        public virtual void OnRigUpdated()
        {
        }

        public virtual void OnFeatureAdded()
        {
        }

        public virtual void MapChains()
        {

        }

        public virtual string GetDisplayName()
        {
            return GetType().Name;
        }

        protected void AddInitializationNotificationMessage(List<RetargetFeatureInitializationMessage> messages,
            string message, MessageType type = MessageType.Info)
        {
            AddInitializationMessage(messages, message, type, RetargetFeatureInitializationMessageChannel.Notification);
        }

        protected void AddInitializationHelpMessage(List<RetargetFeatureInitializationMessage> messages, string message,
            MessageType type = MessageType.Info)
        {
            AddInitializationMessage(messages, message, type, RetargetFeatureInitializationMessageChannel.Help);
        }

        private void AddInitializationMessage(List<RetargetFeatureInitializationMessage> messages, string message,
            MessageType type, RetargetFeatureInitializationMessageChannel channel)
        {
            if (messages == null)
            {
                return;
            }

            string normalizedMessage = NormalizeInitializationMessage(message);
            if (string.IsNullOrEmpty(normalizedMessage))
            {
                return;
            }

            messages.Add(new RetargetFeatureInitializationMessage(channel, type,
                this, GetInitializationMessagePrefix(), normalizedMessage));
        }

        private string GetInitializationMessagePrefix()
        {
            string displayName = NormalizeInitializationMessage(GetDisplayName());
            return string.IsNullOrEmpty(displayName) ? GetType().Name : displayName;
        }

        private static string NormalizeInitializationMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        }
#endif
        public KRigElement[] GetHierarchy()
        {
            return targetRig == null ? null : targetRig.GetHierarchy();
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(RetargetFeatureWeightAttribute))]
    public class RetargetFeatureWeightDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedObject serializedObject = property.serializedObject;
            SerializedProperty modeProperty =
                serializedObject.FindProperty(nameof(RetargetFeature.featureWeightMode));
            SerializedProperty curveProperty =
                serializedObject.FindProperty(nameof(RetargetFeature.featureWeightCurve));

            if (modeProperty != null && curveProperty != null &&
                DrawModeMenu(position, property, modeProperty, curveProperty))
            {
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            if (modeProperty != null && curveProperty != null &&
                modeProperty.enumValueIndex == (int) RetargetFeatureWeightMode.Curve)
            {
                EditorGUI.PropertyField(position, curveProperty, label);
            }
            else
            {
                EditorGUI.Slider(position, property, 0f, 1f, label);
            }

            EditorGUI.EndProperty();
        }

        private static bool DrawModeMenu(Rect position, SerializedProperty weightProperty,
            SerializedProperty modeProperty, SerializedProperty curveProperty)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.ContextClick || !position.Contains(currentEvent.mousePosition))
            {
                return false;
            }

            RetargetFeatureWeightMode currentMode =
                (RetargetFeatureWeightMode) modeProperty.enumValueIndex;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Value"), currentMode == RetargetFeatureWeightMode.Value,
                () => SetMode(weightProperty, modeProperty, curveProperty, RetargetFeatureWeightMode.Value));
            menu.AddItem(new GUIContent("Curve"), currentMode == RetargetFeatureWeightMode.Curve,
                () => SetMode(weightProperty, modeProperty, curveProperty, RetargetFeatureWeightMode.Curve));
            menu.ShowAsContext();
            currentEvent.Use();
            return true;
        }

        private static void SetMode(SerializedProperty weightProperty, SerializedProperty modeProperty,
            SerializedProperty curveProperty, RetargetFeatureWeightMode mode)
        {
            if (modeProperty.enumValueIndex == (int) mode)
            {
                return;
            }

            if (mode == RetargetFeatureWeightMode.Curve &&
                (curveProperty.animationCurveValue == null || curveProperty.animationCurveValue.length == 0))
            {
                curveProperty.animationCurveValue =
                    AnimationCurve.Constant(0f, 1f, Mathf.Clamp01(weightProperty.floatValue));
            }

            if (mode == RetargetFeatureWeightMode.Curve && weightProperty.floatValue <= 0f)
            {
                weightProperty.floatValue = 1f;
            }

            modeProperty.enumValueIndex = (int) mode;
            modeProperty.serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
