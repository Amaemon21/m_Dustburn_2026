// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.RetargetPro.Runtime.Features
{
    public abstract class RetargetFeatureState
    {
        protected KRigComponent sourceRigComponent;
        protected KRigComponent targetRigComponent;

#if UNITY_EDITOR
        private const float BoneSizeFactor = 0.15f;
        private const float BoneSizeScale = 1f;
        private const float BoneFillAlphaFactor = 0.22f;
        private const float MinBoneLength = 0.0001f;
        private const float DefaultBoneSize = 0.02f;
        
        protected static readonly Color SourceBoneChainColor = new Color(1f, 0.45f, 0.05f, 1f);
        protected static readonly Color TargetBoneChainColor = Color.cyan;

        private static void DrawPyramid(Vector3 point1, Vector3 point2, Vector3 size, Color color)
        {
            Vector3 direction = point2 - point1;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            direction.Normalize();
            
            Vector3 baseCenter = point2;
            Vector3 tangent = Mathf.Abs(Vector3.Dot(direction, Vector3.right)) > 0.95f
                ? Vector3.up
                : Vector3.right;
            Vector3 up = Vector3.Cross(direction, tangent).normalized * size.y / 2f;
            Vector3 right = Vector3.Cross(direction, up).normalized * size.x / 2f;

            Vector3 baseVertex1 = baseCenter + up + right;
            Vector3 baseVertex2 = baseCenter + up - right;
            Vector3 baseVertex3 = baseCenter - up - right;
            Vector3 baseVertex4 = baseCenter - up + right;
            
            Color originalColor = Handles.color;
            Matrix4x4 originalMatrix = Handles.matrix;
            
            Color fillColor = color;
            fillColor.a *= BoneFillAlphaFactor;
            Handles.color = fillColor;
            Handles.DrawAAConvexPolygon(point1, baseVertex1, baseVertex2);
            Handles.DrawAAConvexPolygon(point1, baseVertex2, baseVertex3);
            Handles.DrawAAConvexPolygon(point1, baseVertex3, baseVertex4);
            Handles.DrawAAConvexPolygon(point1, baseVertex4, baseVertex1);
            Handles.DrawAAConvexPolygon(baseVertex1, baseVertex2, baseVertex3, baseVertex4);
            
            Handles.color = color;
            Handles.DrawLine(point1, baseVertex1);
            Handles.DrawLine(point1, baseVertex2);
            Handles.DrawLine(point1, baseVertex3);
            Handles.DrawLine(point1, baseVertex4);

            Handles.DrawLine(baseVertex1, baseVertex2);
            Handles.DrawLine(baseVertex2, baseVertex3);
            Handles.DrawLine(baseVertex3, baseVertex4);
            Handles.DrawLine(baseVertex4, baseVertex1);
            
            Handles.color = originalColor;
            Handles.matrix = originalMatrix;
        }
        
        private static void DrawWireSphere(Vector3 position, float radius, Color color)
        {
            Color originalColor = Handles.color;
            
            Handles.color = color;
            
            Handles.DrawWireArc(position, Vector3.up, Vector3.forward, 360, radius);
            Handles.DrawWireArc(position, Vector3.right, Vector3.up, 360, radius);
            Handles.DrawWireArc(position, Vector3.forward, Vector3.right, 360, radius);
            
            Handles.color = originalColor;
        }

        private static float GetBaseBoneSize(KTransformChain chain)
        {
            List<float> lengths = new List<float>();
            int num = chain.transformChain.Count;
            
            for (int i = 0; i < num; i++)
            {
                Transform bone = chain.transformChain[i];
                if (bone == null)
                {
                    continue;
                }
                
                Transform nextBone = null;
                if (i < num - 1)
                {
                    nextBone = chain.transformChain[i + 1];
                }
                
                if (nextBone == null || nextBone != null && !nextBone.IsChildOf(bone))
                {
                    nextBone = bone.childCount == 1 ? bone.GetChild(0) : null;
                }

                if (nextBone == null)
                {
                    continue;
                }

                float length = Vector3.Distance(bone.position, nextBone.position);
                if (length > MinBoneLength)
                {
                    lengths.Add(length);
                }
            }

            if (lengths.Count == 0)
            {
                return DefaultBoneSize;
            }

            lengths.Sort();
            return lengths[lengths.Count / 2] * BoneSizeFactor * BoneSizeScale;
        }
        
        public void RenderBoneChain(KTransformChain chain, Color color)
        {
            if (chain == null || chain.transformChain == null)
            {
                return;
            }
            
            int num = chain.transformChain.Count;
            float baseBoneSize = GetBaseBoneSize(chain);
            for (int i = 0; i < num; i++)
            {
                Transform bone = chain.transformChain[i];
                if (bone == null)
                {
                    continue;
                }

                Transform nextBone = null;

                if (i < num - 1)
                {
                    nextBone = chain.transformChain[i + 1];
                }
                
                if (nextBone == null || nextBone != null && !nextBone.IsChildOf(bone))
                {
                    nextBone = bone.childCount == 1 ? bone.GetChild(0) : null;
                }
                
                Vector3 origin = bone.position;
                
                if (nextBone != null)
                {
                    Vector3 target = nextBone.position;
                    DrawPyramid(target, origin, Vector3.one * baseBoneSize, color);
                    continue;
                }
                
                DrawWireSphere(origin, baseBoneSize * 0.5f, color);
            }
        }

        protected void RenderSourceAndTargetBoneChains(KTransformChain sourceChain, KTransformChain targetChain,
            Color targetColor)
        {
            RenderBoneChain(sourceChain, SourceBoneChainColor);
            RenderBoneChain(targetChain, targetColor);
        }
        
        public virtual void OnSceneGUI()
        {
            OnSceneGUI(true, true);
        }

        public virtual void OnSceneGUI(bool drawBoneRendering, bool drawTransformHandles)
        {
            if (drawBoneRendering)
            {
                OnBoneRenderingSceneGUI();
            }

            if (drawTransformHandles)
            {
                OnTransformHandlesSceneGUI();
            }
        }

        protected virtual void OnBoneRenderingSceneGUI()
        {
        }

        protected virtual void OnTransformHandlesSceneGUI()
        {
        }
#endif

        public void InitializeComponents(KRigComponent sourceComponent, KRigComponent targetComponent, 
            RetargetFeature featureAsset)
        {
            sourceRigComponent = sourceComponent;
            targetRigComponent = targetComponent;
            Initialize(featureAsset);
        }
        
        public virtual bool IsValid()
        {
            return false;
        }

        public virtual void Retarget(float time = 0f)
        {
        }

        public virtual void OnDestroy()
        {
        }
        
        protected Transform GetSourceRoot()
        {
            return sourceRigComponent.transform;
        }

        protected Transform GetTargetRoot()
        {
            return targetRigComponent.transform;
        }
        
        protected virtual void Initialize(RetargetFeature newAsset)
        {
        }
    }
}
