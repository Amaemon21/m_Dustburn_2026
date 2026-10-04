// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    [Serializable]
    internal struct RetargetWindowObjectReference
    {
        public string guid;
        public long localId;

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(guid);
        }
    }

    [Serializable]
    internal sealed class RetargetWindowBakerState
    {
        public bool copyClipSettings = true;
        public bool useRootMotion = true;
        public float frameRate = 30f;
        public int loopCount = 1;
        public bool useSourceFrameRateByDefault = true;
        public RetargetAnimBaker.BakeOutputType outputType = RetargetAnimBaker.BakeOutputType.AnimationClip;
        public string selectedBakerId = string.Empty;
        public Vector3 rootRotationOffsetEuler = Vector3.zero;
    }

    [Serializable]
    internal sealed class RetargetWindowState
    {
        public int version = 1;
        public RetargetWindowObjectReference profile;
        public RetargetWindowBakerState baker = new RetargetWindowBakerState();
        public List<RetargetWindowObjectReference> animationClips = new List<RetargetWindowObjectReference>();
        public int selectedAnimationIndex;
        public float startTime;
        public float endTime; 
        public Vector2 scrollPosition;
        public Vector3 previewPivot = Vector3.zero;
        public float previewDistance = 6f;
        public float previewYaw = 130f;
        public float previewPitch = 20f;
        public float previewRoll;
        public float previewFieldOfView = 35f;
        public float previewNearClipPlane = 0.01f;
        public bool previewCameraAtPivot;
        public float panelWidthRatio = 0.3f;
        public bool sceneCursorEnabled;
        public bool sceneCursorInitialized;
        public Vector3 sceneCursorPosition = Vector3.zero;
        public Quaternion sceneCursorRotation = Quaternion.identity;
        public bool hasSavedPreviewCameraPose;
        public Vector3 savedPreviewPivot = Vector3.zero;
        public float savedPreviewDistance = 6f;
        public float savedPreviewYaw = 130f;
        public float savedPreviewPitch = 20f;
        public float savedPreviewRoll;
        public bool savedPreviewCameraAtPivot;
        public bool previewLayoutDirty = true;
        public bool needsFrame = true;
        public bool preserveCameraOnNextPreviewRefresh;
        public int infoTabIndex;
        public string initError = string.Empty;
        public string profileStatusMessage = string.Empty;
        public int profileStatusType = (int) MessageType.Info;
    }

    internal static class RetargetWindowAppState
    {
        private const string StateKey = "KINEMATION.RetargetPro.Editor.Window.State";

        public static void Save(RetargetWindowState state)
        {
            if (state == null)
            {
                SessionState.SetString(StateKey, string.Empty);
                return;
            }

            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        public static bool TryLoad(out RetargetWindowState state)
        {
            state = null;

            string json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            state = JsonUtility.FromJson<RetargetWindowState>(json);
            return state != null;
        }

        public static RetargetWindowObjectReference CaptureObjectReference(Object obj)
        {
            if (obj == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            {
                return default;
            }

            return new RetargetWindowObjectReference
            {
                guid = guid,
                localId = localId
            };
        }

        public static T ResolveObjectReference<T>(RetargetWindowObjectReference reference) where T : Object
        {
            if (!reference.IsValid())
            {
                return null;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(reference.guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not T typedAsset)
                {
                    continue;
                }

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(typedAsset, out string guid, out long localId))
                {
                    continue;
                }

                if (string.Equals(guid, reference.guid, StringComparison.Ordinal) && localId == reference.localId)
                {
                    return typedAsset;
                }
            }

            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }
    }
}
