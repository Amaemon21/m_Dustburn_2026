#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Repetitionless.Editor.Painter
{
    public class PainterBrushPreview
    {
        private class FadingPopupData
        {
            public string Label;
            public Color BackgroundColor;
            public bool AlphaToColour;
            public Vector2 PositionOffset;

            public double DisplayUntil;
            public double FadeDuration;
        }

        private static readonly Vector2 POPUP_MOUSE_OFFSET = new Vector2(20, 0); 
        private static readonly Vector2 POPUP_PADDING = new Vector2(5, 3);

        private static readonly Color ROTATION_LINE_COLOUR = Color.red;

        private GUIStyle _popupLabelStyle;
        private bool _popupStylesSetup = false;

        private List<FadingPopupData> _fadingPopups = new List<FadingPopupData>();

        // Brush
        public void DrawBrush(RaycastHit mouseHit, SceneView sceneView, float radius, float innerRadius, float rotationDegrees)
        {
            // Outer circle
            Handles.DrawWireDisc(mouseHit.point, mouseHit.normal, radius, 3);

            // Smoothness circle
            innerRadius *= radius;
            Handles.DrawWireDisc(mouseHit.point, mouseHit.normal, innerRadius, 1);

            if (rotationDegrees >= 0) {
                // Same calculations as shader when sampling brush
                Vector3 normal = mouseHit.normal.normalized;
                Vector3 up = Mathf.Abs(normal.y) < 0.999f ? Vector3.up : Vector3.right;
                Vector3 tangent = Vector3.Cross(up, normal).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent);

                float rotationRadians = -rotationDegrees * Mathf.Deg2Rad;
                Vector3 lineDir = Mathf.Sin(rotationRadians) * tangent + Mathf.Cos(rotationRadians) * bitangent;

                Color prevColour = Handles.color;
                Handles.color = ROTATION_LINE_COLOUR;
                Handles.DrawLine(mouseHit.point, mouseHit.point + lineDir * radius);
                Handles.color = prevColour;
            }

            sceneView.Repaint();
        }

        // Mouse Popups
        private void SetupNotificationGUIStyles()
        {
            _popupStylesSetup = true;

            // Notification label
            _popupLabelStyle = new GUIStyle(GUI.skin.label);
            _popupLabelStyle.fontSize = 14;
            _popupLabelStyle.fontStyle = FontStyle.Bold;
        }

        // Only required if using fading notifications
        // Must be called every scene gui call
        public void OnSceneGUI()
        {
            // Loop backwards to allow removing popups
            for (int i = _fadingPopups.Count - 1; i >= 0; i--) {
                UpdateFadingPopup(_fadingPopups[i]);
            }
        }

        public void DrawMousePopup(string label, Color backgroundColor, bool alphaToColour = false)
        {
            DrawMousePopup(label, backgroundColor, alphaToColour, Vector2.zero);
        }

        public void DrawMousePopup(string label, Color backgroundColor, bool alphaToColour, Vector2 positionOffset)
        {
            if (!_popupStylesSetup)
                SetupNotificationGUIStyles();

            Handles.BeginGUI();

            // Calculate size based on contents
            Vector2 size = _popupLabelStyle.CalcSize(new GUIContent(label));
            size.x += POPUP_PADDING.x * 2;
            size.y += POPUP_PADDING.y * 2;

            Vector2 mousePos = Event.current.mousePosition;
            Rect rect = new Rect(mousePos.x + POPUP_MOUSE_OFFSET.x + positionOffset.x, mousePos.y + POPUP_MOUSE_OFFSET.y + positionOffset.y, size.x, size.y);

            Color prevColour = GUI.color;
            if (alphaToColour) {
                Color newColour = prevColour;
                newColour.a = backgroundColor.a;
                GUI.color = newColour;
            }

            EditorGUI.DrawRect(rect, backgroundColor);

            Rect contentRect = rect;
            contentRect.x += POPUP_PADDING.x;
            contentRect.y += POPUP_PADDING.y;
            contentRect.width -= POPUP_PADDING.x * 2;
            contentRect.height -= POPUP_PADDING.y * 2;

            GUI.Label(contentRect, label, _popupLabelStyle);

            if (alphaToColour)
                GUI.color = prevColour;

            Handles.EndGUI();
        }

        public void AddFadingPopup(double displayUntil, double fadeDuration, string label, Color backgroundColor, bool alphaToColour = false)
        {
            AddFadingPopup(displayUntil, fadeDuration, label, backgroundColor, alphaToColour, Vector2.zero);
        }

        public void AddFadingPopup(double displayUntil, double fadeDuration, string label, Color backgroundColor, bool alphaToColour, Vector2 positionOffset)
        {
            _fadingPopups.Add(new FadingPopupData {
                Label = label,
                BackgroundColor = backgroundColor,
                AlphaToColour = alphaToColour,
                PositionOffset = positionOffset,
                DisplayUntil = displayUntil,
                FadeDuration = fadeDuration
            });
        }

        private void UpdateFadingPopup(FadingPopupData data)
        {
            double timeRemaining = data.DisplayUntil - EditorApplication.timeSinceStartup;
            if (timeRemaining <= 0) {
                _fadingPopups.Remove(data);
                return;
            }

            float alpha = timeRemaining < data.FadeDuration ? Mathf.Clamp01((float)(timeRemaining / data.FadeDuration)) : 1.0f;

            Color colour = data.BackgroundColor;
            colour.a = alpha;

            DrawMousePopup(data.Label, colour, data.AlphaToColour, data.PositionOffset);
        }

        public void ClearFadingPopups()
        {
            _fadingPopups.Clear();
        }
    }
}
#endif