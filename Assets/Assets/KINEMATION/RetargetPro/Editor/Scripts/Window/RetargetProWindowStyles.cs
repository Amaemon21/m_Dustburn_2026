// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    internal sealed class RetargetProWindowStyles
    {
        private static readonly Color SectionBackgroundColorValue = new Color(0.18f, 0.18f, 0.18f, 1f);

        private GUIStyle _overlayLabelStyle;
        private GUIStyle _metricsLabelStyle;
        private GUIStyle _metricsOverlayLabelStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _windowHeaderStyle;
        private GUIStyle _compactFieldLabelStyle;
        private GUIStyle _rightAlignedMiniLabelStyle;
        private GUIStyle _toolbarNoPaddingStyle;
        private GUIStyle _toolbarButtonNoPaddingStyle;
        private GUIStyle _messageBoxStyle;
        private GUIStyle _messageHeaderStyle;
        private GUIStyle _messageTimestampStyle;
        private GUIStyle _messageBodyStyle;
        private GUIStyle _messageLinkStyle;

        private Texture2D _messageBoxBackgroundTexture;
        private GUIContent _messageInfoIconContent;
        private GUIContent _messageWarningIconContent;
        private GUIContent _messageErrorIconContent;
        private GUIContent _swapSourceTargetContent;
        private GUIContent _playPreviewContent;
        private GUIContent _pausePreviewContent;
        private GUIContent _loopPreviewContent;
        private GUIContent _pauseLoopPreviewContent;
        private GUIContent _sceneCameraViewIconContent;
        private GUIContent _cursorToggleLabelContent;
        private GUIContent _resetCursorPositionLabelContent;
        private GUIContent _resetCursorRotationLabelContent;
        private GUIContent _cameraToCursorLabelContent;
        private GUIContent _restoreCameraLabelContent;
        private GUIContent _fullScreenContent;
        private GUIContent _boneGizmoContent;
        private GUIContent _transformHandleGizmoContent;

        public GUIStyle OverlayLabelStyle => _overlayLabelStyle ??= new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12
        };

        public GUIStyle MetricsLabelStyle => _metricsLabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 10
        };

        public GUIStyle MetricsOverlayLabelStyle => _metricsOverlayLabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.UpperRight,
            fontSize = 9
        };

        public GUIStyle SectionStyle => _sectionStyle ??= CreateSectionStyle();

        public Color SectionBackgroundColor => SectionBackgroundColorValue;

        public RectOffset SectionPadding => SectionStyle.padding;

        public GUIStyle SectionHeaderStyle => _sectionHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };

        public GUIStyle WindowHeaderStyle => _windowHeaderStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            wordWrap = false
        };

        public GUIStyle CompactFieldLabelStyle => _compactFieldLabelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel);

        public GUIStyle RightAlignedMiniLabelStyle => _rightAlignedMiniLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip
        };

        public GUIStyle ToolbarNoPaddingStyle => _toolbarNoPaddingStyle ??= new GUIStyle(EditorStyles.toolbar)
        {
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };

        public GUIStyle ToolbarButtonNoPaddingStyle => _toolbarButtonNoPaddingStyle ??=
            new GUIStyle(EditorStyles.toolbarButton)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };

        public GUIStyle MessageBoxStyle => _messageBoxStyle ??= CreateMessageBoxStyle();

        public GUIStyle MessageHeaderStyle => _messageHeaderStyle ??= CreateMessageHeaderStyle();

        public GUIStyle MessageTimestampStyle => _messageTimestampStyle ??= CreateMessageTimestampStyle();

        public GUIStyle MessageBodyStyle => _messageBodyStyle ??= CreateMessageBodyStyle();

        public GUIStyle MessageLinkStyle => _messageLinkStyle ??= CreateMessageLinkStyle();

        public GUIContent GetMessageStatusIcon(MessageType type)
        {
            return type switch
            {
                MessageType.Warning => _messageWarningIconContent ??=
                    CreateIconContent("Warning", string.Empty, "console.warnicon.sml", "d_console.warnicon.sml"),
                MessageType.Error => _messageErrorIconContent ??=
                    CreateIconContent("Error", string.Empty, "console.erroricon.sml", "d_console.erroricon.sml"),
                _ => _messageInfoIconContent ??=
                    CreateIconContent("Info", string.Empty, "console.infoicon.sml", "d_console.infoicon.sml")
            };
        }

        public GUIContent SwapSourceTargetContent => _swapSourceTargetContent ??=
            CreateIconContent("Swap Source & Target", "Swap", "RotateTool", "d_RotateTool");

        public GUIContent SceneCameraViewIconContent => _sceneCameraViewIconContent ??=
            CreateIconContent("Camera view marker.", "Camera",
                "d_VideoPlayer Icon", "d_Camera Gizmo", "d_Camera Icon", "VideoPlayer Icon", "Camera Gizmo",
                "Camera Icon", "Camera1");

        public GUIContent CursorToggleLabelContent => _cursorToggleLabelContent ??=
            new GUIContent("Cursor", "Toggle the camera view transform handle.");

        public GUIContent ResetCursorPositionLabelContent => _resetCursorPositionLabelContent ??=
            new GUIContent("Pos", "Reset the camera view position to the origin.");

        public GUIContent ResetCursorRotationLabelContent => _resetCursorRotationLabelContent ??=
            new GUIContent("Rot", "Reset the camera view rotation to identity.");
        
        public GUIContent FullScreenContent => _fullScreenContent ??=
            CreateIconContent("Toggle full-screen mode.", "⛶", 
                "d_ScaleTool");

        public GUIContent BoneGizmoContent => _boneGizmoContent ??=
            CreateIconContent("Toggle selected feature bone rendering.", "Bone", 
                "Avatar Icon", "d_Avatar Icon");

        public GUIContent TransformHandleGizmoContent => _transformHandleGizmoContent ??=
            CreateIconContent("Toggle selected feature transform handles.", "Gizmo", 
                "Transform Icon");

        public GUIContent GetPlayPauseContent(bool isPlaying)
        {
            return isPlaying
                ? _pausePreviewContent ??= CreateIconContent("Pause playback", "||", "PauseButton")
                : _playPreviewContent ??= CreateIconContent("Play once", ">", "PlayButton");
        }

        public GUIContent GetLoopContent(bool isLooping)
        {
            return isLooping
                ? _pauseLoopPreviewContent ??= CreateIconContent("Pause looping playback", "Loop",
                    "preAudioLoopOff", "d_preAudioLoopOff")
                : _loopPreviewContent ??= CreateIconContent("Play in loop", "Loop",
                    "preAudioLoopOff", "d_preAudioLoopOff");
        }

        public GUIContent GetCameraModeLabelContent(bool restoreCamera)
        {
            if (restoreCamera)
            {
                return _restoreCameraLabelContent ??=
                    new GUIContent("Camera",
                        "Return the preview camera to the pose it had before snapping to the camera view.");
            }

            return _cameraToCursorLabelContent ??=
                new GUIContent("Camera", "Snap the preview camera to the camera view position and orientation.");
        }

        private static GUIContent CreateIconContent(string tooltip, string fallbackText, params string[] iconNames)
        {
            for (int i = 0; i < iconNames.Length; i++)
            {
                GUIContent iconContent = EditorGUIUtility.IconContent(iconNames[i]);
                if (iconContent != null && iconContent.image != null)
                {
                    return new GUIContent(iconContent.image, tooltip);
                }
            }

            return new GUIContent(fallbackText, tooltip);
        }

        private static GUIStyle CreateSectionStyle()
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 6, 8),
                margin = new RectOffset(0, 0, 0, 8)
            };
            ApplySolidBackground(style, Texture2D.whiteTexture);
            return style;
        }

        private static void ApplySolidBackground(GUIStyle style, Texture2D backgroundTexture)
        {
            style.normal.background = backgroundTexture;
            style.hover.background = backgroundTexture;
            style.active.background = backgroundTexture;
            style.focused.background = backgroundTexture;
            style.onNormal.background = backgroundTexture;
            style.onHover.background = backgroundTexture;
            style.onActive.background = backgroundTexture;
            style.onFocused.background = backgroundTexture;
        }

        private GUIStyle CreateMessageBoxStyle()
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 6),
                border = new RectOffset(6, 6, 6, 6)
            };

            Texture2D backgroundTexture = GetMessageBoxBackgroundTexture();
            ApplySolidBackground(style, backgroundTexture);
            return style;
        }

        private static GUIStyle CreateMessageHeaderStyle()
        {
            Color color = new Color(0.96f, 0.97f, 0.98f, 1f);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private static GUIStyle CreateMessageTimestampStyle()
        {
            Color color = new Color(0.88f, 0.9f, 0.93f, 0.95f);
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private static GUIStyle CreateMessageBodyStyle()
        {
            Color color = new Color(0.95f, 0.96f, 0.98f, 1f);
            var style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = false
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private static GUIStyle CreateMessageLinkStyle()
        {
            Color color = new Color(0.33f, 0.67f, 0.98f, 1f);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                wordWrap = false,
                stretchWidth = false,
                clipping = TextClipping.Clip,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private Texture2D GetMessageBoxBackgroundTexture()
        {
            if (_messageBoxBackgroundTexture != null)
            {
                return _messageBoxBackgroundTexture;
            }

            const int size = 24;
            const float radius = 6f;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "RetargetProMessageBoxBackground",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            float halfSize = size * 0.5f;
            float innerExtent = halfSize - radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - halfSize;
                    float py = y + 0.5f - halfSize;
                    float qx = Mathf.Abs(px) - innerExtent;
                    float qy = Mathf.Abs(py) - innerExtent;
                    float outsideDistance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float insideDistance = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float signedDistance = outsideDistance + insideDistance - radius;
                    float alpha = Mathf.Clamp01(0.5f - signedDistance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _messageBoxBackgroundTexture = texture;
            return _messageBoxBackgroundTexture;
        }
    }
}