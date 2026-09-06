// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Editor.Scripts.Preview;
using KINEMATION.RetargetPro.Editor.Scripts.UI;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    public class RetargetProWindow : EditorWindow
    {
        private const string WindowTitle = "Retarget Pro";
        private const string WindowMenuPath = "Window/KINEMATION/Retarget Pro";
        private const float OrbitSpeed = 0.35f;
        private const float PanSpeedFactor = 0.0025f;
        private const float ZoomSpeed = 0.06f;
        private const float ShiftPrecisionInputScale = 0.2f;
        private const float MinPitch = -85f;
        private const float MaxPitch = 85f;
        private const float DefaultPreviewDistance = 6f;
        private const float DefaultFieldOfView = 35f;
        private const float DefaultNearClipPlane = 0.01f;
        private const float MinPreviewFieldOfView = 5f;
        private const float MaxPreviewFieldOfView = 120f;
        private const float MinPreviewNearClipPlane = 0.001f;
        private const float MaxPreviewNearClipPlane = 10f;
        private const float MinPreviewDistance = 0.2f;
        private const float DefaultPreviewMaxDistance = 120f;
        private const float DefaultPreviewYaw = 130f;
        private const float DefaultPreviewPitch = 20f;
        private const int HdrpDx12PreviewTextureRefreshInterval = 120;
        private const int MaxTabMessages = 64;

        private const float DefaultPanelWidthRatio = 0.3f;
        private const float MinPanelWidthRatio = 0.25f;
        private const float MaxPanelWidthRatio = 0.55f;
        private const float PanelMinWidth = 220f;
        private const float SceneMinWidth = 320f;
        private const float SplitterWidth = 1f;
        private const float SplitterHitWidth = 5f;
        private const float CameraSettingsOverlaySpacing = 6f;
        private const float CameraSettingsOverlayMaxWidth = 460f;
        private const float CameraSettingsFieldStackWidth = 320f;
        private const float PreviewGizmoActionButtonWidth = 24f;
        private const float PreviewOverlayButtonSpacing = 4f;
        private const float PreviewOverlayPanelPadding = 8f;
        private const float PreviewOverlayTextButtonPadding = 12f;
        private const float CameraViewSceneIconSize = 22f;
        private static readonly Color PanelBackground = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        private static readonly Color SplitterColor = new Color(0f, 0f, 0f, 0.34f);
        private const string PreviewHelpText = "LMB: Orbit  RMB/MMB: Pan  Scroll: Zoom  F: Frame";
        private static readonly string[] InfoTabs = { "Notifications", "Help" };

        private static readonly GUIContent PreviewInfoDropdownContent =
            new GUIContent("Info \u25BE", "Show preview control hints.");

        private static readonly GUIContent PreviewCameraSettingsDropdownContent =
            new GUIContent("Camera Settings \u25BE", "Show preview camera settings.");

        private static readonly GUIContent PreviewHelpLabelContent = new GUIContent(PreviewHelpText);

        private static readonly RetargetPreviewTransformIndicator.SizeOptions PreviewTransformIndicatorSizeOptions =
            new(0.5f, 0.1f, 0.08f, 0f);

        private static readonly RetargetPreviewTransformIndicator.ColorOptions PreviewTransformIndicatorColorOptions =
            new(
                new Color(0.91f, 0.28f, 0.22f, 0.95f),
                new Color(0.42f, 0.82f, 0.27f, 0.95f),
                new Color(0.26f, 0.56f, 0.95f, 0.95f),
                new Color(1f, 0.96f, 0.74f, 0.9f));

        public RetargetAnimBaker retargetAnimBaker;

        private List<AnimationClip> _animationClips;
        private string[] _animationClipDisplayNames = Array.Empty<string>();
        private bool _animationClipDisplayNamesDirty = true;
        private int _selectedAnimationIndex;
        private float _startTime;
        private float _endTime;
        private float _timeSlider;

        private Vector2 _scrollPosition;

        private bool _loopPreview;
        private float _lastFrameTime;

        private RetargetProfileWidget _retargetWidget;
        private Exception _lastException;
        private string _lastErrorContext;
        private Vector2 _errorScroll;
        private string _initError;

        private Camera _previewCamera;
        private Light _previewKeyLight;
        private Light _previewFillLight;
        private GameObject _sourceAnchor;
        private GameObject _targetAnchor;
        private RetargetPreviewEnvironment _previewEnvironment;
        private RetargetPreviewBoundsCache _previewBoundsCache;
        private RetargetPreviewMaterialCache _previewMaterialCache;
        private RetargetPreviewTransformIndicator _previewTransformIndicator;

        private RenderTexture _previewTexture;
        private int _previewTextureWidth;
        private int _previewTextureHeight;
        private int _previewTextureRenderCount;
        private bool _refreshHdrpDx12PreviewTexture;

        private PreviewCameraState _previewCameraState = PreviewCameraState.Default;
        private PreviewCameraState _savedPreviewCameraState = PreviewCameraState.Default;
        private SceneCursorState _sceneCursorState = SceneCursorState.Default;
        private SplitViewState _splitViewState = SplitViewState.Default;
        private PreviewInputState _previewInputState;

        private bool _previewLayoutDirty = true;
        private bool _needsFrame = true;
        private bool _preserveCameraOnNextPreviewRefresh;
        private GameObject _lastSourceInstance;
        private GameObject _lastTargetInstance;
        private bool _hasSavedPreviewCameraPose;
        private bool _cameraViewPoseDirty;
        private RetargetProfile _cameraViewPoseDirtyProfile;
        private KTransform _cameraViewPoseBeforeEdit = KTransform.Identity;

        private RetargetProWindowStyles _styles;
        private bool _previewCharactersReady = true;
        private string _previewCharactersError = string.Empty;
        private string _profileStatusMessage = string.Empty;
        private MessageType _profileStatusType = MessageType.Info;
        private int _infoTabIndex;

        private FoldoutSection _profileSetupSection;
        private FoldoutSection _itemSection;
        private FoldoutSection _clipSettingsSection;
        private FoldoutSection _playbackPreviewSection;
        private FoldoutSection _boneChainSection;

        private readonly List<WindowMessage> _notificationMessages = new List<WindowMessage>();
        private readonly List<WindowMessage> _helpMessages = new List<WindowMessage>();
        private PreviewOverlayDropdown _activePreviewOverlayDropdown = PreviewOverlayDropdown.None;
        private bool _windowInitialized;
        private string _lastValidationHelpMessage = string.Empty;
        private string _lastInitHelpMessage = string.Empty;
        private string _lastProfileStatusHelpMessage = string.Empty;
        private string _lastPreviewCharactersHelpMessage = string.Empty;
        private string _lastExceptionHelpMessage = string.Empty;

        private static bool _useManualInitialization = true;

        private bool _drawBoneGizmos;
        private bool _drawTransformHandleGizmos;
        private bool _fullScreenMode;

        private struct PreviewCameraState
        {
            public Vector3 pivot;
            public float distance;
            public float yaw;
            public float pitch;
            public float roll;
            public float fieldOfView;
            public float nearClipPlane;
            public float minDistance;
            public float maxDistance;
            public bool atPivot;

            public static PreviewCameraState Default => new PreviewCameraState
            {
                pivot = Vector3.zero,
                distance = DefaultPreviewDistance,
                yaw = DefaultPreviewYaw,
                pitch = DefaultPreviewPitch,
                roll = 0f,
                fieldOfView = DefaultFieldOfView,
                nearClipPlane = DefaultNearClipPlane,
                minDistance = MinPreviewDistance,
                maxDistance = DefaultPreviewMaxDistance,
                atPivot = false
            };
        }

        private struct SceneCursorState
        {
            public bool enabled;
            public bool initialized;
            public KTransform transform;

            public static SceneCursorState Default => new SceneCursorState
            {
                enabled = false,
                initialized = false,
                transform = KTransform.Identity
            };
        }

        private struct SplitViewState
        {
            public float ratio;
            public bool isResizing;
            public float dragMinRatio;
            public float dragMaxRatio;

            public static SplitViewState Default => new SplitViewState
            {
                ratio = DefaultPanelWidthRatio,
                isResizing = false,
                dragMinRatio = 0f,
                dragMaxRatio = 0f
            };
        }

        private struct PreviewInputState
        {
            public Vector2 lastMousePosition;
            public bool isOrbiting;
            public bool isPanning;
        }

        private enum PreviewOverlayDropdown
        {
            None,
            Info,
            CameraSettings
        }

        private readonly struct WindowMessage
        {
            public readonly MessageType type;
            public readonly RetargetFeature feature;
            public readonly string featureDisplayName;
            public readonly string text;
            public readonly string timestamp;

            public WindowMessage(MessageType type, string text, RetargetFeature feature = null,
                string featureDisplayName = null)
            {
                this.type = type;
                this.feature = feature;
                this.featureDisplayName = string.IsNullOrWhiteSpace(featureDisplayName)
                    ? string.Empty
                    : featureDisplayName.Trim();
                this.text = text;
                timestamp = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        [MenuItem(WindowMenuPath)]
        public static void ShowWindow()
        {
            ShowWindow(null);
        }

        public static RetargetProWindow ShowWindow(RetargetProfile profile)
        {
            _useManualInitialization = false;
            var window = GetWindow<RetargetProWindow>(false, WindowTitle);
            window.InitializeWindow(profile);
            window.Show();
            return window;
        }

        private void OnProfileUpdated(RetargetProfile profile)
        {
            _initError = string.Empty;
            if (retargetAnimBaker.IsInitialized)
            {
                StopRetargetPreview();
            }

            CleanupScenePreviewObjects();
            ResetPreviewInstanceCache();
            RefreshProfileWidget(profile);
            LoadSceneCursorPoseFromProfile(profile);
        }

        private void InitializeWindow(RetargetProfile profile)
        {
            if (_windowInitialized)
            {
                if (profile != null && retargetAnimBaker.retargetProfile != profile)
                {
                    ApplyProfile(profile);
                }

                return;
            }

            _windowInitialized = true;
            ResetRuntimeOnlyState();
            ResetStoredWindowStateToDefaults();
            ResetInfoMessages();

            if (profile != null)
            {
                if (!TryRestoreWindowState(profile)) ApplyProfile(profile);
            }

            _lastException = null;
            _lastErrorContext = null;
            _errorScroll = Vector2.zero;
            _initError = string.Empty;
            _profileStatusMessage = string.Empty;
            _profileStatusType = MessageType.Info;

            AddNotificationMessage("Retarget Pro window opened.");
            Repaint();
        }

        private void ResetRuntimeOnlyState()
        {
            _retargetWidget = null;
            _lastException = null;
            _lastErrorContext = null;
            _errorScroll = Vector2.zero;

            _previewCamera = null;
            _previewKeyLight = null;
            _previewFillLight = null;
            _sourceAnchor = null;
            _targetAnchor = null;
            _previewTexture = null;
            _previewTextureWidth = 0;
            _previewTextureHeight = 0;
            _previewTextureRenderCount = 0;
            _refreshHdrpDx12PreviewTexture = false;

            _previewCameraState.minDistance = MinPreviewDistance;
            _previewCameraState.maxDistance = DefaultPreviewMaxDistance;

            _previewInputState = default;
            _splitViewState.isResizing = false;
            _splitViewState.dragMinRatio = 0f;
            _splitViewState.dragMaxRatio = 0f;

            _previewLayoutDirty = true;
            _needsFrame = true;
            _preserveCameraOnNextPreviewRefresh = false;
            _lastSourceInstance = null;
            _lastTargetInstance = null;

            _previewCharactersReady = true;
            _previewCharactersError = string.Empty;
            _cameraViewPoseDirty = false;
            _cameraViewPoseDirtyProfile = null;
            _cameraViewPoseBeforeEdit = KTransform.Identity;

            _animationClipDisplayNames = null;
        }

        private void ResetStoredWindowStateToDefaults()
        {
            _animationClips.Clear();
            _animationClipDisplayNamesDirty = true;
            _selectedAnimationIndex = 0;
            _startTime = 0f;
            _endTime = 0f;
            _timeSlider = 0f;
            StopPlayback();

            _scrollPosition = Vector2.zero;
            _initError = string.Empty;

            _previewCameraState = PreviewCameraState.Default;
            _splitViewState = SplitViewState.Default;
            _sceneCursorState = SceneCursorState.Default;

            _hasSavedPreviewCameraPose = false;
            _savedPreviewCameraState = PreviewCameraState.Default;

            _infoTabIndex = 0;
            _profileStatusMessage = string.Empty;
            _profileStatusType = MessageType.Info;
        }

        private RetargetWindowBakerState CaptureBakerState()
        {
            var state = new RetargetWindowBakerState();
            if (retargetAnimBaker == null)
            {
                return state;
            }

            state.copyClipSettings = retargetAnimBaker.CopyClipSettings;
            state.useRootMotion = retargetAnimBaker.UseRootMotion;
            state.frameRate = retargetAnimBaker.FrameRate;
            state.loopCount = retargetAnimBaker.LoopCount;
            state.useSourceFrameRateByDefault = retargetAnimBaker.UseSourceFrameRateByDefault;
            state.outputType = retargetAnimBaker.OutputType;
            state.selectedBakerId = retargetAnimBaker.SelectedBakerId;
            state.rootRotationOffsetEuler = retargetAnimBaker.RootRotationOffsetEuler;

            return state;
        }

        private void RestoreBakerState(RetargetWindowBakerState state, bool restoreRigType)
        {
            if (retargetAnimBaker == null || state == null)
            {
                return;
            }

            retargetAnimBaker.CopyClipSettings = state.copyClipSettings;
            retargetAnimBaker.UseRootMotion = state.useRootMotion;
            retargetAnimBaker.FrameRate = state.frameRate;
            retargetAnimBaker.LoopCount = state.loopCount;
            retargetAnimBaker.UseSourceFrameRateByDefault = state.useSourceFrameRateByDefault;
            retargetAnimBaker.OutputType = state.outputType;
            retargetAnimBaker.RootRotationOffsetEuler = state.rootRotationOffsetEuler;
            if (restoreRigType && !string.IsNullOrEmpty(state.selectedBakerId))
            {
                retargetAnimBaker.SelectedBakerId = state.selectedBakerId;
            }
        }

        private RetargetWindowState CaptureWindowState()
        {
            var state = new RetargetWindowState
            {
                profile = RetargetWindowAppState.CaptureObjectReference(GetActiveProfile()),
                baker = CaptureBakerState(),
                selectedAnimationIndex = _selectedAnimationIndex,
                startTime = _startTime,
                endTime = _endTime,
                scrollPosition = _scrollPosition,
                previewPivot = _previewCameraState.pivot,
                previewDistance = _previewCameraState.distance,
                previewYaw = _previewCameraState.yaw,
                previewPitch = _previewCameraState.pitch,
                previewRoll = _previewCameraState.roll,
                previewFieldOfView = _previewCameraState.fieldOfView,
                previewNearClipPlane = _previewCameraState.nearClipPlane,
                previewCameraAtPivot = _previewCameraState.atPivot,
                panelWidthRatio = _splitViewState.ratio,
                sceneCursorEnabled = _sceneCursorState.enabled,
                sceneCursorInitialized = _sceneCursorState.initialized,
                sceneCursorPosition = _sceneCursorState.transform.position,
                sceneCursorRotation = _sceneCursorState.transform.rotation,
                hasSavedPreviewCameraPose = _hasSavedPreviewCameraPose,
                savedPreviewPivot = _savedPreviewCameraState.pivot,
                savedPreviewDistance = _savedPreviewCameraState.distance,
                savedPreviewYaw = _savedPreviewCameraState.yaw,
                savedPreviewPitch = _savedPreviewCameraState.pitch,
                savedPreviewRoll = _savedPreviewCameraState.roll,
                savedPreviewCameraAtPivot = _savedPreviewCameraState.atPivot,
                previewLayoutDirty = _previewLayoutDirty,
                needsFrame = _needsFrame,
                preserveCameraOnNextPreviewRefresh = _preserveCameraOnNextPreviewRefresh,
                infoTabIndex = _infoTabIndex,
                initError = _initError ?? string.Empty,
                profileStatusMessage = _profileStatusMessage ?? string.Empty,
                profileStatusType = (int)_profileStatusType,
            };

            for (int i = 0; i < _animationClips.Count; i++)
            {
                AnimationClip clip = _animationClips[i];
                if (clip == null)
                {
                    continue;
                }

                RetargetWindowObjectReference clipReference =
                    RetargetWindowAppState.CaptureObjectReference(clip);
                if (clipReference.IsValid())
                {
                    state.animationClips.Add(clipReference);
                }
            }

            return state;
        }

        private bool TryRestoreWindowState(RetargetProfile requestedProfile)
        {
            if (!RetargetWindowAppState.TryLoad(out RetargetWindowState state) || state == null)
            {
                return false;
            }

            RetargetProfile savedProfile =
                RetargetWindowAppState.ResolveObjectReference<RetargetProfile>(state.profile);
            RetargetProfile profile = requestedProfile != null ? requestedProfile : savedProfile;
            bool restoreAnimationState = profile == savedProfile;

            retargetAnimBaker?.SetProfile(profile, false);
            RefreshProfileWidget(profile);
            LoadSceneCursorPoseFromProfile(profile);
            RestoreBakerState(state.baker, restoreAnimationState);

            _animationClips.Clear();
            if (restoreAnimationState)
            {
                var seenClips = new HashSet<AnimationClip>();
                _selectedAnimationIndex = 0;
                if (state.animationClips != null)
                {
                    for (int i = 0; i < state.animationClips.Count; i++)
                    {
                        AnimationClip clip =
                            RetargetWindowAppState.ResolveObjectReference<AnimationClip>(
                                state.animationClips[i]);
                        AddUniqueAnimationClip(clip, seenClips, _animationClips);
                    }

                    _selectedAnimationIndex = Mathf.Clamp(state.selectedAnimationIndex, 0,
                        Mathf.Max(0, _animationClips.Count - 1));
                }
            }
            else
            {
                _selectedAnimationIndex = 0;
            }

            AnimationClip activeAnimation = GetActiveAnimation();
            float clipLength = activeAnimation != null ? Mathf.Max(0f, activeAnimation.length) : 0f;
            _startTime = Mathf.Clamp(state.startTime, 0f, clipLength);
            _endTime = Mathf.Clamp(state.endTime, _startTime, clipLength);
            _animationClipDisplayNamesDirty = true;

            _scrollPosition = state.scrollPosition;
            _previewCameraState = new PreviewCameraState
            {
                pivot = state.previewPivot,
                distance = state.previewDistance,
                yaw = state.previewYaw,
                pitch = state.previewPitch,
                roll = state.previewRoll,
                fieldOfView = state.previewFieldOfView,
                nearClipPlane = state.previewNearClipPlane,
                minDistance = MinPreviewDistance,
                maxDistance = DefaultPreviewMaxDistance,
                atPivot = state.previewCameraAtPivot
            };
            _splitViewState.ratio = state.panelWidthRatio;

            _sceneCursorState.enabled = state.sceneCursorEnabled;
            _sceneCursorState.initialized = state.sceneCursorInitialized;
            _sceneCursorState.transform = new KTransform(state.sceneCursorPosition,
                state.sceneCursorInitialized ? state.sceneCursorRotation : Quaternion.identity, Vector3.one);

            _hasSavedPreviewCameraPose = state.hasSavedPreviewCameraPose;
            _savedPreviewCameraState = new PreviewCameraState
            {
                pivot = state.savedPreviewPivot,
                distance = state.savedPreviewDistance,
                yaw = state.savedPreviewYaw,
                pitch = state.savedPreviewPitch,
                roll = state.savedPreviewRoll,
                fieldOfView = _previewCameraState.fieldOfView,
                nearClipPlane = _previewCameraState.nearClipPlane,
                minDistance = _previewCameraState.minDistance,
                maxDistance = _previewCameraState.maxDistance,
                atPivot = state.savedPreviewCameraAtPivot
            };

            _previewLayoutDirty = state.previewLayoutDirty;
            _needsFrame = state.needsFrame;
            _preserveCameraOnNextPreviewRefresh = state.preserveCameraOnNextPreviewRefresh || !_needsFrame;
            _infoTabIndex = Mathf.Clamp(state.infoTabIndex, 0, InfoTabs.Length - 1);
            _initError = state.initError ?? string.Empty;
            _profileStatusMessage = state.profileStatusMessage ?? string.Empty;
            _profileStatusType = Enum.IsDefined(typeof(MessageType), state.profileStatusType)
                ? (MessageType)state.profileStatusType
                : MessageType.Info;

            if (retargetAnimBaker != null && retargetAnimBaker.UseSourceFrameRateByDefault)
            {
                retargetAnimBaker.SyncFrameRateFromSource(activeAnimation);
            }

            Repaint();
            return true;
        }

        private void ApplyProfile(RetargetProfile profile)
        {
            RetargetProfile previousProfile = retargetAnimBaker.retargetProfile;
            FlushCameraViewPose();
            retargetAnimBaker.SetProfile(profile, true, out string profileMessage, out MessageType profileMessageType);

            if (previousProfile != profile)
            {
                ClearAnimationPreview();
                OnProfileUpdated(profile);
            }

            ResetPreviewInstanceCache();
            LoadSceneCursorPoseFromProfile(profile);
            AddNotificationMessage(profile != null
                ? $"Assigned profile `{profile.name}`."
                : "Cleared the active Retarget Profile.");
            AddProfileMessage(profileMessage, profileMessageType);
            Repaint();
        }

        public void AddProfileMessage(string message, MessageType type = MessageType.Info)
        {
            string normalizedMessage = NormalizeMessage(message);
            if (string.IsNullOrEmpty(normalizedMessage))
            {
                return;
            }

            _profileStatusMessage = normalizedMessage;
            _profileStatusType = type;

            if (type == MessageType.Warning || type == MessageType.Error)
            {
                _infoTabIndex = 1;
                AddHelpMessage(normalizedMessage, type);
            }
            else
            {
                _infoTabIndex = 0;
                AddNotificationMessage(normalizedMessage, MessageType.Info, true);
            }
        }

        public void AddNotification(string message, MessageType type = MessageType.Info)
        {
            string normalizedMessage = NormalizeMessage(message);
            if (string.IsNullOrEmpty(normalizedMessage))
            {
                return;
            }

            _infoTabIndex = 0;
            AddNotificationMessage(normalizedMessage, type, true);
        }

        private bool TryInitializePreview()
        {
            if (retargetAnimBaker == null || GetActiveAnimation() == null)
            {
                return false;
            }

            if (!retargetAnimBaker.IsInitialized)
            {
                if (!retargetAnimBaker.TryInitializeBaker(out string error))
                {
                    _initError = error;
                    AddHelpMessage(error, MessageType.Error);
                    StopRetargetPreview();
                    return false;
                }

                retargetAnimBaker.RetargetComponent.drawBoneGizmos = _drawBoneGizmos;
                retargetAnimBaker.RetargetComponent.drawTransformHandleGizmos = _drawTransformHandleGizmos;

                CaptureFeatureInitializationMessages();
            }

            _initError = string.Empty;
            return true;
        }

        private void StopPlayback()
        {
            _loopPreview = false;
            _lastFrameTime = 0f;
        }

        private void MarkPreviewDirty(bool preserveCameraState = false)
        {
            _previewLayoutDirty = true;
            if (!preserveCameraState)
            {
                _needsFrame = true;
            }
        }

        private void ResetPreviewInstanceCache(bool preserveCameraState = false)
        {
            _lastSourceInstance = null;
            _lastTargetInstance = null;
            _previewBoundsCache.Invalidate();
            _previewMaterialCache.Cleanup();
            _preserveCameraOnNextPreviewRefresh = preserveCameraState;
            if (!preserveCameraState)
            {
                _sceneCursorState.initialized = false;
            }

            MarkPreviewDirty(preserveCameraState);
        }

        private void StopRetargetPreview()
        {
            bool wasInitialized = retargetAnimBaker != null && retargetAnimBaker.IsInitialized;
            if (retargetAnimBaker != null)
            {
                retargetAnimBaker.UnInitializeBaker();
            }

            StopPlayback();

            if (wasInitialized)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        private bool DrawWindowGUI()
        {
            bool profileWidgetChanged = false;

            if (!string.IsNullOrEmpty(_initError))
            {
                EditorGUILayout.HelpBox(_initError, MessageType.Error);
            }

            retargetAnimBaker.SyncFrameRateFromSource(GetActiveAnimation());

            if (retargetAnimBaker.retargetProfile != null && _retargetWidget == null)
            {
                RefreshProfileWidget(retargetAnimBaker.retargetProfile);
            }

            _profileSetupSection.DrawLayout(_styles.SectionStyle, _styles.SectionHeaderStyle,
                _styles.SectionBackgroundColor, DrawProfileSetupSection);
            _itemSection.DrawLayout(_styles.SectionStyle, _styles.SectionHeaderStyle, _styles.SectionBackgroundColor,
                DrawItemSection);
            bool readyForPlayback = retargetAnimBaker.TryGetValidationMessage(out string validationMessage,
                out MessageType validationType);
            _clipSettingsSection.DrawLayout(_styles.SectionStyle, _styles.SectionHeaderStyle,
                _styles.SectionBackgroundColor, () => DrawClipSettingsSection(readyForPlayback));
            _playbackPreviewSection.DrawLayout(_styles.SectionStyle, _styles.SectionHeaderStyle,
                _styles.SectionBackgroundColor,
                () => DrawPlaybackSection(readyForPlayback, validationMessage, validationType));

            _boneChainSection.DrawLayout(_styles.SectionStyle, _styles.SectionHeaderStyle,
                _styles.SectionBackgroundColor,
                () => profileWidgetChanged = DrawBoneChainSectionBody(profileWidgetChanged),
                _retargetWidget != null ? DrawBoneChainHeaderAction : null, 118f);

            DrawInfoTabs();
            DrawInfoPanel(readyForPlayback, validationMessage, validationType);

            return profileWidgetChanged;
        }

        private void DrawBoneChainHeaderAction(Rect buttonRect)
        {
            if (GUI.Button(buttonRect, "Remap All Chains", EditorStyles.miniButton))
            {
                if (!_retargetWidget.RequestRemapAllChains(out string message))
                {
                    AddNotification(message, MessageType.Warning);
                }
            }
        }

        private bool IsRetargetPreviewActive()
        {
            return retargetAnimBaker != null && retargetAnimBaker.IsInitialized;
        }

        private bool DrawBoneChainSectionBody(bool profileWidgetChanged)
        {
            if (_retargetWidget == null)
            {
                EditorGUILayout.HelpBox("Select a Retarget Profile to edit bone chain mappings.", MessageType.Info);
                return profileWidgetChanged;
            }

            _retargetWidget.SetStructureEditingDisabled(IsRetargetPreviewActive());
            return _retargetWidget.OnGUI();
        }

        private void DrawProfileSetupSection()
        {
            RetargetProfile selectedProfile = RetargetQuickSettingsGUI.DrawTargetProfileField(retargetAnimBaker);

            if (selectedProfile != retargetAnimBaker.retargetProfile)
            {
                _profileStatusMessage = string.Empty;
                ApplyProfile(selectedProfile);
            }

            RetargetProfile profile = retargetAnimBaker.retargetProfile;
            if (profile == null)
            {
                return;
            }

            KRig sourceRig = profile.sourceRig;
            KRig targetRig = profile.targetRig;

            const float spacing = 4f;
            const float swapWidth = 32f;
            float rowHeight = EditorGUIUtility.singleLineHeight * 2f + 2f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
            float fieldWidth = Mathf.Max(32f, (rowRect.width - swapWidth - spacing * 2f) * 0.5f);

            Rect sourceRect = new Rect(rowRect.x, rowRect.y, fieldWidth, rowRect.height);
            Rect swapRect = new Rect(sourceRect.xMax + spacing, rowRect.y, swapWidth, rowRect.height);
            Rect targetRect = new Rect(swapRect.xMax + spacing, rowRect.y,
                Mathf.Max(32f, rowRect.xMax - (swapRect.xMax + spacing)), rowRect.height);

            sourceRig = DrawInlineObjectField(sourceRect, "Source Rig", sourceRig, false);
            targetRig = DrawInlineObjectField(targetRect, "Target Rig", targetRig, false);

            GUIContent swapContent = _styles.SwapSourceTargetContent;
            Rect swapButtonRect = new Rect(swapRect.x + 2f, swapRect.y + EditorGUIUtility.singleLineHeight + 2f,
                Mathf.Max(18f, swapRect.width - 4f), EditorGUIUtility.singleLineHeight);
            if (GUI.Button(swapButtonRect, swapContent, EditorStyles.miniButton))
            {
                _profileStatusMessage = string.Empty;
                retargetAnimBaker.SwapSourceAndTargetProfiles(out string swapMessage, out MessageType swapMessageType);
                AddProfileMessage(string.IsNullOrEmpty(swapMessage)
                    ? "Swapped source and target rig assignments."
                    : swapMessage, swapMessageType);
                profile = retargetAnimBaker.retargetProfile;
                sourceRig = profile != null ? profile.sourceRig : null;
                targetRig = profile != null ? profile.targetRig : null;
            }

            ApplyProfileFieldChanges(UpdateRigAssignments(profile, sourceRig, targetRig));
            ApplyProfileFieldChanges(RetargetProfileFieldsGUI.DrawRootMotionField(profile, false));

            bool canCreateMissingRigs = profile.sourceCharacter != null && profile.targetCharacter != null &&
                                        (profile.sourceRig == null || profile.targetRig == null);
            if (canCreateMissingRigs && GUILayout.Button("Create Missing Rigs"))
            {
                TryCreateMissingRigs(profile);
            }

            if (!string.IsNullOrEmpty(_profileStatusMessage))
            {
                EditorGUILayout.HelpBox(_profileStatusMessage, _profileStatusType);
            }
        }

        private void TryCreateMissingRigs(RetargetProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            bool success = RetargetProfileModelRigUtility.TryEnsureProfileRigs(profile, true, out string message);
            AddProfileMessage(message, success ? MessageType.Info : MessageType.Warning);

            if (success)
            {
                ApplyProfileFieldChanges(new RetargetProfileFieldChangeSet(true, true));
            }
        }

        private void DrawClipSettingsSection(bool readyForPlayback)
        {
            bool canBake = readyForPlayback && GetActiveAnimation() != null;
            RetargetQuickSettingsGUI.DrawClipAndSaveSettings(retargetAnimBaker, false,
                bakeButtonLabel: GetBakeButtonLabel(), canBake: canBake, onBake: BakeSelectedAnimations);
        }

        private string GetBakeButtonLabel()
        {
            return _animationClips.Count > 1 ? $"Bake {_animationClips.Count} Animations" : "Bake Animation";
        }

        private void DrawItemSection()
        {
            RetargetProfile profile = retargetAnimBaker.retargetProfile;
            if (profile == null)
            {
                EditorGUILayout.HelpBox("Select a Retarget Profile to configure a clip item.", MessageType.Info);
                return;
            }

            ApplyProfileFieldChanges(RetargetProfileFieldsGUI.DrawItemFields(profile, false), true);
        }

        private void DrawPlaybackSection(bool readyForPlayback, string validationMessage, MessageType validationType)
        {
            ApplyProfileFieldChanges(DrawReferencePoseFields());
            DrawAnimationSelectionControls();

            AnimationClip activeAnimation = GetActiveAnimation();
            float clipLength = activeAnimation != null ? Mathf.Max(0f, activeAnimation.length) : 0f;

            if (retargetAnimBaker.retargetProfile != null && !string.IsNullOrEmpty(_previewCharactersError))
            {
                EditorGUILayout.HelpBox(_previewCharactersError,
                    _previewCharactersReady ? MessageType.Warning : MessageType.Error);
            }

            if (!readyForPlayback && !string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, validationType);
            }

            bool hasAnimation = activeAnimation != null;
            if (!hasAnimation)
            {
                EditorGUILayout.HelpBox("Select an animation clip to preview or bake.", MessageType.Warning);
            }

            bool canPlay = readyForPlayback && hasAnimation;
            if (!canPlay && retargetAnimBaker.IsInitialized)
            {
                StopRetargetPreview();
            }

            if (_animationClips.Count <= 1)
            {
                bool trimChanged = DrawTimeRangeSlider(clipLength);
                if (trimChanged)
                {
                    _timeSlider = 0f;
                    StopPlayback();
                    RequestPreviewSample();
                }
            }

            using (new EditorGUI.DisabledScope(!canPlay))
            {
                DrawPlaybackTimelineRow();
            }
        }

        private void DrawAnimationSelectionControls()
        {
            AnimationClip previousAnimation = GetActiveAnimation();

            if (_animationClips.Count > 1)
            {
                DrawAnimationSelectionDropdown();
            }
            else
            {
                AnimationClip nextAnimation = DrawCompactObjectField("Animation", previousAnimation, false);
                if (nextAnimation != previousAnimation)
                {
                    if (_animationClips.Count == 0) _animationClips.Add(nextAnimation);
                    _animationClips[0] = nextAnimation;
                    _selectedAnimationIndex = 0;
                }
            }

            DrawAnimationDropArea();

            AnimationClip activeAnimation = GetActiveAnimation();
            if (previousAnimation != activeAnimation)
            {
                _startTime = _timeSlider = 0f;
                _endTime = activeAnimation != null ? Mathf.Max(0f, activeAnimation.length) : 0f;
                RequestPreviewSample();
            }
        }

        private void DrawAnimationSelectionDropdown()
        {
            int currentIndex = Mathf.Clamp(_selectedAnimationIndex, 0, _animationClips.Count - 1);

            string[] options = GetAnimationClipDisplayNames();

            EditorGUILayout.BeginHorizontal();
            int nextIndex = EditorGUILayout.Popup("Preview Clip", currentIndex, options);
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(58f)))
            {
                ClearAnimationPreview();
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            _selectedAnimationIndex = nextIndex;
        }

        private void DrawAnimationDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, GetAnimationDropAreaMessage(), EditorStyles.helpBox);

            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition) ||
                (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
            {
                return;
            }

            bool canAccept =
                TryCollectAnimationClips(DragAndDrop.objectReferences, out List<AnimationClip> droppedClips);
            DragAndDrop.visualMode = canAccept ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (canAccept && evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AppendAnimationClips(droppedClips);
            }

            evt.Use();
        }


        private string GetAnimationDropAreaMessage()
        {
            if (_animationClips.Count > 1)
            {
                return $"{_animationClips.Count} clips loaded. Drop FBX or animation clips here to add more.";
            }

            if (_animationClips.Count == 1)
            {
                return "Drop FBX or animation clips here to add more clips.";
            }

            return "Drag & Drop FBX or animation clips here.";
        }

        private void AppendAnimationClips(IReadOnlyList<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return;
            }

            var merged = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();
            AnimationClip activeAnimation = GetActiveAnimation();

            for (int i = 0; i < _animationClips.Count; i++)
            {
                AddUniqueAnimationClip(_animationClips[i], seen, merged);
            }

            bool addedAny = false;
            for (int i = 0; i < clips.Count; i++)
            {
                addedAny |= AddUniqueAnimationClip(clips[i], seen, merged);
            }

            if (!addedAny)
            {
                return;
            }

            _animationClips.Clear();
            _animationClips.AddRange(merged);
            _animationClipDisplayNamesDirty = true;

            int currentIndex = activeAnimation != null ? _animationClips.IndexOf(activeAnimation) : -1;
            if (currentIndex >= 0)
            {
                _selectedAnimationIndex = currentIndex;
                return;
            }

            _selectedAnimationIndex = 0;
        }

        private void ClearAnimationPreview()
        {
            if (_animationClips.Count == 0)
            {
                return;
            }

            _animationClips.Clear();
            _selectedAnimationIndex = 0;
            _animationClipDisplayNamesDirty = true;
            _startTime = 0f;
            _endTime = 0f;
            _timeSlider = 0f;
            StopPlayback();
        }

        private void BakeSelectedAnimations()
        {
            if (!TryInitializePreview())
            {
                return;
            }

            StopPlayback();
            AddNotificationMessage($"Baking {_animationClips.Count} animation(s).");

            for (int i = 0; i < _animationClips.Count; i++)
            {
                AnimationClip clip = _animationClips[i];
                if (clip == null)
                {
                    continue;
                }

                float clipLength = Mathf.Max(0f, clip.length);
                float clipStart = Mathf.Clamp(_startTime, 0f, clipLength);
                float clipEnd = Mathf.Clamp(_endTime, clipStart, clipLength);

                if (_animationClips.Count > 1)
                {
                    clipStart = 0f;
                    clipEnd = clipLength;
                }

                retargetAnimBaker.BakeAnimation(clip, clipStart, clipEnd);
            }

            StopRetargetPreview();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private string[] GetAnimationClipDisplayNames()
        {
            if (!_animationClipDisplayNamesDirty && _animationClipDisplayNames.Length == _animationClips.Count)
            {
                return _animationClipDisplayNames;
            }

            var names = new string[_animationClips.Count];
            for (int i = 0; i < _animationClips.Count; i++)
            {
                AnimationClip clip = _animationClips[i];
                if (clip == null)
                {
                    names[i] = "<Missing>";
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(clip);
                string assetName = !string.IsNullOrEmpty(assetPath)
                    ? Path.GetFileNameWithoutExtension(assetPath)
                    : "Clip";
                names[i] = $"{clip.name} ({assetName})";
            }

            _animationClipDisplayNames = names;
            _animationClipDisplayNamesDirty = false;
            return _animationClipDisplayNames;
        }

        private static bool TryCollectAnimationClips(UnityEngine.Object[] references, out List<AnimationClip> clips)
        {
            clips = null;
            if (references == null || references.Length == 0)
            {
                return false;
            }

            var result = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();

            for (int i = 0; i < references.Length; i++)
            {
                UnityEngine.Object reference = references[i];
                if (reference is AnimationClip clip)
                {
                    AddUniqueAnimationClip(clip, seen, result);
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(reference);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                {
                    if (subAsset is AnimationClip fbxClip)
                    {
                        AddUniqueAnimationClip(fbxClip, seen, result);
                    }
                }
            }

            if (result.Count == 0)
            {
                return false;
            }

            clips = result;
            return true;
        }

        private static bool AddUniqueAnimationClip(AnimationClip clip, ISet<AnimationClip> seen,
            IList<AnimationClip> clips)
        {
            if (clip == null || seen == null || clips == null || !seen.Add(clip))
            {
                return false;
            }

            clips.Add(clip);
            return true;
        }

        private bool DrawTimeRangeSlider(float clipLength)
        {
            const float fieldWidth = 54f;
            const float elementSpacing = 8f;

            float rowHeight = EditorGUIUtility.singleLineHeight * 2f + 2f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width, EditorGUIUtility.singleLineHeight);
            Rect contentRect = new Rect(rowRect.x, labelRect.yMax + 2f, rowRect.width,
                EditorGUIUtility.singleLineHeight);
            Rect minRect = new Rect(contentRect.x, contentRect.y, fieldWidth, contentRect.height);
            Rect maxRect = new Rect(contentRect.xMax - fieldWidth, contentRect.y, fieldWidth, contentRect.height);
            Rect sliderRect = new Rect(minRect.xMax + elementSpacing, contentRect.y,
                Mathf.Max(12f, contentRect.width - fieldWidth * 2f - elementSpacing * 2f), contentRect.height);

            float nextStart = _startTime;
            float nextEnd = _endTime;

            using (new EditorGUI.DisabledScope(GetActiveAnimation() == null))
            {
                GUI.Label(labelRect, "Clip Time Range", _styles.CompactFieldLabelStyle);
                EditorGUI.BeginChangeCheck();
                float fieldStart = EditorGUI.DelayedFloatField(minRect, nextStart);
                float fieldEnd = EditorGUI.DelayedFloatField(maxRect, nextEnd);
                float sliderStart = nextStart;
                float sliderEnd = nextEnd;

                if (clipLength > 0f)
                {
                    EditorGUI.MinMaxSlider(sliderRect, ref sliderStart, ref sliderEnd, 0f,
                        clipLength);
                }

                if (!EditorGUI.EndChangeCheck())
                {
                    return false;
                }

                bool startFieldChanged = !Mathf.Approximately(fieldStart, _startTime);
                bool endFieldChanged = !Mathf.Approximately(fieldEnd, _endTime);

                nextStart = startFieldChanged ? fieldStart : sliderStart;
                nextEnd = endFieldChanged ? fieldEnd : sliderEnd;
            }

            nextStart = Mathf.Clamp(nextStart, 0f, clipLength);
            nextEnd = Mathf.Clamp(nextEnd, nextStart, clipLength);
            if (Mathf.Approximately(nextStart, _startTime) && Mathf.Approximately(nextEnd, _endTime))
            {
                return false;
            }

            _startTime = nextStart;
            _endTime = nextEnd;
            return true;
        }

        private void DrawPlaybackTimelineRow()
        {
            float clipLength = Mathf.Max(0f, _endTime - _startTime);
            const float poseButtonWidth = 48f;
            const float iconButtonWidth = 28f;
            const float spacing = 4f;
            const float timeWidth = 66f;

            Rect rowRect = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
            Rect poseWideRect = new Rect(rowRect.x, rowRect.y, poseButtonWidth, rowRect.height);
            Rect playWideRect = new Rect(poseWideRect.xMax + spacing, rowRect.y, iconButtonWidth, rowRect.height);
            Rect loopWideRect = new Rect(playWideRect.xMax + spacing, rowRect.y, iconButtonWidth, rowRect.height);
            Rect timeWideRect = new Rect(rowRect.xMax - timeWidth, rowRect.y, timeWidth, rowRect.height);
            float sliderX = loopWideRect.xMax + spacing;
            Rect sliderWideRect = new Rect(sliderX, rowRect.y,
                Mathf.Max(20f, timeWideRect.x - sliderX - spacing), rowRect.height);

            DrawPlaybackTransportButtons(poseWideRect, playWideRect, loopWideRect);

            float sliderMax = Mathf.Max(0.0001f, clipLength);
            float currentValue = Mathf.Clamp(_timeSlider, 0f, clipLength);
            EditorGUI.BeginChangeCheck();
            float nextValue = GUI.HorizontalSlider(sliderWideRect, currentValue, 0f, sliderMax);
            if (EditorGUI.EndChangeCheck())
            {
                _timeSlider = Mathf.Clamp(nextValue, 0f, clipLength);
                StopPlayback();
                RequestPreviewSample();
            }

            GUI.Label(timeWideRect, $"{Mathf.Clamp(_timeSlider, 0f, clipLength):0.00}s",
                _styles.RightAlignedMiniLabelStyle);
        }

        private void DrawPlaybackTransportButtons(Rect poseRect, Rect playRect, Rect loopRect)
        {
            if (GUI.Button(poseRect, "Pose", EditorStyles.miniButton))
            {
                _timeSlider = 0f;
                if (retargetAnimBaker.IsInitialized) StopRetargetPreview();
            }

            bool isPlaying = _lastFrameTime > 0f;
            GUIContent playPauseContent = _styles.GetPlayPauseContent(isPlaying);
            if (GUI.Button(playRect, playPauseContent, EditorStyles.miniButton))
            {
                if (isPlaying)
                {
                    StopPlayback();
                    Repaint();
                }
                else
                {
                    StartPlayback(false);
                }
            }

            bool isLooping = isPlaying && _loopPreview;
            GUIContent loopContent = _styles.GetLoopContent(isLooping);
            bool nextLooping = GUI.Toggle(loopRect, isLooping, loopContent, EditorStyles.miniButton);
            if (nextLooping != isLooping)
            {
                if (nextLooping)
                {
                    StartPlayback(true);
                }
                else
                {
                    StopPlayback();
                }

                Repaint();
            }
        }

        private RetargetProfileFieldChangeSet DrawReferencePoseFields()
        {
            RetargetProfile profile = retargetAnimBaker.retargetProfile;
            if (profile == null)
            {
                return default;
            }

            AnimationClip sourcePose = profile.sourcePose;
            AnimationClip targetPose = profile.targetPose;

            const float spacing = 4f;
            float rowHeight = EditorGUIUtility.singleLineHeight * 2f + 2f;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
            float fieldWidth = Mathf.Max(32f, (rowRect.width - spacing) * 0.5f);

            Rect sourceRect = new Rect(rowRect.x, rowRect.y, fieldWidth, rowRect.height);
            Rect targetRect = new Rect(sourceRect.xMax + spacing, rowRect.y,
                Mathf.Max(32f, rowRect.xMax - (sourceRect.xMax + spacing)), rowRect.height);

            sourcePose = DrawInlineObjectField(sourceRect, "Source Pose", sourcePose, false);
            targetPose = DrawInlineObjectField(targetRect, "Target Pose", targetPose, false);
            EditorGUILayout.Space(2f);

            bool posesChanged = sourcePose != profile.sourcePose || targetPose != profile.targetPose;
            if (!posesChanged)
            {
                return default;
            }

            return ApplyRetargetProfileEdit(profile, () =>
            {
                profile.sourcePose = sourcePose;
                profile.targetPose = targetPose;
            });
        }

        private RetargetProfileFieldChangeSet UpdateRigAssignments(RetargetProfile profile, KRig sourceRig,
            KRig targetRig)
        {
            if (profile == null)
            {
                return default;
            }

            bool rigsChanged = sourceRig != profile.sourceRig || targetRig != profile.targetRig;
            if (!rigsChanged)
            {
                return default;
            }

            _profileStatusMessage = string.Empty;
            _profileStatusType = MessageType.Info;

            return ApplyRetargetProfileEdit(profile, () =>
            {
                profile.sourceRig = sourceRig;
                profile.targetRig = targetRig;
                profile.OnRigUpdated();
            });
        }

        private static RetargetProfileFieldChangeSet ApplyRetargetProfileEdit(RetargetProfile profile,
            Action applyChanges)
        {
            if (profile == null || applyChanges == null)
            {
                return default;
            }

            Undo.RecordObject(profile, "Edit Retarget Profile");
            applyChanges();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return new RetargetProfileFieldChangeSet(true, true);
        }

        private void ApplyProfileFieldChanges(RetargetProfileFieldChangeSet fieldChanges,
            bool preserveCameraState = false)
        {
            if (!fieldChanges.changed)
            {
                return;
            }

            if (fieldChanges.requiresPreviewRefresh)
            {
                if (!retargetAnimBaker.RefreshAfterProfileSettingsChange(out string refreshError))
                {
                    _initError = refreshError;
                    return;
                }

                _initError = string.Empty;
                ResetPreviewInstanceCache(preserveCameraState);
            }

            if (GetActiveAnimation() != null && retargetAnimBaker.IsInitialized)
            {
                RequestPreviewSample();
            }
            else
            {
                Repaint();
            }
        }

        private AnimationClip GetProfileItemAnimation()
        {
            RetargetProfile profile = retargetAnimBaker != null ? retargetAnimBaker.retargetProfile : null;
            return profile != null ? profile.clipItemAnimation : null;
        }

        private AnimationClip GetActiveAnimation()
        {
            return _animationClips != null && _selectedAnimationIndex >= 0 &&
                   _selectedAnimationIndex < _animationClips.Count
                ? _animationClips[_selectedAnimationIndex]
                : null;
        }

        private void StartPlayback(bool loop)
        {
            if (GetActiveAnimation() == null || !TryInitializePreview())
            {
                return;
            }

            _loopPreview = loop;
            _lastFrameTime = (float)EditorApplication.timeSinceStartup;
            RequestPreviewSample();
        }

        private T DrawCompactObjectField<T>(string label, T value, bool allowSceneObjects) where T : UnityEngine.Object
        {
            Rect fieldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            return (T)EditorGUI.ObjectField(fieldRect, label, value, typeof(T), allowSceneObjects);
        }

        private T DrawInlineObjectField<T>(Rect rect, string label, T value, bool allowSceneObjects)
            where T : UnityEngine.Object
        {
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = new Rect(rect.x, labelRect.yMax + 2f, rect.width, EditorGUIUtility.singleLineHeight);

            GUI.Label(labelRect, label, _styles.CompactFieldLabelStyle);
            return (T)EditorGUI.ObjectField(fieldRect, value, typeof(T), allowSceneObjects);
        }

        private void DrawInfoTabs()
        {
            Rect toolbarRect =
                GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            GUI.Box(toolbarRect, GUIContent.none, _styles.ToolbarNoPaddingStyle);
            _infoTabIndex = GUI.Toolbar(toolbarRect, _infoTabIndex, InfoTabs, _styles.ToolbarButtonNoPaddingStyle);
            GUILayoutUtility.GetRect(0f, 8f, GUILayout.ExpandWidth(true));
        }

        private void DrawInfoPanel(bool readyForPlayback, string validationMessage, MessageType validationType)
        {
            CollectHelpMessages(readyForPlayback, validationMessage, validationType);

            if (_infoTabIndex == 0)
            {
                DrawMessageListPanel(_notificationMessages);
                return;
            }

            DrawMessageListPanel(_helpMessages);
        }

        private void DrawMessageListPanel(List<WindowMessage> messages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(38f),
                        GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                {
                    messages.Clear();
                }
            }

            EditorGUILayout.Space();

            if (messages.Count == 0)
            {
                return;
            }

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                DrawRoundedMessageBox(messages[i]);
            }
        }

        private void DrawRoundedMessageBox(WindowMessage message)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _styles.SectionBackgroundColor;
            EditorGUILayout.BeginVertical(_styles.MessageBoxStyle);
            GUI.backgroundColor = previousBackgroundColor;

            try
            {
                Rect headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                Rect iconRect = new Rect(headerRect.x, headerRect.y, 18f, headerRect.height);
                Rect statusRect = new Rect(iconRect.xMax + 2f, headerRect.y, 52f, headerRect.height);
                Rect timestampRect = new Rect(headerRect.xMax - 64f, headerRect.y, 64f, headerRect.height);

                GUI.Label(iconRect, _styles.GetMessageStatusIcon(message.type));
                GUI.Label(statusRect, GetMessageStatusLabel(message.type), _styles.MessageHeaderStyle);

                if (message.feature != null && !string.IsNullOrEmpty(message.featureDisplayName))
                {
                    float linkX = statusRect.xMax + 4f;
                    float linkWidth = Mathf.Max(1f, timestampRect.x - linkX - 6f);
                    Rect linkRect = new Rect(linkX, headerRect.y, linkWidth, headerRect.height);
                    DrawFeatureMessageLink(linkRect, message.feature, message.featureDisplayName);
                }

                GUI.Label(timestampRect, message.timestamp, _styles.MessageTimestampStyle);

                if (!string.IsNullOrEmpty(NormalizeMessage(message.text)))
                {
                    EditorGUILayout.Space(3f);
                }

                DrawMessageBody(message);
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawMessageBody(WindowMessage message)
        {
            string bodyText = NormalizeMessage(message.text);
            if (string.IsNullOrEmpty(bodyText))
            {
                return;
            }

            GUILayout.Label(bodyText, _styles.MessageBodyStyle);
        }

        private GUIContent CreateFeatureLinkContent(string featureDisplayName)
        {
            string linkText = EscapeRichText(featureDisplayName);
            return new GUIContent($"<color=#55ABFA><u>{linkText}</u></color>");
        }

        private void DrawFeatureMessageLink(Rect linkRect, RetargetFeature feature, string featureDisplayName)
        {
            GUIContent linkContent = CreateFeatureLinkContent(featureDisplayName);
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Arrow);
            GUI.Label(linkRect, linkContent, _styles.MessageLinkStyle);

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 &&
                linkRect.Contains(currentEvent.mousePosition))
            {
                OpenFeatureFromMessage(feature);
                currentEvent.Use();
            }
        }

        private void CollectHelpMessages(bool readyForPlayback, string validationMessage, MessageType validationType)
        {
            CaptureHelpMessage(ref _lastInitHelpMessage, _initError, MessageType.Error);
            CaptureHelpMessage(ref _lastValidationHelpMessage,
                readyForPlayback ? string.Empty : validationMessage, validationType);
            CaptureHelpMessage(ref _lastProfileStatusHelpMessage,
                _profileStatusType == MessageType.Info ? string.Empty : _profileStatusMessage, _profileStatusType);
            CaptureHelpMessage(ref _lastPreviewCharactersHelpMessage, _previewCharactersError,
                _previewCharactersReady ? MessageType.Warning : MessageType.Error);

            string exceptionMessage = string.Empty;
            if (_lastException != null)
            {
                string contextPrefix =
                    string.IsNullOrEmpty(_lastErrorContext) ? string.Empty : $"{_lastErrorContext}: ";
                exceptionMessage = $"{contextPrefix}{_lastException.Message}";
            }

            CaptureHelpMessage(ref _lastExceptionHelpMessage, exceptionMessage, MessageType.Error);
        }

        private void CaptureHelpMessage(ref string cache, string message, MessageType type)
        {
            string normalizedMessage = NormalizeMessage(message);
            if (string.IsNullOrEmpty(normalizedMessage))
            {
                cache = string.Empty;
                return;
            }

            if (string.Equals(cache, normalizedMessage, StringComparison.Ordinal))
            {
                return;
            }

            cache = normalizedMessage;
            AddHelpMessage(normalizedMessage, type);
        }

        private void AddNotificationMessage(string message, MessageType type = MessageType.Info,
            bool preventDuplicates = false, RetargetFeature feature = null, string featureDisplayName = null)
        {
            AddMessage(_notificationMessages, message, type, preventDuplicates, feature, featureDisplayName);
        }

        private void AddHelpMessage(string message, MessageType type, RetargetFeature feature = null,
            string featureDisplayName = null)
        {
            AddMessage(_helpMessages, message, type, true, feature, featureDisplayName);
        }

        private void AddMessage(List<WindowMessage> target, string message, MessageType type, bool preventDuplicates,
            RetargetFeature feature = null, string featureDisplayName = null)
        {
            string normalizedMessage = NormalizeMessage(message);
            if (string.IsNullOrEmpty(normalizedMessage))
            {
                return;
            }

            string normalizedFeatureDisplayName = NormalizeMessage(featureDisplayName);

            if (preventDuplicates)
            {
                for (int i = 0; i < target.Count; i++)
                {
                    if (target[i].type == type &&
                        target[i].feature == feature &&
                        string.Equals(target[i].featureDisplayName, normalizedFeatureDisplayName,
                            StringComparison.Ordinal) &&
                        string.Equals(target[i].text, normalizedMessage, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            target.Add(new WindowMessage(type, normalizedMessage, feature, normalizedFeatureDisplayName));
            if (target.Count > MaxTabMessages)
            {
                target.RemoveAt(0);
            }

            Repaint();
        }

        private void ResetInfoMessages()
        {
            _notificationMessages.Clear();
            _helpMessages.Clear();
            _lastValidationHelpMessage = string.Empty;
            _lastInitHelpMessage = string.Empty;
            _lastProfileStatusHelpMessage = string.Empty;
            _lastPreviewCharactersHelpMessage = string.Empty;
            _lastExceptionHelpMessage = string.Empty;
        }

        private static string NormalizeMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        }

        private static string EscapeRichText(string text)
        {
            string normalizedText = NormalizeMessage(text);
            if (string.IsNullOrEmpty(normalizedText))
            {
                return string.Empty;
            }

            return normalizedText.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string GetMessageStatusLabel(MessageType type)
        {
            return type switch
            {
                MessageType.Warning => "Warning",
                MessageType.Error => "Error",
                _ => "Info"
            };
        }

        private void CaptureFeatureInitializationMessages()
        {
            IReadOnlyList<RetargetFeatureInitializationMessage> messages =
                retargetAnimBaker?.RetargetComponent?.InitializationMessages;
            if (messages == null)
            {
                return;
            }

            int count = messages.Count;
            for (int i = 0; i < count; i++)
            {
                RetargetFeatureInitializationMessage message = messages[i];
                if (message.channel == RetargetFeatureInitializationMessageChannel.Help)
                {
                    AddHelpMessage(message.text, message.type, message.feature, message.featureDisplayName);
                    continue;
                }

                AddNotificationMessage(message.text, message.type, true, message.feature, message.featureDisplayName);
            }
        }

        private void OpenFeatureFromMessage(RetargetFeature feature)
        {
            if (feature == null)
            {
                return;
            }

            if (_retargetWidget == null && retargetAnimBaker?.retargetProfile != null)
            {
                RefreshProfileWidget(retargetAnimBaker.retargetProfile);
            }

            if (_retargetWidget == null)
            {
                return;
            }

            _boneChainSection?.SetExpanded(true);
            if (_retargetWidget.TrySelectFeature(feature))
            {
                Focus();
                Repaint();
            }
        }

        private void RefreshProfileWidget(RetargetProfile profile)
        {
            if (profile == null)
            {
                _retargetWidget = null;
                return;
            }

            _retargetWidget = new RetargetProfileWidget(profile);
            _retargetWidget.Init(new SerializedObject(profile));
        }

        private void OnEnable()
        {
            retargetAnimBaker ??= new RetargetAnimBaker();

            _animationClips ??= new List<AnimationClip>();
            _animationClipDisplayNames ??= Array.Empty<string>();
            if (_useManualInitialization)
            {
                TryRestoreWindowState(null);
                _useManualInitialization = false;
            }

            _previewEnvironment ??= new RetargetPreviewEnvironment();
            _previewBoundsCache ??= new RetargetPreviewBoundsCache();
            _previewMaterialCache ??= new RetargetPreviewMaterialCache();
            _previewTransformIndicator ??=
                new RetargetPreviewTransformIndicator(PreviewTransformIndicatorSizeOptions,
                    PreviewTransformIndicatorColorOptions);
            _styles = new RetargetProWindowStyles();
            _profileSetupSection ??= new FoldoutSection(nameof(RetargetProWindow), "ProfileSetup",
                "Profile Setup");
            _itemSection ??= new FoldoutSection(nameof(RetargetProWindow), "Item", "Item");
            _clipSettingsSection ??= new FoldoutSection(nameof(RetargetProWindow), "ClipSettings",
                "Clip & Save Settings");
            _playbackPreviewSection ??= new FoldoutSection(nameof(RetargetProWindow), "PlaybackPreview",
                "Playback & Preview");
            _boneChainSection ??= new FoldoutSection(nameof(RetargetProWindow), "BoneChainSettings",
                "Bone Chain Settings");

            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(900f, 560f);

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            FlushCameraViewPose();
            RetargetWindowAppState.Save(CaptureWindowState());

            EditorApplication.update -= OnEditorUpdate;
            StopRetargetPreview();
            CleanupScenePreviewResources();
            retargetAnimBaker?.CleanupPreviewResources();

            _useManualInitialization = true;
        }

        private void OnDestroy()
        {
            //todo: cleanup the resources here.
        }

        private void OnProjectChange()
        {
            _animationClipDisplayNamesDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            bool useWideMode = EditorGUIUtility.wideMode;
            if(!useWideMode) EditorGUIUtility.wideMode = true;
            
            FlushWindowIfCharacterModelsDeleted();
            _previewCharactersReady = true;
            _previewCharactersError = string.Empty;

            Rect viewRect = new Rect(0f, 0f, position.width, position.height);
            HandlePanelResize(viewRect);
            Rect panelRect = GetPanelRect(viewRect);
            Rect sceneRect = GetSceneRect(viewRect, panelRect);

            if (_fullScreenMode)
            {
                DrawSharedScenePreview(viewRect);
            }
            else
            {
                DrawSharedScenePreview(sceneRect);
                DrawControlPanel(panelRect);
                DrawPanelSplitter(viewRect, panelRect);
            }

            EditorGUIUtility.wideMode = useWideMode;
        }

        private Rect GetPanelRect(Rect viewRect)
        {
            float width = GetPanelWidth(viewRect);
            float height = Mathf.Max(120f, viewRect.height);
            return new Rect(viewRect.x, viewRect.y, width, height);
        }

        private static Rect GetSceneRect(Rect viewRect, Rect panelRect)
        {
            float x = panelRect.xMax + SplitterWidth;
            float width = Mathf.Max(1f, viewRect.xMax - x);
            return new Rect(x, viewRect.y, width, viewRect.height);
        }

        private void HandlePanelResize(Rect viewRect)
        {
            Rect panelRect = GetPanelRect(viewRect);
            Rect splitterRect = GetSplitterRect(viewRect, panelRect);
            Event evt = Event.current;

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            if (evt.type == EventType.MouseDown && evt.button == 0 && splitterRect.Contains(evt.mousePosition))
            {
                _splitViewState.isResizing = true;
                _previewInputState.isOrbiting = false;
                _previewInputState.isPanning = false;
                GetPanelRatioBounds(viewRect, out _splitViewState.dragMinRatio, out _splitViewState.dragMaxRatio);
                evt.Use();
                return;
            }

            if (_splitViewState.isResizing && evt.type == EventType.MouseDrag)
            {
                float availableWidth = GetAvailableSplitWidth(viewRect);
                if (availableWidth > 0f)
                {
                    float desiredWidth = Mathf.Clamp(evt.mousePosition.x - viewRect.x, 0f, availableWidth);
                    float rawRatio = desiredWidth / availableWidth;
                    _splitViewState.ratio = Mathf.Clamp(rawRatio, _splitViewState.dragMinRatio,
                        _splitViewState.dragMaxRatio);
                    GUI.changed = true;
                    Repaint();
                }

                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp)
            {
                _splitViewState.isResizing = false;
            }
        }

        private void DrawPanelSplitter(Rect viewRect, Rect panelRect)
        {
            Rect splitterRect = new Rect(panelRect.xMax, viewRect.y, SplitterWidth, viewRect.height);
            EditorGUI.DrawRect(splitterRect, SplitterColor);
        }

        private float GetPanelWidth(Rect viewRect)
        {
            float availableWidth = GetAvailableSplitWidth(viewRect);
            GetPanelRatioBounds(viewRect, out float minRatio, out float maxRatio);

            _splitViewState.ratio = Mathf.Clamp(_splitViewState.ratio, minRatio, maxRatio);
            return availableWidth * _splitViewState.ratio;
        }

        private void GetPanelRatioBounds(Rect viewRect, out float minRatio, out float maxRatio)
        {
            float availableWidth = GetAvailableSplitWidth(viewRect);

            minRatio = Mathf.Max(MinPanelWidthRatio, PanelMinWidth / availableWidth);
            maxRatio = Mathf.Min(MaxPanelWidthRatio, 1f - SceneMinWidth / availableWidth);

            minRatio = Mathf.Clamp01(minRatio);
            maxRatio = Mathf.Clamp(maxRatio, minRatio, 1f);
        }

        private static float GetAvailableSplitWidth(Rect viewRect)
        {
            return Mathf.Max(1f, viewRect.width - SplitterWidth);
        }

        private static Rect GetSplitterRect(Rect viewRect, Rect panelRect)
        {
            float x = panelRect.xMax - (SplitterHitWidth - SplitterWidth) * 0.5f;
            return new Rect(x, viewRect.y, SplitterHitWidth, viewRect.height);
        }

        private void DrawControlPanel(Rect panelRect)
        {
            EditorGUI.DrawRect(panelRect, PanelBackground);

            Rect contentRect = new Rect(panelRect.x + 8f, panelRect.y + 8f, panelRect.width - 16f,
                panelRect.height - 16f);
            Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 22f);
            GUI.Label(headerRect, "ANIMATION RETARGET PRO", _styles.WindowHeaderStyle);

            Rect scrollRect = new Rect(contentRect.x, headerRect.yMax + 4f, contentRect.width,
                Mathf.Max(1f, contentRect.height - headerRect.height - 4f));
            GUILayout.BeginArea(scrollRect);

            if (_lastException != null)
            {
                DrawErrorUI();
                GUILayout.EndArea();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            bool profileWidgetChanged = false;

            if (!TryDraw(() => { profileWidgetChanged = DrawWindowGUI(); },
                    "Control Panel"))
            {
                DrawErrorUI();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            if (profileWidgetChanged && retargetAnimBaker != null && retargetAnimBaker.IsInitialized)
            {
                RequestPreviewSample();
            }
        }

        private void DrawSharedScenePreview(Rect viewRect)
        {
            EditorGUI.DrawRect(viewRect, _previewEnvironment.GetBackgroundColor());

            if (!TryPrepareSharedScenePreview(viewRect))
            {
                return;
            }

            bool repaintEvent = Event.current.type == EventType.Repaint;
            if (!repaintEvent)
            {
                DrawRetargetSceneHandles(viewRect);
            }

            if (repaintEvent)
            {
                RenderPreviewTexture(viewRect);
                DrawRetargetSceneHandles(viewRect);
            }

            DrawMetricsOverlay(viewRect);
            DrawViewHint(viewRect);
            HandlePreviewCameraInput(viewRect);
        }

        private bool TryPrepareSharedScenePreview(Rect viewRect)
        {
            if (retargetAnimBaker == null || retargetAnimBaker.retargetProfile == null)
            {
                DrawPreviewMessage(viewRect, "Select a Retarget Profile in the left panel.");
                return false;
            }

            _previewCharactersReady = retargetAnimBaker.EnsurePreviewCharacters(out _previewCharactersError);
            if (!_previewCharactersReady)
            {
                DrawPreviewMessage(viewRect, _previewCharactersError);
                return false;
            }

            Scene previewScene = retargetAnimBaker.PreviewScene;
            if (!previewScene.IsValid())
            {
                DrawPreviewMessage(viewRect, "Preview scene is unavailable.");
                return false;
            }

            EnsureScenePreviewObjects(previewScene);
            if (_previewCamera == null)
            {
                DrawPreviewMessage(viewRect, "Failed to create preview camera.");
                return false;
            }

            TrackPreviewInstances();
            LayoutPreviewInstances(previewScene);
            UpdateCameraFraming();
            ConfigurePreviewCamera(viewRect);
            EnsurePreviewTexture(viewRect);

            if (_previewTexture == null)
            {
                DrawPreviewMessage(viewRect, "Failed to allocate preview texture.");
                return false;
            }

            return true;
        }

        private void DrawPreviewMessage(Rect viewRect, string message)
        {
            GUI.Label(viewRect, message, _styles.OverlayLabelStyle);
        }

        private void EnsureScenePreviewObjects(Scene previewScene)
        {
            bool sceneChanged =
                _previewCamera != null && _previewCamera.gameObject.scene != previewScene ||
                _previewKeyLight != null && _previewKeyLight.gameObject.scene != previewScene ||
                _previewFillLight != null && _previewFillLight.gameObject.scene != previewScene ||
                _sourceAnchor != null && _sourceAnchor.scene != previewScene ||
                _targetAnchor != null && _targetAnchor.scene != previewScene;

            if (sceneChanged)
            {
                CleanupScenePreviewObjects();
                ResetPreviewInstanceCache();
            }

            if (_previewCamera == null)
            {
                _previewCamera = CreateHiddenObject<Camera>("RetargetPreviewCamera", previewScene);
                if (_previewCamera != null)
                {
                    _previewCamera.enabled = false;
                    _previewCamera.cameraType = CameraType.Preview;
                    _previewCamera.allowHDR = false;
                    _previewCamera.allowMSAA = false;
                }
            }

            ApplyPreviewSceneCullingMask(previewScene);

            if (_previewKeyLight == null)
            {
                _previewKeyLight = CreateHiddenObject<Light>("RetargetPreviewKeyLight", previewScene);
                if (_previewKeyLight != null)
                {
                    RetargetPreviewRenderIsolation.ConfigureLight(_previewKeyLight);
                    _previewKeyLight.type = LightType.Directional;
                    _previewKeyLight.intensity = 1.1f;
                    _previewKeyLight.cullingMask = 1 << RetargetAnimBaker.PreviewLayer;
                    _previewKeyLight.transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                }
            }

            if (_previewFillLight == null)
            {
                _previewFillLight = CreateHiddenObject<Light>("RetargetPreviewFillLight", previewScene);
                if (_previewFillLight != null)
                {
                    RetargetPreviewRenderIsolation.ConfigureLight(_previewFillLight);
                    _previewFillLight.type = LightType.Directional;
                    _previewFillLight.intensity = 0.55f;
                    _previewFillLight.cullingMask = 1 << RetargetAnimBaker.PreviewLayer;
                    _previewFillLight.transform.rotation = Quaternion.Euler(330f, 218f, 177f);
                }
            }

            if (_sourceAnchor == null)
            {
                _sourceAnchor = CreateHiddenGameObject("RetargetPreviewSourceAnchor", previewScene);
            }

            if (_targetAnchor == null)
            {
                _targetAnchor = CreateHiddenGameObject("RetargetPreviewTargetAnchor", previewScene);
            }

            _previewEnvironment.EnsureResources(previewScene, _previewCameraState.pivot);
        }

        private void TrackPreviewInstances()
        {
            GameObject source = retargetAnimBaker.SourcePreviewInstance;
            GameObject target = retargetAnimBaker.TargetPreviewInstance;

            if (source != _lastSourceInstance || target != _lastTargetInstance)
            {
                _previewMaterialCache.Cleanup();
                _lastSourceInstance = source;
                _lastTargetInstance = target;
                _previewBoundsCache.Invalidate();
                SetLayerRecursive(_lastSourceInstance, RetargetAnimBaker.PreviewLayer);
                SetLayerRecursive(_lastTargetInstance, RetargetAnimBaker.PreviewLayer);
                _previewMaterialCache.EnsureSupportedRendererMaterials(_lastSourceInstance, "RetargetPreview");
                _previewMaterialCache.EnsureSupportedRendererMaterials(_lastTargetInstance, "RetargetPreview");
                bool preserveCameraState = _preserveCameraOnNextPreviewRefresh;
                _preserveCameraOnNextPreviewRefresh = false;
                MarkPreviewDirty(preserveCameraState);
            }
        }

        private void LayoutPreviewInstances(Scene previewScene)
        {
            if (!_previewLayoutDirty || _lastSourceInstance == null || _lastTargetInstance == null)
            {
                return;
            }

            if (_sourceAnchor == null || _sourceAnchor.scene != previewScene)
            {
                _sourceAnchor = CreateHiddenGameObject("RetargetPreviewSourceAnchor", previewScene);
            }

            if (_targetAnchor == null || _targetAnchor.scene != previewScene)
            {
                _targetAnchor = CreateHiddenGameObject("RetargetPreviewTargetAnchor", previewScene);
            }

            if (_sourceAnchor == null || _targetAnchor == null)
            {
                return;
            }

            if (_lastSourceInstance.scene != previewScene)
            {
                SceneManager.MoveGameObjectToScene(_lastSourceInstance, previewScene);
            }

            if (_lastTargetInstance.scene != previewScene)
            {
                SceneManager.MoveGameObjectToScene(_lastTargetInstance, previewScene);
            }

            if (_lastSourceInstance.transform.parent != _sourceAnchor.transform)
            {
                _lastSourceInstance.transform.SetParent(_sourceAnchor.transform, true);
            }

            if (_lastTargetInstance.transform.parent != _targetAnchor.transform)
            {
                _lastTargetInstance.transform.SetParent(_targetAnchor.transform, true);
            }

            Bounds sourceBounds = _previewBoundsCache.GetSourceBounds(_lastSourceInstance);
            Transform clipItemRoot = GetClipItemRootTransform();
            Bounds targetBounds = _previewBoundsCache.GetTargetBounds(_lastTargetInstance, clipItemRoot);

            float sourceRadius = Mathf.Max(0.5f, sourceBounds.extents.magnitude);
            float targetRadius = Mathf.Max(0.5f, targetBounds.extents.magnitude);
            float spacing = Mathf.Max(1f, sourceRadius + targetRadius);
            float chunkSize = _previewEnvironment.GetConfiguredChunkSize();
            float halfSpacing = spacing * 0.5f;
            if (chunkSize > 0f)
            {
                // Snap down to the nearest major grid chunk so the pair stays aligned without drifting too far apart.
                halfSpacing = RetargetPreviewEnvironment.SnapDownToChunkMagnitude(halfSpacing, chunkSize, chunkSize);
            }

            Vector3 sourceOffset = new Vector3(-sourceBounds.center.x, 0f, -sourceBounds.center.z);
            Vector3 targetOffset = new Vector3(-targetBounds.center.x, 0f, -targetBounds.center.z);
            if (chunkSize > 0f)
            {
                sourceOffset.z = RetargetPreviewEnvironment.SnapDownToChunkMagnitude(sourceOffset.z, chunkSize);
                targetOffset.z = RetargetPreviewEnvironment.SnapDownToChunkMagnitude(targetOffset.z, chunkSize);
            }

            _sourceAnchor.transform.SetPositionAndRotation(
                sourceOffset + new Vector3(-halfSpacing, 0f, 0f), Quaternion.identity);
            _targetAnchor.transform.SetPositionAndRotation(
                targetOffset + new Vector3(halfSpacing, 0f, 0f), Quaternion.identity);

            _previewBoundsCache.Invalidate();
            _previewLayoutDirty = false;
        }

        private void UpdateCameraFraming()
        {
            if (!TryGetCombinedBounds(out Bounds bounds))
            {
                _previewCameraState.minDistance = MinPreviewDistance;
                _previewCameraState.maxDistance = DefaultPreviewMaxDistance;
                _previewCameraState.distance = Mathf.Clamp(_previewCameraState.distance, GetPreviewDistanceMin(),
                    _previewCameraState.maxDistance);

                _previewEnvironment.UpdateSizing(
                    _previewEnvironment.GetDefaultExtentForCamera(_previewCameraState.distance),
                    _previewEnvironment.GetConfiguredSubChunkStep(), _previewCameraState.pivot);
                return;
            }

            float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            _previewCameraState.minDistance = Mathf.Max(MinPreviewDistance, radius * 0.2f);
            _previewCameraState.maxDistance = Mathf.Max(24f, radius * 25f);

            if (_needsFrame)
            {
                _previewCameraState.pivot = bounds.center;
                float halfFovRadians = GetPreviewFieldOfView() * Mathf.Deg2Rad * 0.5f;
                float targetDistance = radius / Mathf.Sin(halfFovRadians);
                _previewCameraState.distance = Mathf.Clamp(targetDistance, _previewCameraState.minDistance,
                    _previewCameraState.maxDistance);
                _previewCameraState.atPivot = false;
                _needsFrame = false;
            }
            else
            {
                _previewCameraState.distance = Mathf.Clamp(_previewCameraState.distance, GetPreviewDistanceMin(),
                    _previewCameraState.maxDistance);
            }

            _previewEnvironment.UpdateSizing(
                _previewEnvironment.GetExtentForBounds(radius, _previewCameraState.distance),
                _previewEnvironment.GetConfiguredSubChunkStep(), _previewCameraState.pivot);
        }

        private void ConfigurePreviewCamera(Rect viewRect)
        {
            if (_previewCamera == null)
            {
                return;
            }

            RetargetPreviewRenderIsolation.ConfigureCamera(_previewCamera);

            float fieldOfView = GetPreviewFieldOfView();
            float nearClipPlane = GetPreviewNearClipPlane();

            _previewCamera.fieldOfView = fieldOfView;
            _previewCamera.nearClipPlane = nearClipPlane;
            _previewCamera.farClipPlane = Mathf.Max(nearClipPlane + 0.01f,
                Mathf.Max(200f, Mathf.Max(_previewCameraState.maxDistance * 2f, _previewEnvironment.GridExtent * 2f)));
            _previewCamera.clearFlags = CameraClearFlags.Color;
            _previewCamera.backgroundColor = _previewEnvironment.GetBackgroundColor();
            _previewCamera.allowHDR = false;
            _previewCamera.allowMSAA = false;
            _previewCamera.cullingMask = 1 << RetargetAnimBaker.PreviewLayer;
            _previewCamera.aspect = Mathf.Max(0.01f, viewRect.width / Mathf.Max(1f, viewRect.height));
            _previewCamera.rect = new Rect(0f, 0f, 1f, 1f);

            ApplyPreviewCameraPose();
        }

        private void ResetCameraPose()
        {
            _previewCameraState.yaw = DefaultPreviewYaw;
            _previewCameraState.pitch = DefaultPreviewPitch;
            _previewCameraState.roll = 0f;
            _previewCameraState.atPivot = false;
            _hasSavedPreviewCameraPose = false;
            _needsFrame = true;
            Repaint();
        }

        private void ApplyPreviewCameraPose()
        {
            if (_previewCamera == null)
            {
                return;
            }

            _previewCamera.transform.SetPositionAndRotation(GetPreviewCameraPosition(), GetPreviewCameraRotation());
        }

        private Quaternion GetPreviewCameraRotation()
        {
            return Quaternion.Euler(_previewCameraState.pitch, _previewCameraState.yaw, _previewCameraState.roll);
        }

        private Vector3 GetPreviewCameraPosition()
        {
            Quaternion rotation = GetPreviewCameraRotation();
            float cameraDistance = _previewCameraState.atPivot ? 0f : Mathf.Max(0f, _previewCameraState.distance);
            return _previewCameraState.pivot - (rotation * Vector3.forward * cameraDistance);
        }

        private float GetPreviewDistanceMin()
        {
            return _previewCameraState.atPivot ? 0f : _previewCameraState.minDistance;
        }

        private float GetPreviewFieldOfView()
        {
            _previewCameraState.fieldOfView = Mathf.Clamp(_previewCameraState.fieldOfView, MinPreviewFieldOfView,
                MaxPreviewFieldOfView);
            return _previewCameraState.fieldOfView;
        }

        private float GetPreviewNearClipPlane()
        {
            _previewCameraState.nearClipPlane = Mathf.Clamp(_previewCameraState.nearClipPlane, MinPreviewNearClipPlane,
                MaxPreviewNearClipPlane);
            return _previewCameraState.nearClipPlane;
        }

        private RetargetProfile GetActiveProfile()
        {
            return retargetAnimBaker != null ? retargetAnimBaker.retargetProfile : null;
        }

        private static bool CameraViewPoseEquals(KTransform left, KTransform right)
        {
            return left.position == right.position && left.rotation == right.rotation;
        }

        private void LoadSceneCursorPoseFromProfile(RetargetProfile profile)
        {
            if (_cameraViewPoseDirty && _cameraViewPoseDirtyProfile != null && _cameraViewPoseDirtyProfile != profile)
            {
                FlushCameraViewPose(_cameraViewPoseDirtyProfile);
            }

            _sceneCursorState.enabled = false;

            if (profile == null)
            {
                _sceneCursorState.transform = KTransform.Identity;
                _sceneCursorState.initialized = false;
                return;
            }

            _sceneCursorState.transform = profile.cameraViewPose;
            _sceneCursorState.initialized = true;
        }

        private void WriteSceneCursorPoseToProfile()
        {
            RetargetProfile profile = GetActiveProfile();
            if (profile == null)
            {
                return;
            }

            if (_cameraViewPoseDirty && _cameraViewPoseDirtyProfile != null && _cameraViewPoseDirtyProfile != profile)
            {
                FlushCameraViewPose(_cameraViewPoseDirtyProfile);
            }

            KTransform cameraViewPose = new KTransform(_sceneCursorState.transform.position,
                _sceneCursorState.transform.rotation, Vector3.one);
            _sceneCursorState.transform = cameraViewPose;

            if (CameraViewPoseEquals(profile.cameraViewPose, cameraViewPose))
            {
                return;
            }

            if (!_cameraViewPoseDirty)
            {
                _cameraViewPoseBeforeEdit = profile.cameraViewPose;
                _cameraViewPoseDirtyProfile = profile;
            }

            profile.cameraViewPose = cameraViewPose;
            _cameraViewPoseDirty = true;
        }

        private void FlushCameraViewPose(RetargetProfile profile = null)
        {
            if (!_cameraViewPoseDirty)
            {
                return;
            }

            profile = _cameraViewPoseDirtyProfile != null ? _cameraViewPoseDirtyProfile : profile ?? GetActiveProfile();
            if (profile == null)
            {
                return;
            }

            KTransform cameraViewPose = profile.cameraViewPose;
            if (CameraViewPoseEquals(_cameraViewPoseBeforeEdit, cameraViewPose))
            {
                _cameraViewPoseDirty = false;
                _cameraViewPoseDirtyProfile = null;
                _cameraViewPoseBeforeEdit = KTransform.Identity;
                return;
            }

            profile.cameraViewPose = _cameraViewPoseBeforeEdit;
            Undo.RecordObject(profile, "Edit Camera View");
            profile.cameraViewPose = cameraViewPose;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            _cameraViewPoseDirty = false;
            _cameraViewPoseDirtyProfile = null;
            _cameraViewPoseBeforeEdit = KTransform.Identity;
        }

        private void SavePreviewCameraPose()
        {
            _savedPreviewCameraState = _previewCameraState;
            _hasSavedPreviewCameraPose = true;
        }

        private void RestoreSavedPreviewCameraPose()
        {
            if (!_hasSavedPreviewCameraPose)
            {
                return;
            }

            float currentFieldOfView = _previewCameraState.fieldOfView;
            float currentNearClipPlane = _previewCameraState.nearClipPlane;
            float currentMinDistance = _previewCameraState.minDistance;
            float currentMaxDistance = _previewCameraState.maxDistance;

            _previewCameraState = _savedPreviewCameraState;
            _previewCameraState.fieldOfView = currentFieldOfView;
            _previewCameraState.nearClipPlane = currentNearClipPlane;
            _previewCameraState.minDistance = currentMinDistance;
            _previewCameraState.maxDistance = currentMaxDistance;
            _hasSavedPreviewCameraPose = false;
            ApplyPreviewCameraPose();
        }

        private void SyncSceneCursorPoseFromPreviewCamera()
        {
            _sceneCursorState.transform = new KTransform(GetPreviewCameraPosition(), GetPreviewCameraRotation(),
                Vector3.one);
            _sceneCursorState.initialized = true;
            WriteSceneCursorPoseToProfile();
            ApplyPreviewCameraPose();
        }

        private void SyncPreviewCameraToSceneCursor()
        {
            EnsureSceneCursorPoseInitialized();

            Vector3 cursorEuler = _sceneCursorState.transform.rotation.eulerAngles;
            _previewCameraState.yaw = GetNormalizedAngle(cursorEuler.y);
            _previewCameraState.pitch = GetNormalizedAngle(cursorEuler.x);
            _previewCameraState.roll = GetNormalizedAngle(cursorEuler.z);
            _previewCameraState.pivot = _sceneCursorState.transform.position;
            _previewCameraState.distance = 0f;
            _previewCameraState.atPivot = true;
            ApplyPreviewCameraPose();
        }

        private void ResetSceneCursorPosition()
        {
            EnsureSceneCursorPoseInitialized();
            _sceneCursorState.transform.position = Vector3.zero;
            WriteSceneCursorPoseToProfile();

            if (_previewCameraState.atPivot)
            {
                SyncPreviewCameraToSceneCursor();
            }

            Repaint();
        }

        private void ResetSceneCursorRotation()
        {
            EnsureSceneCursorPoseInitialized();
            _sceneCursorState.transform.rotation = Quaternion.identity;
            WriteSceneCursorPoseToProfile();

            if (_previewCameraState.atPivot)
            {
                SyncPreviewCameraToSceneCursor();
            }

            Repaint();
        }

        private void EnsurePreviewTexture(Rect viewRect)
        {
            int width = Mathf.Max(1, Mathf.RoundToInt(viewRect.width * EditorGUIUtility.pixelsPerPoint));
            int height = Mathf.Max(1, Mathf.RoundToInt(viewRect.height * EditorGUIUtility.pixelsPerPoint));

            if (_previewTexture != null && (_previewTextureWidth != width || _previewTextureHeight != height))
            {
                _previewTexture.Release();
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            if (_previewTexture != null)
            {
                return;
            }

            _previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "RetargetPreviewTexture",
                antiAliasing = 1
            };
            _previewTexture.wrapMode = TextureWrapMode.Clamp;
            _previewTexture.filterMode = FilterMode.Bilinear;
            _previewTexture.useMipMap = false;
            _previewTexture.autoGenerateMips = false;
            _previewTexture.Create();

            _previewTextureWidth = width;
            _previewTextureHeight = height;
            _previewTextureRenderCount = 0;
            _refreshHdrpDx12PreviewTexture =
                GraphicsSettings.currentRenderPipeline?.GetType().FullName ==
                "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset" &&
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 &&
                SystemInfo.renderingThreadingMode.ToString() == "NativeGraphicsJobsSplitThreading";
        }

        private void ApplyPreviewSceneCullingMask(Scene previewScene)
        {
            if (_previewCamera == null || !previewScene.IsValid())
            {
                return;
            }

            ulong sceneCullingMask = EditorSceneManager.GetSceneCullingMask(previewScene);
            if (sceneCullingMask == 0UL)
            {
                sceneCullingMask = ulong.MaxValue;
            }

            _previewCamera.overrideSceneCullingMask = sceneCullingMask;
        }

        private void RenderPreviewTexture(Rect viewRect)
        {
            if (_previewCamera == null || _previewTexture == null)
            {
                return;
            }

            // HDRP on DX12 Split accumulates render dependencies for repeated Camera.Render calls.
            // Recreating only the preview target clears that queue without disturbing either model.
            if (_refreshHdrpDx12PreviewTexture &&
                _previewTextureRenderCount >= HdrpDx12PreviewTextureRefreshInterval)
            {
                _previewTexture.Release();
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
                EnsurePreviewTexture(viewRect);
                if (_previewTexture == null)
                {
                    return;
                }
            }

            RenderTexture originalTargetTexture = _previewCamera.targetTexture;

            try
            {
                _previewCamera.rect = new Rect(0f, 0f, 1f, 1f);
                _previewCamera.targetTexture = _previewTexture;
                using (RetargetPreviewRenderIsolation.SuppressFog())
                {
                    _previewCamera.Render();
                }
            }
            finally
            {
                _previewCamera.targetTexture = originalTargetTexture;
                _previewCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }

            _previewTextureRenderCount++;

            EditorGUI.DrawPreviewTexture(viewRect, _previewTexture, null, ScaleMode.StretchToFill);
        }

        private void HandlePreviewCameraInput(Rect viewRect)
        {
            Event evt = Event.current;
            if (evt.type == EventType.Used)
            {
                return;
            }

            bool mouseInView = viewRect.Contains(evt.mousePosition);
            float inputScale = evt.shift ? ShiftPrecisionInputScale : 1f;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (!mouseInView || GUIUtility.hotControl != 0)
                    {
                        return;
                    }

                    if (evt.button == 0)
                    {
                        _previewInputState.isOrbiting = true;
                    }
                    else if (evt.button == 1 || evt.button == 2)
                    {
                        _previewInputState.isPanning = true;
                    }
                    else
                    {
                        return;
                    }

                    _previewInputState.lastMousePosition = evt.mousePosition;
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_previewInputState.isOrbiting && !_previewInputState.isPanning)
                    {
                        return;
                    }

                    if (GUIUtility.hotControl != 0)
                    {
                        _previewInputState.isOrbiting = false;
                        _previewInputState.isPanning = false;
                        return;
                    }

                    Vector2 delta = evt.mousePosition - _previewInputState.lastMousePosition;
                    _previewInputState.lastMousePosition = evt.mousePosition;
                    bool previewCameraChanged = false;

                    if (_previewInputState.isOrbiting)
                    {
                        _previewCameraState.yaw += delta.x * OrbitSpeed * inputScale;
                        _previewCameraState.pitch = Mathf.Clamp(
                            _previewCameraState.pitch + delta.y * OrbitSpeed * inputScale, MinPitch, MaxPitch);
                        previewCameraChanged = true;
                    }

                    if (_previewInputState.isPanning && _previewCamera != null)
                    {
                        Transform camTransform = _previewCamera.transform;
                        float panDistance = Mathf.Max(_previewCameraState.distance,
                            _previewCameraState.atPivot ? 1f : _previewCameraState.minDistance);
                        Vector3 panOffset = (-camTransform.right * delta.x + camTransform.up * delta.y) *
                                            (panDistance * PanSpeedFactor * inputScale);
                        _previewCameraState.pivot += panOffset;
                        previewCameraChanged = true;
                    }

                    if (previewCameraChanged && _previewCameraState.atPivot)
                    {
                        SyncSceneCursorPoseFromPreviewCamera();
                    }

                    evt.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    _previewInputState.isOrbiting = false;
                    _previewInputState.isPanning = false;
                    break;

                case EventType.ScrollWheel:
                    if (!mouseInView)
                    {
                        return;
                    }

                    float scrollDelta = Mathf.Abs(evt.delta.y) >= Mathf.Abs(evt.delta.x) ? evt.delta.y : evt.delta.x;
                    if (_previewCameraState.atPivot)
                    {
                        float translationStep =
                            Mathf.Max(0.05f, Mathf.Max(_previewCameraState.minDistance, 1f) * 0.25f);
                        Vector3 translation = -(GetPreviewCameraRotation() * Vector3.forward) *
                                              (scrollDelta * translationStep * inputScale);
                        _previewCameraState.pivot += translation;
                        SyncSceneCursorPoseFromPreviewCamera();
                    }
                    else
                    {
                        float zoomScale = Mathf.Exp(scrollDelta * ZoomSpeed * inputScale);
                        float currentDistance = _previewCameraState.distance;
                        _previewCameraState.distance = Mathf.Clamp(currentDistance * zoomScale,
                            _previewCameraState.minDistance, _previewCameraState.maxDistance);
                    }

                    evt.Use();
                    Repaint();
                    break;

                case EventType.KeyDown:
                    if (!mouseInView || evt.keyCode != KeyCode.F)
                    {
                        return;
                    }

                    _previewCameraState.atPivot = false;
                    _hasSavedPreviewCameraPose = false;
                    _needsFrame = true;
                    evt.Use();
                    Repaint();
                    break;
            }
        }

        private void DrawRetargetSceneHandles(Rect viewRect)
        {
            bool canDrawRetargetHandles = retargetAnimBaker != null && retargetAnimBaker.IsInitialized &&
                                          retargetAnimBaker.RetargetComponent != null;
            bool canDrawCameraView = GetActiveProfile() != null;
            if (_previewCamera == null || (!canDrawRetargetHandles && !canDrawCameraView))
            {
                return;
            }

            Color originalGuiColor = GUI.color;
            Color originalGuiContentColor = GUI.contentColor;
            Color originalGuiBackgroundColor = GUI.backgroundColor;
            Matrix4x4 originalGuiMatrix = GUI.matrix;
            bool originalGuiEnabled = GUI.enabled;
            int originalGuiDepth = GUI.depth;

            Color originalHandlesColor = Handles.color;
            Matrix4x4 originalHandlesMatrix = Handles.matrix;
            CompareFunction originalHandlesZTest = Handles.zTest;
            RenderTexture originalCameraTargetTexture = _previewCamera.targetTexture;
            bool retargetHandlesChanged = false;
            bool sceneCursorChanged = false;

            try
            {
                Handles.SetCamera(viewRect, _previewCamera);
                Handles.zTest = CompareFunction.Always;

                if (canDrawRetargetHandles)
                {
                    EditorGUI.BeginChangeCheck();
                    retargetAnimBaker.RetargetComponent.OnSceneGUI();
                    retargetHandlesChanged = EditorGUI.EndChangeCheck();
                }

                DrawPreviewTransformIndicators();

                if (canDrawCameraView && _activePreviewOverlayDropdown == PreviewOverlayDropdown.CameraSettings)
                {
                    DrawSceneCursorIcon();

                    if (_sceneCursorState.enabled && !_previewCameraState.atPivot)
                    {
                        EditorGUI.BeginChangeCheck();
                        DrawSceneCursorHandles();
                        sceneCursorChanged = EditorGUI.EndChangeCheck();
                    }
                }
            }
            catch (Exception ex)
            {
                CaptureException(ex, "Preview Scene GUI");
                StopRetargetPreview();
                return;
            }
            finally
            {
                Handles.color = originalHandlesColor;
                Handles.matrix = originalHandlesMatrix;
                Handles.zTest = originalHandlesZTest;
                _previewCamera.targetTexture = originalCameraTargetTexture;
                _previewCamera.rect = new Rect(0f, 0f, 1f, 1f);

                GUI.color = originalGuiColor;
                GUI.contentColor = originalGuiContentColor;
                GUI.backgroundColor = originalGuiBackgroundColor;
                GUI.matrix = originalGuiMatrix;
                GUI.enabled = originalGuiEnabled;
                GUI.depth = originalGuiDepth;
            }

            if (retargetHandlesChanged)
            {
                RequestPreviewSample();
            }

            if (sceneCursorChanged)
            {
                WriteSceneCursorPoseToProfile();

                if (_previewCameraState.atPivot)
                {
                    SyncPreviewCameraToSceneCursor();
                }

                Repaint();
            }
        }

        private void DrawMetricsOverlay(Rect viewRect)
        {
            if (_lastSourceInstance == null || _lastTargetInstance == null)
            {
                return;
            }

            float modelDistance;
            if (_sourceAnchor != null && _targetAnchor != null)
            {
                modelDistance = Vector3.Distance(_sourceAnchor.transform.position, _targetAnchor.transform.position);
            }
            else
            {
                Transform clipItemRoot = GetClipItemRootTransform();
                Bounds sourceBounds = _previewBoundsCache.GetSourceBounds(_lastSourceInstance);
                Bounds targetBounds = _previewBoundsCache.GetTargetBounds(_lastTargetInstance, clipItemRoot);
                modelDistance = Vector3.Distance(sourceBounds.center, targetBounds.center);
            }

            Vector3 sourceScale = _lastSourceInstance.transform.lossyScale;
            Vector3 targetScale = _lastTargetInstance.transform.lossyScale;

            string metricsText = $"Inter-model distance: {modelDistance:F2}m\n" +
                                 $"Source scale: ({sourceScale.x:F2}, {sourceScale.y:F2}, {sourceScale.z:F2})\n" +
                                 $"Target scale: ({targetScale.x:F2}, {targetScale.y:F2}, {targetScale.z:F2})";

            const float margin = 12f;
            float availableWidth = Mathf.Max(160f, viewRect.width - margin * 2f);
            float labelWidth = Mathf.Min(220f, availableWidth);
            float labelHeight = _styles.MetricsOverlayLabelStyle.CalcHeight(new GUIContent(metricsText), labelWidth);
            Rect labelRect = new Rect(viewRect.xMax - labelWidth - margin, viewRect.yMax - labelHeight - margin,
                labelWidth, labelHeight);
            GUI.Label(labelRect, metricsText, _styles.MetricsOverlayLabelStyle);
        }

        private void DrawViewHint(Rect viewRect)
        {
            float availableWidth = Mathf.Max(120f, viewRect.width - 24f);
            float maxOverlayWidth = Mathf.Min(CameraSettingsOverlayMaxWidth, availableWidth);
            float overlayRowHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, PreviewGizmoActionButtonWidth);
            float infoWidth = GetPreviewOverlayButtonWidth(PreviewInfoDropdownContent);
            float gizmoActionWidth = overlayRowHeight * 2f + PreviewOverlayButtonSpacing;
            float cameraWidth = GetPreviewOverlayButtonWidth(PreviewCameraSettingsDropdownContent);
            float preferredRowWidth = infoWidth + cameraWidth + gizmoActionWidth + PreviewOverlayButtonSpacing * 3f;
            float rowWidth = Mathf.Min(maxOverlayWidth, preferredRowWidth);
            cameraWidth = Mathf.Max(70f,
                Mathf.Min(cameraWidth, rowWidth - infoWidth - gizmoActionWidth - PreviewOverlayButtonSpacing * 3f));

            Rect overlayRowRect = new Rect(viewRect.x + 12f, viewRect.y + 12f, rowWidth, overlayRowHeight);
            Rect infoRect = new Rect(overlayRowRect.x, overlayRowRect.y, infoWidth, overlayRowRect.height);
            Rect cameraRect = new Rect(infoRect.xMax + PreviewOverlayButtonSpacing, overlayRowRect.y, cameraWidth,
                overlayRowRect.height);
            Rect gizmoRect = new Rect(cameraRect.xMax + PreviewOverlayButtonSpacing, overlayRowRect.y,
                gizmoActionWidth, overlayRowRect.height);

            if (GUI.Button(infoRect, PreviewInfoDropdownContent))
            {
                TogglePreviewOverlayDropdown(PreviewOverlayDropdown.Info);
            }

            if (GUI.Button(cameraRect, PreviewCameraSettingsDropdownContent))
            {
                TogglePreviewOverlayDropdown(PreviewOverlayDropdown.CameraSettings);
            }

            DrawPreviewGizmoHeaderAction(gizmoRect);

            if (_activePreviewOverlayDropdown == PreviewOverlayDropdown.None)
            {
                return;
            }

            float dropdownWidth = GetPreviewOverlayDropdownWidth(maxOverlayWidth, rowWidth);
            bool stackCameraFields = dropdownWidth - PreviewOverlayPanelPadding * 2f < CameraSettingsFieldStackWidth;
            float dropdownHeight = _activePreviewOverlayDropdown == PreviewOverlayDropdown.Info
                ? GetPreviewInfoDropdownHeight(dropdownWidth)
                : GetPreviewCameraSettingsHeight(stackCameraFields);
            Rect dropdownRect = new Rect(overlayRowRect.x, overlayRowRect.yMax + CameraSettingsOverlaySpacing,
                dropdownWidth, dropdownHeight);
            DrawPreviewDropdownPanel(dropdownRect, stackCameraFields);
        }

        private static float GetPreviewOverlayButtonWidth(GUIContent content)
        {
            return Mathf.Ceil(GUI.skin.button.CalcSize(content).x + PreviewOverlayTextButtonPadding);
        }

        private float GetPreviewOverlayDropdownWidth(float maxOverlayWidth, float rowWidth)
        {
            if (_activePreviewOverlayDropdown == PreviewOverlayDropdown.CameraSettings)
            {
                return Mathf.Min(maxOverlayWidth,
                    Mathf.Max(rowWidth, CameraSettingsFieldStackWidth + PreviewOverlayPanelPadding * 2f));
            }

            float helpWidth = _styles.MetricsLabelStyle.CalcSize(PreviewHelpLabelContent).x +
                              PreviewOverlayPanelPadding * 2f;
            return Mathf.Min(maxOverlayWidth, Mathf.Max(rowWidth, helpWidth));
        }

        private void TogglePreviewOverlayDropdown(PreviewOverlayDropdown dropdown)
        {
            _activePreviewOverlayDropdown = _activePreviewOverlayDropdown == dropdown
                ? PreviewOverlayDropdown.None
                : dropdown;
        }

        private float GetPreviewCameraSettingsHeight(bool stackFields)
        {
            return GetPreviewCameraSettingsContentHeight(stackFields) + PreviewOverlayPanelPadding * 2f;
        }

        private float GetPreviewInfoDropdownHeight(float dropdownWidth)
        {
            float labelWidth = Mathf.Max(1f, dropdownWidth - PreviewOverlayPanelPadding * 2f);
            float labelHeight = _styles.MetricsLabelStyle.CalcHeight(PreviewHelpLabelContent, labelWidth);
            return Mathf.Max(EditorGUIUtility.singleLineHeight, labelHeight) + PreviewOverlayPanelPadding * 2f;
        }

        private void DrawPreviewDropdownPanel(Rect panelRect, bool stackCameraFields)
        {
            GUI.BeginGroup(panelRect);
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = Color.white;

            Rect localPanelRect = new Rect(0f, 0f, panelRect.width, panelRect.height);
            EditorGUI.DrawRect(localPanelRect, _styles.SectionBackgroundColor);
            Color borderColor = new Color(0f, 0f, 0f, 0.65f);
            EditorGUI.DrawRect(new Rect(0f, 0f, panelRect.width, 0.5f), borderColor);
            EditorGUI.DrawRect(new Rect(0f, panelRect.height - 1f, panelRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(0f, 0f, 1f, panelRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(panelRect.width - 1f, 0f, 1f, panelRect.height), borderColor);

            try
            {
                Rect contentRect = new Rect(PreviewOverlayPanelPadding, PreviewOverlayPanelPadding,
                    Mathf.Max(1f, panelRect.width - PreviewOverlayPanelPadding * 2f),
                    Mathf.Max(1f, panelRect.height - PreviewOverlayPanelPadding * 2f));

                if (_activePreviewOverlayDropdown == PreviewOverlayDropdown.Info)
                {
                    GUI.Label(contentRect, PreviewHelpLabelContent, _styles.MetricsLabelStyle);
                    return;
                }

                DrawPreviewCameraSettingsSection(contentRect, stackCameraFields);
            }
            finally
            {
                GUI.contentColor = previousContentColor;
                GUI.EndGroup();
            }
        }

        private void DrawPreviewCameraSettingsSection(Rect contentRect, bool stackFields)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            const float buttonSpacing = 4f;
            float fieldsHeight = stackFields ? lineHeight * 2f + spacing : lineHeight;
            Rect fieldsRect = new Rect(contentRect.x, contentRect.y, contentRect.width, fieldsHeight);

            EditorGUI.BeginChangeCheck();
            float originalLabelWidth = EditorGUIUtility.labelWidth;

            try
            {
                if (stackFields)
                {
                    EditorGUIUtility.labelWidth = 40f;
                    Rect fovRect = new Rect(fieldsRect.x, fieldsRect.y, fieldsRect.width, lineHeight);
                    Rect clipRect = new Rect(fieldsRect.x, fovRect.yMax + spacing, fieldsRect.width, lineHeight);

                    _previewCameraState.fieldOfView = EditorGUI.FloatField(fovRect, "FOV",
                        _previewCameraState.fieldOfView);
                    _previewCameraState.nearClipPlane = EditorGUI.FloatField(clipRect, "Clip",
                        _previewCameraState.nearClipPlane);
                }
                else
                {
                    EditorGUIUtility.labelWidth = 36f;
                    float fieldWidth = Mathf.Max(40f, (fieldsRect.width - spacing) * 0.5f);
                    Rect fovRect = new Rect(fieldsRect.x, fieldsRect.y, fieldWidth, lineHeight);
                    Rect clipRect = new Rect(fovRect.xMax + spacing, fieldsRect.y,
                        Mathf.Max(40f, fieldsRect.xMax - (fovRect.xMax + spacing)), lineHeight);

                    _previewCameraState.fieldOfView = EditorGUI.FloatField(fovRect, "FOV",
                        _previewCameraState.fieldOfView);
                    _previewCameraState.nearClipPlane = EditorGUI.FloatField(clipRect, "Clip",
                        _previewCameraState.nearClipPlane);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = originalLabelWidth;
            }

            if (EditorGUI.EndChangeCheck())
            {
                GetPreviewFieldOfView();
                GetPreviewNearClipPlane();
                Repaint();
            }

            float buttonWidth = Mathf.Max(32f, (contentRect.width - buttonSpacing * 2f) / 3f);
            Rect frameRect = new Rect(contentRect.x, fieldsRect.yMax + spacing, buttonWidth, lineHeight);
            Rect viewRect = new Rect(frameRect.xMax + buttonSpacing, frameRect.y, buttonWidth, lineHeight);
            Rect moveRect = new Rect(viewRect.xMax + buttonSpacing, frameRect.y, buttonWidth, lineHeight);

            if (GUI.Button(frameRect, new GUIContent("Frame", "Frame the source and target models."),
                    EditorStyles.miniButtonLeft))
            {
                _needsFrame = true;
                Repaint();
            }

            bool cameraViewEnabled = GUI.Toggle(viewRect, _previewCameraState.atPivot,
                new GUIContent("View", "Toggle between the standard and camera views."),
                EditorStyles.miniButtonMid);
            if (cameraViewEnabled != _previewCameraState.atPivot)
            {
                SnapCameraToSceneCursor();
            }

            bool moveEnabled = GUI.Toggle(moveRect, _sceneCursorState.enabled,
                new GUIContent("Move", "Show the camera view transform and move handle."),
                EditorStyles.miniButtonRight);
            if (moveEnabled != _sceneCursorState.enabled)
            {
                SetSceneCursorEnabled(moveEnabled);
            }

            if (!_sceneCursorState.enabled)
            {
                return;
            }

            EnsureSceneCursorPoseInitialized();

            const float clearButtonWidth = 44f;
            float vectorFieldHeight = EditorGUIUtility.wideMode ? lineHeight : lineHeight * 2f + spacing;
            Rect positionRowRect = new Rect(contentRect.x, frameRect.yMax + spacing, contentRect.width,
                vectorFieldHeight);
            Rect rotationRowRect = new Rect(contentRect.x, positionRowRect.yMax + spacing, contentRect.width,
                vectorFieldHeight);
            Rect positionClearRect = new Rect(positionRowRect.xMax - clearButtonWidth, positionRowRect.y,
                clearButtonWidth, lineHeight);
            Rect rotationClearRect = new Rect(rotationRowRect.xMax - clearButtonWidth, rotationRowRect.y,
                clearButtonWidth, lineHeight);
            Rect positionFieldRect = new Rect(positionRowRect.x, positionRowRect.y,
                positionRowRect.width - clearButtonWidth - buttonSpacing, vectorFieldHeight);
            Rect rotationFieldRect = new Rect(rotationRowRect.x, rotationRowRect.y,
                rotationRowRect.width - clearButtonWidth - buttonSpacing, vectorFieldHeight);

            EditorGUI.BeginChangeCheck();
            originalLabelWidth = EditorGUIUtility.labelWidth;
            Vector3 position;
            Vector3 rotation;

            try
            {
                EditorGUIUtility.labelWidth = 58f;
                position = EditorGUI.Vector3Field(positionFieldRect, "Position",
                    _sceneCursorState.transform.position);
                rotation = EditorGUI.Vector3Field(rotationFieldRect, "Rotation",
                    _sceneCursorState.transform.rotation.eulerAngles);
            }
            finally
            {
                EditorGUIUtility.labelWidth = originalLabelWidth;
            }

            if (EditorGUI.EndChangeCheck())
            {
                _sceneCursorState.transform.position = position;
                _sceneCursorState.transform.rotation = Quaternion.Euler(rotation);
                WriteSceneCursorPoseToProfile();

                if (_previewCameraState.atPivot)
                {
                    SyncPreviewCameraToSceneCursor();
                }

                Repaint();
            }

            if (GUI.Button(positionClearRect, "Clear", EditorStyles.miniButton))
            {
                ResetSceneCursorPosition();
            }

            if (GUI.Button(rotationClearRect, "Clear", EditorStyles.miniButton))
            {
                ResetSceneCursorRotation();
            }
        }

        private void DrawPreviewGizmoHeaderAction(Rect actionRect)
        {
            RetargetProComponent retargetComponent = retargetAnimBaker != null
                ? retargetAnimBaker.RetargetComponent
                : null;
            bool canToggleGizmos = retargetComponent != null;

            const float spacing = 4f;
            float buttonWidth = actionRect.height;
            float buttonHeight = actionRect.height;
            float rowWidth = buttonWidth * 2f + spacing;
            float rowX = actionRect.x + Mathf.Max(0f, (actionRect.width - rowWidth) * 0.5f);
            float rowY = actionRect.y;

            Rect fullScreenRect = new Rect(rowX, rowY, buttonWidth, buttonHeight);
            Rect boneRect = new Rect(fullScreenRect.xMax + spacing, rowY, buttonWidth, buttonHeight);
            Rect transformHandleRect = new Rect(boneRect.xMax + spacing, rowY, buttonWidth, buttonHeight);

            _fullScreenMode = GUI.Toggle(fullScreenRect, _fullScreenMode, _styles.FullScreenContent, GUI.skin.button);

            using (new EditorGUI.DisabledScope(!canToggleGizmos))
            {
                _drawBoneGizmos = GUI.Toggle(boneRect, _drawBoneGizmos, _styles.BoneGizmoContent,
                    GUI.skin.button);
                if (retargetComponent != null && _drawBoneGizmos != retargetComponent.drawBoneGizmos)
                {
                    retargetComponent.drawBoneGizmos = _drawBoneGizmos;
                }

                _drawTransformHandleGizmos = GUI.Toggle(transformHandleRect, _drawTransformHandleGizmos,
                    _styles.TransformHandleGizmoContent, GUI.skin.button);

                if (retargetComponent != null
                    && _drawTransformHandleGizmos != retargetComponent.drawTransformHandleGizmos)
                {
                    retargetComponent.drawTransformHandleGizmos = _drawTransformHandleGizmos;
                }
            }
        }

        private float GetPreviewCameraSettingsContentHeight(bool stackFields)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float fieldsHeight = stackFields ? lineHeight * 2f + spacing : lineHeight;
            float vectorFieldHeight = EditorGUIUtility.wideMode ? lineHeight : lineHeight * 2f + spacing;
            float controlsHeight = _sceneCursorState.enabled
                ? lineHeight + spacing + vectorFieldHeight + spacing + vectorFieldHeight
                : lineHeight;
            return fieldsHeight + spacing + controlsHeight;
        }

        private void DrawSceneCursorIcon()
        {
            if (_previewCamera == null || _previewCameraState.atPivot)
            {
                return;
            }

            EnsureSceneCursorPoseInitialized();

            Vector3 viewportPoint = _previewCamera.WorldToViewportPoint(_sceneCursorState.transform.position);
            if (viewportPoint.z <= 0f)
            {
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float handleSize = Mathf.Max(0.001f,
                HandleUtility.GetHandleSize(_sceneCursorState.transform.position) * 0.2f);
            DrawSceneCursorIconCap(0, _sceneCursorState.transform.position, Quaternion.identity, handleSize,
                EventType.Repaint);
        }

        private void DrawSceneCursorIconCap(int controlId, Vector3 position, Quaternion rotation, float size,
            EventType eventType)
        {
            switch (eventType)
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, HandleUtility.DistanceToCircle(position, size * 0.6f));
                    break;

                case EventType.Repaint:
                    Vector2 guiPoint = HandleUtility.WorldToGUIPoint(position);
                    if (float.IsNaN(guiPoint.x) || float.IsNaN(guiPoint.y))
                    {
                        return;
                    }

                    Rect iconRect = new Rect(guiPoint.x - CameraViewSceneIconSize * 0.5f,
                        guiPoint.y - CameraViewSceneIconSize * 0.5f, CameraViewSceneIconSize, CameraViewSceneIconSize);

                    Handles.BeginGUI();
                    try
                    {
                        if (_sceneCursorState.enabled)
                        {
                            EditorGUI.DrawRect(iconRect, new Color(1f, 0.85f, 0.35f, 0.35f));
                        }

                        GUIContent sceneCameraViewIconContent = _styles.SceneCameraViewIconContent;
                        if (sceneCameraViewIconContent != null && sceneCameraViewIconContent.image != null)
                        {
                            GUI.DrawTexture(iconRect, sceneCameraViewIconContent.image, ScaleMode.ScaleToFit, true);
                        }
                        else
                        {
                            GUI.Label(iconRect, sceneCameraViewIconContent);
                        }
                    }
                    finally
                    {
                        Handles.EndGUI();
                    }

                    break;
            }
        }

        private static Tool GetSceneCursorHandleTool()
        {
            return Tools.current == Tool.Rotate ? Tool.Rotate : Tool.Move;
        }

        private void DrawPreviewTransformIndicators()
        {
            if (retargetAnimBaker == null || _previewCamera == null)
            {
                return;
            }

            _previewTransformIndicator.Draw(retargetAnimBaker.SourcePreviewInstance?.transform, _previewCamera);
            _previewTransformIndicator.Draw(retargetAnimBaker.TargetPreviewInstance?.transform, _previewCamera);
        }

        private void DrawSceneCursorHandles()
        {
            EnsureSceneCursorPoseInitialized();
            CompareFunction originalZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            try
            {
                if (GetSceneCursorHandleTool() == Tool.Rotate)
                {
                    _sceneCursorState.transform.rotation = KTransformHandles.RotationHandle(
                        _sceneCursorState.transform.position, _sceneCursorState.transform.rotation,
                        KTransformHandles.RotationHandleSettings.Default);
                }
                else
                {
                    Quaternion moveHandleRotation = Tools.pivotRotation == PivotRotation.Global
                        ? Quaternion.identity
                        : _sceneCursorState.transform.rotation;

                    _sceneCursorState.transform.position =
                        KTransformHandles.PositionHandle(_sceneCursorState.transform.position, moveHandleRotation,
                            KTransformHandles.PositionHandleSettings.Default);
                }
            }
            finally
            {
                Handles.zTest = originalZTest;
            }
        }

        private void SetSceneCursorEnabled(bool enabled)
        {
            _sceneCursorState.enabled = enabled;
            if (_sceneCursorState.enabled)
            {
                EnsureSceneCursorPoseInitialized();
                if (Tools.current != Tool.Rotate)
                {
                    Tools.current = Tool.Move;
                }
            }
            else
            {
                GUIUtility.hotControl = 0;
            }

            Repaint();
        }

        private void EnsureSceneCursorPoseInitialized()
        {
            if (_sceneCursorState.initialized)
            {
                return;
            }

            LoadSceneCursorPoseFromProfile(GetActiveProfile());
            if (_sceneCursorState.initialized)
            {
                return;
            }

            _sceneCursorState.transform = KTransform.Identity;
            _sceneCursorState.initialized = true;
        }

        private void SnapCameraToSceneCursor()
        {
            if (_previewCameraState.atPivot && _hasSavedPreviewCameraPose)
            {
                RestoreSavedPreviewCameraPose();
                Repaint();
                return;
            }

            SavePreviewCameraPose();
            SyncPreviewCameraToSceneCursor();
            GUIUtility.hotControl = 0;
            Repaint();
        }

        private static float GetNormalizedAngle(float angle)
        {
            return Mathf.DeltaAngle(0f, angle);
        }

        private void OnEditorUpdate()
        {
            if (FlushWindowIfCharacterModelsDeleted())
            {
                return;
            }

            AnimationClip activeAnimation = GetActiveAnimation();
            if (retargetAnimBaker == null || !retargetAnimBaker.IsInitialized || activeAnimation == null ||
                _lastFrameTime <= 0f)
            {
                return;
            }

            float length = Mathf.Max(0f, _endTime - _startTime);
            float timeNow = (float)EditorApplication.timeSinceStartup;
            _timeSlider += Mathf.Max(0f, timeNow - _lastFrameTime);
            _lastFrameTime = timeNow;

            if (length <= 0f)
            {
                _timeSlider = 0f;
                StopPlayback();
                retargetAnimBaker.RetargetAtTime(activeAnimation, GetProfileItemAnimation(), _startTime);
                _previewBoundsCache.Invalidate();
                Repaint();
                return;
            }

            if (_loopPreview)
            {
                _timeSlider = Mathf.Repeat(_timeSlider, length);
            }
            else if (_timeSlider >= length)
            {
                _timeSlider = length;
                StopPlayback();
            }

            float sampleTime = Mathf.Min(_timeSlider + _startTime, _endTime);
            retargetAnimBaker.RetargetAtTime(activeAnimation, GetProfileItemAnimation(), sampleTime);
            _previewBoundsCache.Invalidate();

            Repaint();
            if (_lastFrameTime > 0f) EditorApplication.QueuePlayerLoopUpdate();
        }

        private void RequestPreviewSample()
        {
            AnimationClip activeAnimation = GetActiveAnimation();
            if (retargetAnimBaker == null || activeAnimation == null || !TryInitializePreview())
            {
                return;
            }

            float sampleTime = Mathf.Clamp(_timeSlider + _startTime, _startTime, _endTime);
            retargetAnimBaker.RetargetAtTime(activeAnimation, GetProfileItemAnimation(), sampleTime);
            _previewBoundsCache.Invalidate();
            Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private bool TryGetCombinedBounds(out Bounds bounds)
        {
            return _previewBoundsCache.TryGetCombinedBounds(_lastSourceInstance, _lastTargetInstance,
                GetClipItemRootTransform(), out bounds);
        }

        private Transform GetClipItemRootTransform()
        {
            if (retargetAnimBaker == null || _lastTargetInstance == null)
            {
                return null;
            }

            GameObject itemInstance = retargetAnimBaker.ItemInstance;
            if (itemInstance == null)
            {
                return null;
            }

            Transform itemTransform = itemInstance.transform;
            return itemTransform.IsChildOf(_lastTargetInstance.transform) ? itemTransform : null;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                child.gameObject.layer = layer;
            }
        }

        private static T CreateHiddenObject<T>(string name, Scene scene) where T : Component
        {
            GameObject go = CreateHiddenGameObject(name, scene);
            return go != null ? go.AddComponent<T>() : null;
        }

        private static GameObject CreateHiddenGameObject(string name, Scene scene)
        {
            if (!scene.IsValid())
            {
                return null;
            }

            var gameObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            gameObject.layer = RetargetAnimBaker.PreviewLayer;

            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private void CleanupScenePreviewObjects()
        {
            DestroyComponentObject(ref _previewCamera);
            DestroyComponentObject(ref _previewKeyLight);
            DestroyComponentObject(ref _previewFillLight);
            DestroyGameObject(ref _sourceAnchor);
            DestroyGameObject(ref _targetAnchor);
            _previewBoundsCache?.Invalidate();
            _previewEnvironment?.CleanupObjects();
        }

        private void CleanupScenePreviewResources()
        {
            CleanupScenePreviewObjects();
            _previewMaterialCache?.Cleanup();

            if (_previewTexture != null)
            {
                _previewTexture.Release();
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            _previewTextureWidth = 0;
            _previewTextureHeight = 0;
            _previewTextureRenderCount = 0;
            _refreshHdrpDx12PreviewTexture = false;
            _previewEnvironment?.CleanupResources();
        }

        private bool FlushWindowIfCharacterModelsDeleted()
        {
            if (retargetAnimBaker == null)
            {
                return false;
            }

            RetargetProfile profile = retargetAnimBaker.retargetProfile;
            if (profile == null || (profile.sourceCharacter != null && profile.targetCharacter != null))
            {
                return false;
            }

            bool hasWindowState = retargetAnimBaker.IsInitialized ||
                                  retargetAnimBaker.HasPreviewCharacters ||
                                  retargetAnimBaker.PreviewScene.IsValid() ||
                                  _lastSourceInstance != null ||
                                  _lastTargetInstance != null ||
                                  _previewCamera != null ||
                                  _previewKeyLight != null ||
                                  _previewFillLight != null ||
                                  _sourceAnchor != null ||
                                  _targetAnchor != null ||
                                  _previewTexture != null ||
                                  _lastException != null ||
                                  !string.IsNullOrEmpty(_initError);
            if (!hasWindowState)
            {
                return false;
            }

            try
            {
                StopPlayback();
                retargetAnimBaker.UnInitializeBaker();
                retargetAnimBaker.CleanupPreviewResources();
                CleanupScenePreviewResources();
            }
            catch (Exception ex)
            {
                AddHelpMessage($"Failed to reset Retarget Pro preview state: {ex.Message}", MessageType.Error);
            }
            finally
            {
                ResetRuntimeOnlyState();
                _initError = string.Empty;
                _profileStatusMessage = string.Empty;
                _profileStatusType = MessageType.Info;
                RefreshProfileWidget(profile);
            }

            Repaint();
            return true;
        }

        private static void DestroyComponentObject<T>(ref T component) where T : Component
        {
            if (component == null)
            {
                return;
            }

            if (component.gameObject != null)
            {
                DestroyImmediate(component.gameObject);
            }

            component = null;
        }

        private static void DestroyGameObject(ref GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            DestroyImmediate(gameObject);
            gameObject = null;
        }

        private bool TryDraw(Action drawAction, string context)
        {
            try
            {
                drawAction?.Invoke();
                return true;
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CaptureException(ex, context);
                return false;
            }
        }

        private void CaptureException(Exception ex, string context)
        {
            _lastException = ex;
            _lastErrorContext = context;
            AddHelpMessage($"{context}: {ex.Message}", MessageType.Error);
        }

        private void DrawErrorUI()
        {
            EditorGUILayout.HelpBox("Retarget Pro encountered an error. The window is still running.",
                MessageType.Error);

            if (!string.IsNullOrEmpty(_lastErrorContext))
            {
                EditorGUILayout.LabelField("Context", _lastErrorContext);
            }

            using (var scope = new GUILayout.ScrollViewScope(_errorScroll, GUILayout.Height(160f)))
            {
                string message = _lastException != null ? _lastException.ToString() : "Unknown error.";
                EditorGUILayout.SelectableLabel(message, GUILayout.ExpandHeight(true));
                _errorScroll = scope.scrollPosition;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Error"))
                {
                    _lastException = null;
                    _lastErrorContext = null;
                }

                if (GUILayout.Button("Reset Preview"))
                {
                    StopRetargetPreview();
                    MarkPreviewDirty();
                }
            }
        }
    }
}
