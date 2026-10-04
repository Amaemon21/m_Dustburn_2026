// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Utility;

using UnityEditor;
using UnityEngine;

namespace KINEMATION.MotionWarping.Editor.Core
{
    public class WarpingEditorUtility
    {
        public static Vector3 GetVectorValue(AnimationClip clip, EditorCurveBinding[] bindings, float time)
        {
            float tX = AnimationUtility.GetEditorCurve(clip, bindings[0])?.Evaluate(time) ?? 0f;
            float tY = AnimationUtility.GetEditorCurve(clip, bindings[1])?.Evaluate(time) ?? 0f;
            float tZ = AnimationUtility.GetEditorCurve(clip, bindings[2])?.Evaluate(time) ?? 0f;

            return new Vector3(tX, tY, tZ);
        }
        
        public static WarpingCurve CreateWarpingCurve(MotionWarpingAsset motionWarpingAsset, 
            EditorCurveBinding[] tBindings = null)
        {
            WarpingCurve warpingCurve = new WarpingCurve();
            if (tBindings == null) return warpingCurve;

            warpingCurve.X = new AnimationCurve();
            warpingCurve.Y = new AnimationCurve();
            warpingCurve.Z = new AnimationCurve();

            float sampleRate = motionWarpingAsset.animation.frameRate;
            float playback = 0f, length = motionWarpingAsset.GetLength();

            Vector3 refVector3 = GetVectorValue(motionWarpingAsset.animation, tBindings, playback);
            
            Vector3 refT = new Vector3()
            {
                x = refVector3.x,
                y = refVector3.y,
                z = refVector3.z
            };

            while (playback <= length)
            {
                Vector3 root = GetVectorValue(motionWarpingAsset.animation, tBindings, playback);
                Vector3 delta = root - refT;

                warpingCurve.X.AddKey(playback, delta.x);
                warpingCurve.Y.AddKey(playback, delta.y);
                warpingCurve.Z.AddKey(playback, delta.z);
                    
                playback += 1f / sampleRate;
            }
            
            return warpingCurve;
        }
    }
}