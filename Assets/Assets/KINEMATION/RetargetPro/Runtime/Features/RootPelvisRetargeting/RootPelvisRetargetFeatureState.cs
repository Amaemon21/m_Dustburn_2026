// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Runtime.Features.BasicRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

#if UNITY_EDITOR
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
#endif

namespace KINEMATION.RetargetPro.Runtime.Features.RootPelvisRetargeting
{
    public class RootPelvisRetargetFeatureState : BasicRetargetFeatureState
    {
        private RootPelvisRetargetFeature _asset;

        protected override void Initialize(RetargetFeature newAsset)
        {
            base.Initialize(newAsset);

            _asset = newAsset as RootPelvisRetargetFeature;
            if (_asset == null || sourceRigComponent == null || targetRigComponent == null)
            {
                return;
            }

            Transform[] sourceTransforms = sourceRigComponent.GetRigTransforms();
            Transform[] targetTransforms = targetRigComponent.GetRigTransforms();

            float bodyScale = RetargetJobUtility.ComputeBodyScale(sourceTransforms, GetSourceRoot(),
                targetTransforms, GetTargetRoot(), chainScale);

            if (bodyScale <= 0f)
            {
                return;
            }

            chainScale = bodyScale;
        }

#if UNITY_EDITOR
        protected override void OnTransformHandlesSceneGUI()
        {
            Transform root = GetTargetRoot();
            Transform target = targetChain.transformChain[0];
            Vector3 handlePosition = KTransformHandles.PositionHandle(target.position, root.rotation,
                KTransformHandles.PositionHandleSettings.Default);

            Vector3 delta = root.InverseTransformPoint(handlePosition) - root.InverseTransformPoint(target.position);
            if (delta.sqrMagnitude > 0f)
            {
                Undo.RecordObject(_asset, "Adjust Root/Pelvis Offset");
                _asset.offset += delta;
                EditorUtility.SetDirty(_asset);
            }
        }
#endif
    }
}
