// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.UI
{
    internal readonly struct RetargetProfileFieldChangeSet
    {
        public bool changed { get; }
        public bool requiresPreviewRefresh { get; }

        public RetargetProfileFieldChangeSet(bool changed, bool requiresPreviewRefresh)
        {
            this.changed = changed;
            this.requiresPreviewRefresh = requiresPreviewRefresh;
        }
    }

    internal static class RetargetProfileFieldsGUI
    {
        public static RetargetProfileFieldChangeSet Draw(RetargetProfile profile, bool showModels, bool showPoses,
            bool showRigs, bool showItem = false, bool showHeader = true, bool showInitializeButton = true,
            string headerLabel = "Profile Settings")
        {
            if (profile == null)
            {
                return default;
            }

            if (showHeader)
            {
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);
            }

            GameObject sourceCharacter = profile.sourceCharacter;
            GameObject targetCharacter = profile.targetCharacter;
            AnimationClip sourcePose = profile.sourcePose;
            AnimationClip targetPose = profile.targetPose;
            KRig sourceRig = profile.sourceRig;
            KRig targetRig = profile.targetRig;

            if (showModels)
            {
                sourceCharacter = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Source Character", "The model to retarget FROM."),
                    profile.sourceCharacter, typeof(GameObject), false);
                targetCharacter = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Target Character", "The model to retarget TO."),
                    profile.targetCharacter, typeof(GameObject), false);
            }

            if (showPoses)
            {
                sourcePose = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Source Pose", "The pose sampled before retargeting on the source model."),
                    profile.sourcePose, typeof(AnimationClip), false);
                targetPose = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Target Pose", "The pose sampled before retargeting on the target model."),
                    profile.targetPose, typeof(AnimationClip), false);
            }

            if (showRigs)
            {
                sourceRig = (KRig)EditorGUILayout.ObjectField(
                    new GUIContent("Source Rig", "Rig asset with bone chains for the source model."),
                    profile.sourceRig, typeof(KRig), false);
                targetRig = (KRig)EditorGUILayout.ObjectField(
                    new GUIContent("Target Rig", "Rig asset with bone chains for the target model."),
                    profile.targetRig, typeof(KRig), false);
            }

            bool modelsChanged = sourceCharacter != profile.sourceCharacter || targetCharacter != profile.targetCharacter;
            bool posesChanged = sourcePose != profile.sourcePose || targetPose != profile.targetPose;
            bool rigsChanged = sourceRig != profile.sourceRig || targetRig != profile.targetRig;

            string warningMessage = string.Empty;
            MessageType warningType = MessageType.Warning;
            bool changed = false;
            bool requiresPreviewRefresh = false;

            if (modelsChanged || posesChanged || rigsChanged)
            {
                Undo.RecordObject(profile, "Edit Retarget Profile");

                profile.sourceCharacter = sourceCharacter;
                profile.targetCharacter = targetCharacter;
                profile.sourcePose = sourcePose;
                profile.targetPose = targetPose;
                profile.sourceRig = sourceRig;
                profile.targetRig = targetRig;

                changed = true;
                requiresPreviewRefresh = modelsChanged || posesChanged || rigsChanged;

                if (modelsChanged)
                {
                    if (!RetargetProfileModelRigUtility.TryComposeProfileRigs(profile, true, out warningMessage))
                    {
                        warningType = MessageType.Warning;
                    }
                    else
                    {
                        warningMessage = string.Empty;
                    }
                }
                else if (rigsChanged)
                {
                    profile.OnRigUpdated();
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            bool canInitializeMissingRigs = showInitializeButton && (showModels || showRigs) &&
                                           profile.sourceCharacter != null && profile.targetCharacter != null &&
                                           (profile.sourceRig == null || profile.targetRig == null);
            if (canInitializeMissingRigs)
            {
                if (GUILayout.Button("Create Missing Rigs"))
                {
                    if (!RetargetProfileModelRigUtility.TryEnsureProfileRigs(profile, true, out warningMessage))
                    {
                        warningType = MessageType.Warning;
                    }
                    else
                    {
                        changed = true;
                        requiresPreviewRefresh = true;
                        warningMessage = string.Empty;
                    }
                }
            }

            if (!string.IsNullOrEmpty(warningMessage))
            {
                EditorGUILayout.HelpBox(warningMessage, warningType);
            }

            RetargetProfileFieldChangeSet rootMotionFieldChanges = showRigs ? DrawRootMotionField(profile, false) : default;
            RetargetProfileFieldChangeSet itemFieldChanges = showItem ? DrawItemFields(profile, false) : default;
            return new RetargetProfileFieldChangeSet(
                changed || rootMotionFieldChanges.changed || itemFieldChanges.changed,
                requiresPreviewRefresh || rootMotionFieldChanges.requiresPreviewRefresh ||
                itemFieldChanges.requiresPreviewRefresh);
        }

        public static RetargetProfileFieldChangeSet DrawRootMotionField(RetargetProfile profile, bool showHeader = true,
            string headerLabel = "Root Motion")
        {
            if (profile == null)
            {
                return default;
            }

            if (showHeader)
            {
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);
            }

            KRigElement previousRootMotionBone = profile.rootMotionBone;

            var serializedObject = new SerializedObject(profile);
            serializedObject.UpdateIfRequiredOrScript();

            SerializedProperty rootMotionBoneProperty =
                serializedObject.FindProperty(nameof(RetargetProfile.rootMotionBone));

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(rootMotionBoneProperty,
                new GUIContent("Root Node",
                    "Optional target bone used for generic root motion baking. When empty, the target model transform is used."));
            if (!EditorGUI.EndChangeCheck())
            {
                return default;
            }

            if (!serializedObject.ApplyModifiedProperties())
            {
                return default;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            bool changed = !RigElementsEqual(previousRootMotionBone, profile.rootMotionBone);
            return new RetargetProfileFieldChangeSet(changed, false);
        }

        public static RetargetProfileFieldChangeSet DrawItemFields(RetargetProfile profile, bool showHeader = true,
            string headerLabel = "Item")
        {
            if (profile == null)
            {
                return default;
            }

            if (showHeader)
            {
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);
            }

            GameObject previousItemModel = profile.clipItemModel;
            KRigElement previousItemBone = profile.clipItemBone;
            Vector3 previousItemRotationOffset = profile.clipItemRotationOffset;

            var serializedObject = new SerializedObject(profile);
            serializedObject.UpdateIfRequiredOrScript();

            SerializedProperty itemModelProperty = serializedObject.FindProperty(nameof(RetargetProfile.clipItemModel));
            SerializedProperty itemAnimationProperty = serializedObject.FindProperty(nameof(RetargetProfile.clipItemAnimation));
            SerializedProperty itemBoneProperty = serializedObject.FindProperty(nameof(RetargetProfile.clipItemBone));
            SerializedProperty itemRotationOffsetProperty =
                serializedObject.FindProperty(nameof(RetargetProfile.clipItemRotationOffset));

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(itemModelProperty,
                new GUIContent("Item Model", "Secondary model attached to the target skeleton."));
            EditorGUILayout.PropertyField(itemAnimationProperty,
                new GUIContent("Item Animation", "Optional animation sampled on the item during preview playback."));
            EditorGUILayout.PropertyField(itemBoneProperty,
                new GUIContent("Attach Bone", "Target skeleton bone used as the item parent."));
            EditorGUILayout.PropertyField(itemRotationOffsetProperty,
                new GUIContent("Rotation Offset", "Euler rotation offset applied when snapping the item to the bone."));
            if (!EditorGUI.EndChangeCheck())
            {
                return default;
            }

            if (!serializedObject.ApplyModifiedProperties())
            {
                return default;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            bool requiresPreviewRefresh = previousItemModel != profile.clipItemModel ||
                                          !RigElementsEqual(previousItemBone, profile.clipItemBone) ||
                                          previousItemRotationOffset != profile.clipItemRotationOffset;
            return new RetargetProfileFieldChangeSet(true, requiresPreviewRefresh);
        }

        private static bool RigElementsEqual(KRigElement left, KRigElement right)
        {
            return left.index == right.index &&
                   left.depth == right.depth &&
                   left.name == right.name;
        }
    }
}
