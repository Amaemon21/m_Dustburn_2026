// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Editor.Scripts.UI;
using KINEMATION.RetargetPro.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    public struct AnimationRetargetTask
    {
        public AnimationClip[] clips;
        public RuntimeAnimatorController controller;
    }
    
    public class RetargetContextWindow : EditorWindow
    {
        private const string MenuItemName = "Assets/Retarget Animations";
        private static readonly Color SectionBackgroundColor = Color.white;

        private RetargetAnimBaker _animBaker;
        private AnimationRetargetTask[] _tasks;
        private string _lastError;
        private Vector2 _scrollPosition;
        private readonly FoldoutSection _profileSetupSection =
            new(nameof(RetargetContextWindow), "ProfileSetup", "Profile Setup");
        private readonly FoldoutSection _clipSettingsSection =
            new(nameof(RetargetContextWindow), "ClipSettings", "Clip & Save Settings");
        private readonly FoldoutSection _batchSection =
            new(nameof(RetargetContextWindow), "BatchRetargeting", "Batch Retargeting");

        private bool HasControllerTasks()
        {
            if (_tasks == null)
            {
                return false;
            }

            for (int i = 0; i < _tasks.Length; i++)
            {
                if (_tasks[i].controller != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawProfileSetupSection()
        {
            RetargetProfile selectedProfile = RetargetQuickSettingsGUI.DrawTargetProfileField(_animBaker);
            if (selectedProfile != _animBaker.retargetProfile)
            {
                _lastError = string.Empty;
                _animBaker.SetProfile(selectedProfile, true);
            }
        }

        private void DrawQuickSettingsSection()
        {
            RetargetQuickSettingsGUI.DrawClipAndSaveSettings(_animBaker, false);
        }

        private void DrawTaskSummary()
        {
            int clipCount = 0;
            int controllerCount = 0;
            bool hasDirectClipTask = false;

            if (_tasks != null)
            {
                for (int i = 0; i < _tasks.Length; i++)
                {
                    AnimationRetargetTask task = _tasks[i];
                    if (task.controller != null)
                    {
                        controllerCount++;
                    }
                    else
                    {
                        hasDirectClipTask = true;
                    }

                    if (task.clips != null)
                    {
                        clipCount += task.clips.Length;
                    }
                }
            }

            string summary = $"{clipCount} clip{(clipCount == 1 ? string.Empty : "s")} selected";
            if (controllerCount > 0)
            {
                summary += $" across {controllerCount} controller{(controllerCount == 1 ? string.Empty : "s")}";
                if (hasDirectClipTask)
                {
                    summary += " plus direct clip selections";
                }
            }

            EditorGUILayout.HelpBox(summary + ".", MessageType.Info);
        }

        private AnimationClip GetReferenceClip()
        {
            if (_tasks == null)
            {
                return null;
            }

            for (int i = 0; i < _tasks.Length; i++)
            {
                AnimationClip[] clips = _tasks[i].clips;
                if (clips == null)
                {
                    continue;
                }

                for (int j = 0; j < clips.Length; j++)
                {
                    if (clips[j] != null)
                    {
                        return clips[j];
                    }
                }
            }

            return null;
        }

        private void SyncQuickSettingDefaultsFromTasks()
        {
            if (_animBaker == null)
            {
                return;
            }

            AnimationClip referenceClip = GetReferenceClip();
            _animBaker.SyncFrameRateFromSource(referenceClip);
        }

        private void DrawBatchRetargetSection()
        {
            if (_tasks == null || _tasks.Length == 0)
            {
                EditorGUILayout.HelpBox("No retarget tasks found.", MessageType.Warning);
                return;
            }

            DrawTaskSummary();

            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
            }

            bool canRetarget = _animBaker.TryGetValidationMessage(out string validationMessage, out MessageType validationType);
            if (!canRetarget)
            {
                EditorGUILayout.HelpBox(validationMessage, validationType);
            }

            bool fbxControllersUnsupported = _animBaker.OutputType == RetargetAnimBaker.BakeOutputType.Fbx &&
                                             HasControllerTasks();
            if (fbxControllersUnsupported)
            {
                EditorGUILayout.HelpBox(
                    "FBX output mode does not support creating Animator Controller overrides. Switch Bake Output to Animation Clip.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledGroupScope(!canRetarget || fbxControllersUnsupported))
            {
                if (!GUILayout.Button("Retarget Animations"))
                {
                    return;
                }
            }

            _lastError = string.Empty;

            if (!_animBaker.TryInitializeBaker(out string error))
            {
                _lastError = error;
                return;
            }

            try
            {
                foreach (var task in _tasks)
                {
                    bool isController = task.controller != null;
                    if (isController && task.clips == null) continue;

                    AnimatorOverrideController overrideController =
                        isController ? new AnimatorOverrideController(task.controller) : null;
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();

                    List<AnimationClip> bakedClips = isController ? new List<AnimationClip>() : null;
                    foreach (var clip in task.clips)
                    {
                        AnimationClip bakedClip = _animBaker.BakeAnimation(clip);

                        if (isController && bakedClip != null)
                        {
                            overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(clip, bakedClip));
                            bakedClips.Add(bakedClip);
                        }
                    }

                    if (!isController) continue;

                    for (int i = 0; i < overrides.Count; i++)
                    {
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, bakedClips[i]);
                    }

                    overrideController.ApplyOverrides(overrides);
                    RuntimeAnimatorController newAsset = overrideController;

                    string path = _animBaker.SavePath;
                    string controllerName = $"{_animBaker.GetTargetName()}_{task.controller.name}.controller";

                    path = $"{path}/{controllerName}";

                    AssetDatabase.CreateAsset(newAsset, AssetDatabase.GenerateUniqueAssetPath(path));
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Retargeting failed: {ex.Message}";
                Debug.LogException(ex);
            }
            finally
            {
                _animBaker.UnInitializeBaker();
            }
        }

        private void OnEnable()
        {
            _animBaker ??= new RetargetAnimBaker();
            SyncQuickSettingDefaultsFromTasks();
        }

        private void OnGUI()
        {
            _animBaker ??= new RetargetAnimBaker();
            SyncQuickSettingDefaultsFromTasks();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            _profileSetupSection.DrawLayout(EditorStyles.helpBox, EditorStyles.boldLabel, SectionBackgroundColor,
                DrawProfileSetupSection);
            _clipSettingsSection.DrawLayout(EditorStyles.helpBox, EditorStyles.boldLabel, SectionBackgroundColor,
                DrawQuickSettingsSection);
            _batchSection.DrawLayout(EditorStyles.helpBox, EditorStyles.boldLabel, SectionBackgroundColor,
                DrawBatchRetargetSection);
            EditorGUILayout.EndScrollView();
        }

        private void OnDisable()
        {
            _animBaker?.CleanupPreviewResources();
        }

        private static AnimationRetargetTask[] TryGetAnimationTasks()
        {
            if (Selection.objects == null) return null;
            
            List<AnimationClip> clips = null;
            List<AnimationRetargetTask> tasks = null;

            foreach (var selection in Selection.objects)
            {
                if (selection is AnimationClip clip)
                {
                    clips ??= new List<AnimationClip>();
                    clips.Add(clip);
                    continue;
                }

                if (selection is RuntimeAnimatorController controller && controller.animationClips.Length > 0)
                {
                    tasks ??= new List<AnimationRetargetTask>();
                    tasks.Add(new AnimationRetargetTask()
                    {
                        clips = controller.animationClips,
                        controller = controller
                    });
                }
                
                string assetPath = AssetDatabase.GetAssetPath(selection);
                if (!assetPath.ToLower().EndsWith(".fbx")) continue;
                
                foreach (Object subAsset in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                {
                    clip = subAsset as AnimationClip;
                    if (clip == null) continue;

                    clips ??= new List<AnimationClip>();
                    clips.Add(clip);
                }
            }

            if (clips != null)
            {
                tasks ??= new List<AnimationRetargetTask>();
                tasks.Add(new AnimationRetargetTask()
                {
                    clips = clips.ToArray(),
                    controller = null
                });
            }

            return tasks?.ToArray();
        }
        
        [MenuItem(MenuItemName, true)]
        private static bool ValidateRetargetAnimations()
        {
            var animations = TryGetAnimationTasks();
            return animations != null;
        }

        [MenuItem(MenuItemName)]
        private static void RetargetAnimations()
        {
            var tasks = TryGetAnimationTasks();
            if (tasks == null) return;

            RetargetContextWindow window = GetWindow<RetargetContextWindow>(false, 
                "Retarget Animations", true);

            window.minSize = new Vector2(400f, 400f);
            window._tasks = tasks;
            window._lastError = string.Empty;
            window.SyncQuickSettingDefaultsFromTasks();
            window.Show();
        }
    }
}