// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core
{
    public class AnimationAssetCreator
    {
        [MenuItem("Assets/Create/KINEMATION/CAS/Animation Asset", true, 0)]
        private static bool ValidateCreateAnimationAsset()
        {
            return true;
        }

        [MenuItem("Assets/Create/KINEMATION/CAS/Animation Asset", false, 0)]
        private static void CreateAnimationAsset()
        {
            List<AnimationClip> clips = GetSelectedAnimationClips();

            if (clips.Count == 0)
            {
                SaveAnimationAsset(null, KEditorUtility.GetProjectActiveFolder(), "A_NewAnimation.asset");
                return;
            }

            foreach (AnimationClip clip in clips)
            {
                string assetPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip));
                string assetName = clip.name.StartsWith("A_", StringComparison.Ordinal)
                    ? $"{clip.name}.asset"
                    : $"A_{clip.name}.asset";
                SaveAnimationAsset(clip, assetPath, assetName);
            }
        }

        private static void SaveAnimationAsset(AnimationClip clip, string assetPath, string assetName)
        {
            AnimationAsset animAsset = ScriptableObject.CreateInstance<AnimationAsset>();
            animAsset.clip = clip;
            KEditorUtility.SaveAsset(animAsset, assetPath, assetName);
        }

        private static List<AnimationClip> GetSelectedAnimationClips()
        {
            var clips = new List<AnimationClip>();

            foreach (Object selectedObject in Selection.objects)
            {
                AddAnimationClips(selectedObject, clips);
            }

            if (clips.Count == 0)
            {
                AddClip(KEditorUtility.GetAnimationClipFromSelection(), clips);
            }

            return clips;
        }

        private static void AddAnimationClips(Object selectedObject, List<AnimationClip> clips)
        {
            if (selectedObject == null) return;

            if (selectedObject is AnimationClip clip)
            {
                AddClip(clip, clips);
                return;
            }

            string path = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            if (!Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase)) return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                AddClip(asset as AnimationClip, clips);
            }
        }

        private static void AddClip(AnimationClip clip, List<AnimationClip> clips)
        {
            if (clip == null || clips.Contains(clip)) return;
            if ((clip.hideFlags & HideFlags.HideInHierarchy) != 0) return;

            clips.Add(clip);
        }
    }
}