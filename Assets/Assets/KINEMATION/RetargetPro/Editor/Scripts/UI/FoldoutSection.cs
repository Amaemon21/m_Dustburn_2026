// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.UI
{
    internal sealed class FoldoutSection
    {
        private const string StateKeyPrefix = "KINEMATION.RetargetPro.Editor.Section.";
        private const float HeaderActionSpacing = 4f;
        private const float FoldoutArrowWidth = 13f;
        private const float FoldoutLabelSpacing = 2f;
        private const float BodySpacing = 2f;

        private readonly string _stateKey;
        private readonly bool _defaultExpanded;

        public string Title { get; }

        public FoldoutSection(string ownerKey, string sectionKey, string title, bool defaultExpanded = true)
        {
            Title = title;
            _defaultExpanded = defaultExpanded;
            _stateKey = $"{StateKeyPrefix}{ownerKey}.{sectionKey}";
        }

        private bool IsExpanded
        {
            get => SessionState.GetInt(_stateKey, _defaultExpanded ? 1 : 0) != 0;
            set => SessionState.SetInt(_stateKey, value ? 1 : 0);
        }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
        }

        public float GetHeight(GUIStyle sectionStyle, float bodyHeight)
        {
            float totalHeight = sectionStyle.padding.vertical + EditorGUIUtility.singleLineHeight;
            if (!IsExpanded)
            {
                return totalHeight;
            }

            return totalHeight + BodySpacing + Mathf.Max(0f, bodyHeight);
        }

        public bool DrawLayout(GUIStyle sectionStyle, GUIStyle headerStyle, Color backgroundColor, Action drawBody,
            Action<Rect> drawHeaderAction = null, float headerActionWidth = 0f)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = backgroundColor;
            EditorGUILayout.BeginVertical(sectionStyle);
            GUI.backgroundColor = previousBackgroundColor;

            try
            {
                bool expanded = DrawHeader(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight),
                    headerStyle, drawHeaderAction, headerActionWidth);
                if (!expanded)
                {
                    return false;
                }

                EditorGUILayout.Space(BodySpacing);
                drawBody?.Invoke();
                return true;
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        public bool Draw(Rect area, GUIStyle sectionStyle, GUIStyle headerStyle, Color backgroundColor,
            Action<Rect> drawBody, Action<Rect> drawHeaderAction = null, float headerActionWidth = 0f)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = backgroundColor;
            GUI.Box(area, GUIContent.none, sectionStyle);
            GUI.backgroundColor = previousBackgroundColor;

            RectOffset padding = sectionStyle.padding;
            Rect contentRect = new Rect(area.x + padding.left, area.y + padding.top,
                Mathf.Max(1f, area.width - padding.horizontal), Mathf.Max(1f, area.height - padding.vertical));
            Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, EditorGUIUtility.singleLineHeight);

            bool expanded = DrawHeader(headerRect, headerStyle, drawHeaderAction, headerActionWidth);
            if (!expanded)
            {
                return false;
            }

            float contentY = headerRect.yMax + BodySpacing;
            Rect bodyRect = new Rect(contentRect.x, contentY, contentRect.width,
                Mathf.Max(1f, contentRect.yMax - contentY));
            drawBody?.Invoke(bodyRect);
            return true;
        }

        private bool DrawHeader(Rect headerRect, GUIStyle headerStyle, Action<Rect> drawHeaderAction,
            float headerActionWidth)
        {
            Rect titleRect = GetTitleRect(headerRect, drawHeaderAction, headerActionWidth);
            Rect actionRect = GetActionRect(headerRect, drawHeaderAction, headerActionWidth);

            bool expanded = IsExpanded;
            DrawFoldoutArrow(titleRect, expanded);

            Rect labelRect = new Rect(titleRect.x + FoldoutArrowWidth + FoldoutLabelSpacing, titleRect.y,
                Mathf.Max(1f, titleRect.width - FoldoutArrowWidth - FoldoutLabelSpacing), titleRect.height);
            GUI.Label(labelRect, Title, headerStyle);

            if (GUI.Button(titleRect, GUIContent.none, GUIStyle.none))
            {
                expanded = !expanded;
                IsExpanded = expanded;
            }

            drawHeaderAction?.Invoke(actionRect);
            return expanded;
        }

        private static void DrawFoldoutArrow(Rect titleRect, bool expanded)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect arrowRect = new Rect(titleRect.x, titleRect.y, FoldoutArrowWidth, titleRect.height);
            EditorStyles.foldout.Draw(arrowRect, GUIContent.none, false, false, expanded, false);
        }

        private static Rect GetTitleRect(Rect headerRect, Action<Rect> drawHeaderAction, float headerActionWidth)
        {
            if (drawHeaderAction == null || headerActionWidth <= 0f)
            {
                return headerRect;
            }

            return new Rect(headerRect.x, headerRect.y,
                Mathf.Max(1f, headerRect.width - headerActionWidth - HeaderActionSpacing), headerRect.height);
        }

        private static Rect GetActionRect(Rect headerRect, Action<Rect> drawHeaderAction, float headerActionWidth)
        {
            if (drawHeaderAction == null || headerActionWidth <= 0f)
            {
                return default;
            }

            return new Rect(headerRect.xMax - headerActionWidth, headerRect.y, headerActionWidth, headerRect.height);
        }
    }
}