// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using System.Collections.Generic;
using KINEMATION.RetargetPro.Runtime.Features;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using UnityEngine;

#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

namespace KINEMATION.RetargetPro.Runtime
{
    [CreateAssetMenu(fileName = "NewRetargetProfile", menuName = "KINEMATION/Retarget Profile")]
    public class RetargetProfile : ScriptableObject, IRigObserver
    {
        public const string DefaultSaveFolderPath = "Assets/";

        [Header("Model")]
        [Tooltip("The model to retarget FROM. Rig data is composed from the model hierarchy and skinned mesh bones.")]
        public GameObject sourceCharacter;

        [Tooltip("The model to retarget TO. Rig data is composed from the model hierarchy and skinned mesh bones.")]
        public GameObject targetCharacter;

        [Header("Pose")] [Tooltip("This pose is sampled before retargeting. It can be an A or a T pose.")]
        public AnimationClip sourcePose;

        [Tooltip("This pose is sampled before retargeting. It can be an A or a T pose.")]
        public AnimationClip targetPose;

        [Header("Rig")] [Tooltip("Rig asset with bone chains for the SOURCE model.")]
        public KRig sourceRig;

        [Tooltip("Rig asset with bone chains for the TARGET model.")]
        public KRig targetRig;

        [Tooltip(
            "Optional target bone used as the root motion node during generic baking. When empty, the target model transform is used.")]
        [RigAssetSelector("targetRig")]
        public KRigElement rootMotionBone = new KRigElement(-1, string.Empty);

        [Header("Item")] [Tooltip("Secondary model, such as a weapon, attached to the target skeleton for preview.")]
        public GameObject clipItemModel;

        [Tooltip("Optional animation sampled on the clip item during preview playback.")]
        public AnimationClip clipItemAnimation;

        [Tooltip("Target rig element used as the attachment bone for the clip item.")] [RigAssetSelector("targetRig")]
        public KRigElement clipItemBone;

        [Tooltip("Euler rotation offset applied when attaching the clip item to the target bone.")]
        public Vector3 clipItemRotationOffset = Vector3.zero;

        [field: Header("View")]
        [field: SerializeField]
        public KTransform cameraViewPose { get; set; } = KTransform.Identity;

        [SerializeField, HideInInspector] public string saveFolderPath = string.Empty;

        [HideInInspector] public List<RetargetFeature> retargetFeatures = new List<RetargetFeature>();

        private void Reset()
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(saveFolderPath)) return;
            
            MethodInfo getActiveFolder = typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            string assetPath = getActiveFolder?.Invoke(null, null) as string;
            saveFolderPath = string.IsNullOrEmpty(assetPath) ? DefaultSaveFolderPath : assetPath;
#endif
        }

        public void OnRigUpdated()
        {
#if UNITY_EDITOR
            if (retargetFeatures == null) return;

            foreach (var feature in retargetFeatures)
            {
                if (feature == null) continue;
                feature.sourceRig = sourceRig;
                feature.targetRig = targetRig;
                feature.OnRigUpdated();
            }
#endif
        }

#if UNITY_EDITOR
        private KRig _cachedSourceRig;
        private KRig _cachedTargetRig;

        private void OnValidateRig(KRig rig, ref KRig cachedRig)
        {
            if (rig == cachedRig)
            {
                return;
            }

            if (cachedRig != null)
            {
                cachedRig.UnRegisterObserver(this);
            }

            if (rig != null)
            {
                rig.RegisterRigObserver(this);
            }

            OnRigUpdated();
            cachedRig = rig;
        }

        private void OnValidate()
        {
            OnValidateRig(sourceRig, ref _cachedSourceRig);
            OnValidateRig(targetRig, ref _cachedTargetRig);
        }
#endif
    }
}
