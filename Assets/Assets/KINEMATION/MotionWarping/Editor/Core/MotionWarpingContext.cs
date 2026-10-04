// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.IO;
using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.MotionWarping.Editor.Core
{
    public class MotionWarpingContext
    {
        [MenuItem("Assets/Create Motion Warping Asset", true)]
        private static bool ValidateCreateMotionWarpingAsset()
        {
            return KEditorUtility.GetAnimationClipFromSelection() != null;
        }

        [MenuItem("Assets/Create Motion Warping Asset")]
        private static void CreateMotionWarpingAsset()
        {
            AnimationClip clip = KEditorUtility.GetAnimationClipFromSelection();
            MotionWarpingAsset asset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
            asset.animation = clip;
            
            string assetPath = AssetDatabase.GetAssetPath(clip);
            assetPath = Path.GetDirectoryName(assetPath);
            KEditorUtility.SaveAsset(asset, assetPath, $"Warp_{clip.name}.asset");
        }
    }
}