// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor
{
    public class CasEditorUtility
    {
        public static GUIStyle windowUtilityStyle = new GUIStyle()
        {
            padding = new RectOffset(10, 5, 8, 8)
        };
        
        public static void CreateAsset<T>(string assetName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            string path = KEditorUtility.GetProjectActiveFolder();
            assetName += ".asset";
            
            KEditorUtility.SaveAsset(asset, path, assetName);
            
            Selection.activeObject = asset;
        }
    }
}