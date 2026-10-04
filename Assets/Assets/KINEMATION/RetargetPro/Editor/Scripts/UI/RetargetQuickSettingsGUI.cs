// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Runtime;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.UI
{
    internal static class RetargetQuickSettingsGUI
    {
        public static RetargetProfile DrawTargetProfileField(RetargetAnimBaker baker)
        {
            if (baker == null)
            {
                return null;
            }

            Rect fieldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            return (RetargetProfile)EditorGUI.ObjectField(fieldRect, new GUIContent("Target Profile"),
                baker.retargetProfile, typeof(RetargetProfile), false);
        }

        public static void DrawClipAndSaveSettings(RetargetAnimBaker baker, bool includeUtilityButtons,
            Action onFrameModels = null, Action onResetCamera = null, string bakeButtonLabel = null,
            bool canBake = false, Action onBake = null)
        {
            if (baker == null)
            {
                return;
            }

            Rect frameRateRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            baker.FrameRate = EditorGUI.Slider(frameRateRect, "Frame Rate", baker.FrameRate, 24f, 240f);

            baker.DrawRigTypePopup();
            baker.DrawBakeOutputPopup();
            baker.DrawRootRotationOffsetField();
            baker.DrawLoopCountField();

            DrawClipToggleRow(baker);
            DrawSaveFolderRow(baker, includeUtilityButtons, onFrameModels, onResetCamera);
            DrawBakeButton(bakeButtonLabel, canBake, onBake);
        }

        private static void DrawClipToggleRow(RetargetAnimBaker baker)
        {
            const float spacing = 4f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float toggleWidth = Mathf.Max(32f, (rowRect.width - spacing) * 0.5f);

            Rect copyRect = new Rect(rowRect.x, rowRect.y, toggleWidth, rowRect.height);
            Rect rootMotionRect = new Rect(copyRect.xMax + spacing, rowRect.y,
                Mathf.Max(32f, rowRect.xMax - (copyRect.xMax + spacing)), rowRect.height);

            baker.CopyClipSettings = EditorGUI.ToggleLeft(copyRect, "Copy Clip Settings", baker.CopyClipSettings);
            baker.UseRootMotion = EditorGUI.ToggleLeft(rootMotionRect, "Use Root Motion", baker.UseRootMotion);
        }

        private static void DrawSaveFolderRow(RetargetAnimBaker baker, bool includeUtilityButtons,
            Action onFrameModels, Action onResetCamera)
        {
            const float buttonWidth = 92f;
            const float spacing = 4f;

            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = EditorGUI.PrefixLabel(rowRect, new GUIContent("Save Folder"));

            using (new EditorGUI.DisabledScope(baker.retargetProfile == null))
            {
                if (GUI.Button(fieldRect, baker.SavePath, EditorStyles.textField))
                {
                    baker.PromptSaveFolderSelection();
                }
            }

            if (!includeUtilityButtons)
            {
                return;
            }

            GUILayout.Space(2f);
            Rect buttonRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            float buttonsWidth = buttonWidth * 2f + spacing;
            Rect frameRect = new Rect(buttonRowRect.xMax - buttonsWidth, buttonRowRect.y, buttonWidth,
                buttonRowRect.height);
            Rect resetRect = new Rect(frameRect.xMax + spacing, buttonRowRect.y, buttonWidth, buttonRowRect.height);

            EditorGUI.BeginDisabledGroup(onFrameModels == null);
            if (GUI.Button(frameRect, "Frame Models", EditorStyles.miniButtonLeft))
            {
                onFrameModels?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(onResetCamera == null);
            if (GUI.Button(resetRect, "Reset Camera", EditorStyles.miniButtonRight))
            {
                onResetCamera?.Invoke();
            }
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawBakeButton(string bakeButtonLabel, bool canBake, Action onBake)
        {
            if (onBake == null)
            {
                return;
            }

            string buttonLabel = string.IsNullOrWhiteSpace(bakeButtonLabel) ? "Bake Animation" : bakeButtonLabel;
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button(buttonLabel))
                {
                    onBake.Invoke();
                }
            }
        }
    }
}