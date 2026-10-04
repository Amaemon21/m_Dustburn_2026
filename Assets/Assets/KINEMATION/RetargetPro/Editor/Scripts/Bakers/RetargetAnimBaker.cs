// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.Shared.FbxExporter.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    public class RetargetAnimBaker
    {
        public enum BakeOutputType
        {
            AnimationClip = 0,
            Fbx = 1
        }

        private struct CurveRecord
        {
            public EditorCurveBinding binding;
            public AnimationCurve curve;
        }

        public const int PreviewLayer = 31;
        private const float DefaultBakeFrameRate = 30f;
        private const float CurveTimeEpsilon = 0.00001f;
        private const int DefaultLoopCount = 1;
        public bool IsInitialized { get; private set; }
        public RetargetProComponent RetargetComponent { get; private set; }
        public Scene PreviewScene => _previewScene;
        public GameObject SourcePreviewInstance => _sourceCharacterInstance;
        public GameObject TargetPreviewInstance => _targetCharacterInstance;
        public bool HasPreviewCharacters => _sourceCharacterInstance != null && _targetCharacterInstance != null;
        
        private static readonly string[] AnimationBakeOutputOptions =
        {
            "Animation Clip (.anim)",
            "FBX (.fbx)"
        };
        private static readonly string[] AnimatorRootPositionPropertyNames = { "RootT.x", "RootT.y", "RootT.z" };
        private static readonly string[] AnimatorRootRotationPropertyNames = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        private static readonly string[] TransformLocalPositionPropertyNames =
            { "localPosition.x", "localPosition.y", "localPosition.z" };
        private static readonly string[] TransformLocalRotationPropertyNames =
            { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
        private static readonly HashSet<string> HumanoidMusclePropertyNames = BuildHumanoidMusclePropertyNames();

        public Action<RetargetProfile> onProfileChanged;
        
        private GameObject _sourceCharacter;
        private GameObject _targetCharacter;
        private GameObject _itemModel;
        
        private GameObject _sourceCharacterInstance;
        private GameObject _targetCharacterInstance;
        private GameObject _itemInstance;
        private GameObject _itemAttachmentInstance;

        public RetargetProfile retargetProfile;
        private RetargetProfile _cachedRetargetProfile;
        
        private bool _copyClipSettings = true;
        private bool _useRootMotion = true;
        private string _selectedBakerId = string.Empty;
        private KRigComponent _sourceRigComponent;
        private KRigComponent _targetRigComponent;
        private bool _sourceRigComponentTemporary;
        private bool _targetRigComponentTemporary;
        private Scene _previewScene;
        
        private float _frameRate = DefaultBakeFrameRate;
        private int _loopCount = DefaultLoopCount;
        private bool _useSourceFrameRateByDefault = true;
        private BakeOutputType _bakeOutputType = BakeOutputType.AnimationClip;
        private Vector3 _rootRotationOffsetEuler = Vector3.zero;

        private UnityEditor.Editor _sourcePreviewEditor;
        private UnityEditor.Editor _targetPreviewEditor;
        private GameObject _cachedSourcePreviewAsset;
        private GameObject _cachedTargetPreviewAsset;
        private GameObject _sourcePreviewModelAsset;
        private GameObject _targetPreviewModelAsset;
        private Animator _sourcePreviewAnimator;
        private GameObject _itemPreviewModelAsset;
        private KRigElement _itemPreviewBone;
        private Vector3 _itemPreviewRotationOffset;
        private KTransform _sourceRootRestPose = KTransform.Identity;
        private KTransform _targetRootRestPose = KTransform.Identity;
        private KTransform _rootMotionDelta = KTransform.Identity;
        private bool _hasPreviewRootMotionReferencePose;
        private bool _hasPreviewRootMotionDelta;
        
        public string GetTargetName()
        {
            string result = "RetargetResult";

            if (_targetCharacter != null)
            {
                result = _targetCharacter.name;
            }

            return result;
        }

        private string GetBakedClipName(AnimationClip sourceClip)
        {
            string clipName = sourceClip != null && !string.IsNullOrEmpty(sourceClip.name)
                ? sourceClip.name
                : "Clip";
            return $"{GetTargetName()}_{clipName}";
        }

        public GameObject ItemInstance => _itemInstance;

        public float FrameRate
        {
            get => _frameRate;
            set
            {
                float clampedValue = Mathf.Clamp(value, 24f, 240f);
                if (Mathf.Approximately(_frameRate, clampedValue))
                {
                    return;
                }

                _frameRate = clampedValue;
                _useSourceFrameRateByDefault = false;
            }
        }

        public bool CopyClipSettings
        {
            get => _copyClipSettings;
            set => _copyClipSettings = value;
        }

        public int LoopCount
        {
            get => _loopCount;
            set => _loopCount = Mathf.Max(DefaultLoopCount, value);
        }

        public bool UseSourceFrameRateByDefault
        {
            get => _useSourceFrameRateByDefault;
            set => _useSourceFrameRateByDefault = value;
        }

        public bool UseRootMotion
        {
            get => _useRootMotion;
            set
            {
                if (_useRootMotion == value)
                {
                    return;
                }
                _useRootMotion = value;
                if (!_useRootMotion)
                {
                    RestorePreviewRootMotionReferencePose();
                }
            }
        }
        public BakeOutputType OutputType
        {
            get => _bakeOutputType;
            set => _bakeOutputType = value;
        }

        public string SelectedBakerId
        {
            get
            {
                EnsureRigTypeSelection();
                return _selectedBakerId;
            }
            set
            {
                if (!string.IsNullOrEmpty(value) && RetargetProBakerRegistry.GetIndexById(value) >= 0)
                {
                    _selectedBakerId = value;
                }
            }
        }

        public Vector3 RootRotationOffsetEuler
        {
            get => _rootRotationOffsetEuler;
            set => _rootRotationOffsetEuler = value;
        }

        public string SavePath => retargetProfile != null
            ? retargetProfile.saveFolderPath
            : RetargetProfile.DefaultSaveFolderPath;

        public void SetRigType(bool humanoid)
        {
            _selectedBakerId = humanoid ? HumanoidAnimationBaker.BakerId : GenericAnimationBaker.BakerId;
        }

        public void DrawRigTypePopup()
        {
            EnsureRigTypeSelection();

            IReadOnlyList<RetargetProBakerDescriptor> bakers = RetargetProBakerRegistry.GetBakers();
            if (bakers.Count == 0)
            {
                EditorGUILayout.HelpBox("No animation bakers are registered.", MessageType.Error);
                return;
            }

            GUIContent content = new GUIContent("Rig Type",
                "Select Generic output or convert the generic bake to Humanoid using the target avatar.");
            int selectedIndex = RetargetProBakerRegistry.GetIndexById(_selectedBakerId);
            int nextIndex = EditorGUILayout.Popup(content, selectedIndex, RetargetProBakerRegistry.GetDisplayNames());
            if (nextIndex != selectedIndex)
            {
                _selectedBakerId = RetargetProBakerRegistry.GetIdByIndex(nextIndex);
            }
        }
        public void DrawBakeOutputPopup()
        {
            GUIContent outputContent = new GUIContent("Bake Output",
                "Select whether baking creates an Animation Clip (.anim) or an FBX file.");
            OutputType = (BakeOutputType)EditorGUILayout.Popup(outputContent, (int)OutputType, AnimationBakeOutputOptions);

            if (OutputType == BakeOutputType.Fbx)
            {
                EditorGUILayout.HelpBox(
                    "Uses KINEMATION ASCII FBX exporter package.",
                    MessageType.Info);
            }
        }

        private void SelectRigTypeFromTargetAvatar()
        {
            GameObject targetCharacter = retargetProfile != null ? retargetProfile.targetCharacter : null;
            Avatar targetAvatar = targetCharacter != null
                ? targetCharacter.GetComponentInChildren<Animator>(true)?.avatar
                : null;

            SetRigType(targetAvatar != null && targetAvatar.isHuman);
        }

        private void EnsureRigTypeSelection()
        {
            IReadOnlyList<RetargetProBakerDescriptor> bakers = RetargetProBakerRegistry.GetBakers();
            if (bakers.Count == 0)
            {
                _selectedBakerId = string.Empty;
                return;
            }

            if (RetargetProBakerRegistry.GetIndexById(_selectedBakerId) >= 0)
            {
                return;
            }

            if (RetargetProBakerRegistry.GetIndexById(GenericAnimationBaker.BakerId) >= 0)
            {
                _selectedBakerId = GenericAnimationBaker.BakerId;
                return;
            }

            _selectedBakerId = bakers[0].Id;
        }

        public void DrawRootRotationOffsetField()
        {
            GUIContent content = new GUIContent("Root Rotation Offset",
                "Euler offset applied to the target model root before baking.");
            _rootRotationOffsetEuler = EditorGUILayout.Vector3Field(content, _rootRotationOffsetEuler);
        }

        public void DrawLoopCountField()
        {
            GUIContent content = new GUIContent("Loop Count",
                "How many stitched loops of the source animation to include in the baked clip.");
            LoopCount = EditorGUILayout.IntField(content, LoopCount);
        }

        public bool EnsurePreviewCharacters(out string error)
        {
            error = string.Empty;

            if (retargetProfile == null)
            {
                error = "Select Retarget Profile";
                return false;
            }

            SyncCharactersFromProfile();
            if (_sourceCharacter == null || _targetCharacter == null)
            {
                error = "Source/Target models are missing on the profile.";
                return false;
            }

            if (!PreviewInstancesNeedRefresh())
            {
                return true;
            }

            return PreparePreviewCharactersFromProfile(out error);
        }

        public void SwapSourceAndTargetProfiles()
        {
            SwapSourceAndTarget(out _, out _);
        }

        public void SwapSourceAndTargetProfiles(out string message, out MessageType messageType)
        {
            SwapSourceAndTarget(out message, out messageType);
        }

        public void SetProfile(RetargetProfile profile, bool notify = true)
        {
            SetProfile(profile, notify, out _, out _);
        }

        public void SetProfile(RetargetProfile profile, bool notify, out string message, out MessageType messageType)
        {
            message = string.Empty;
            messageType = MessageType.Info;
            bool profileChanged = _cachedRetargetProfile != profile;

            if (profileChanged)
            {
                if (IsInitialized)
                {
                    UnInitializeBaker();
                }

                ReleasePreviewCharacters();
            }

            retargetProfile = profile;

            if (profileChanged)
            {
                SelectRigTypeFromTargetAvatar();
            }

            if (profileChanged && notify)
            {
                onProfileChanged?.Invoke(retargetProfile);
            }

            _cachedRetargetProfile = retargetProfile;

            bool autoMapMissingRigs = retargetProfile != null &&
                                      (retargetProfile.sourceRig == null || retargetProfile.targetRig == null);
            string composeMessage = string.Empty;
            if (retargetProfile != null &&
                !RetargetProfileModelRigUtility.TryComposeProfileRigs(retargetProfile, autoMapMissingRigs,
                    out composeMessage))
            {
                message = $"Failed to compose profile rigs for `{retargetProfile.name}`: {composeMessage}";
                messageType = MessageType.Warning;
            }
            else if (autoMapMissingRigs && !string.IsNullOrEmpty(composeMessage))
            {
                message = composeMessage;
            }

            SyncCharactersFromProfile();

            if (retargetProfile != null && (profileChanged || PreviewInstancesNeedRefresh()))
            {
                if (!PreparePreviewCharactersFromProfile(out string prepareError))
                {
                    string prepareMessage =
                        $"Failed to prepare preview models for `{retargetProfile.name}`: {prepareError}";
                    message = string.IsNullOrEmpty(message) ? prepareMessage : $"{message}\n{prepareMessage}";
                    messageType = MessageType.Warning;
                }
            }

            RefreshPreviewEditors();
        }

        public void CleanupPreviewResources()
        {
            if (_sourcePreviewEditor != null && _sourcePreviewEditor.serializedObject.targetObject != null)
            {
                DestroyEditor(ref _sourcePreviewEditor);
            }
            
            if (_targetPreviewEditor != null && _targetPreviewEditor.serializedObject.targetObject != null)
            {
                DestroyEditor(ref _targetPreviewEditor);
            }
            
            _sourcePreviewEditor = null;
            _targetPreviewEditor = null;

            _cachedSourcePreviewAsset = null;
            _cachedTargetPreviewAsset = null;
            ReleasePreviewCharacters();
        }

        private void SyncCharactersFromProfile()
        {
            _sourceCharacter = retargetProfile != null ? retargetProfile.sourceCharacter : null;
            _targetCharacter = retargetProfile != null ? retargetProfile.targetCharacter : null;
            _itemModel = retargetProfile != null ? retargetProfile.clipItemModel : null;
        }

        private void RefreshPreviewEditors(bool forceRecreate = false)
        {
            if (forceRecreate)
            {
                DestroyEditor(ref _sourcePreviewEditor);
                DestroyEditor(ref _targetPreviewEditor);
                _cachedSourcePreviewAsset = null;
                _cachedTargetPreviewAsset = null;
            }

            GameObject sourcePreviewObject = _sourceCharacter;
            if (_cachedSourcePreviewAsset != sourcePreviewObject)
            {
                DestroyEditor(ref _sourcePreviewEditor);
                _cachedSourcePreviewAsset = sourcePreviewObject;
                if (sourcePreviewObject != null)
                {
                    _sourcePreviewEditor = UnityEditor.Editor.CreateEditor(sourcePreviewObject);
                }
            }

            GameObject targetPreviewObject = _targetCharacter;
            if (_cachedTargetPreviewAsset != targetPreviewObject)
            {
                DestroyEditor(ref _targetPreviewEditor);
                _cachedTargetPreviewAsset = targetPreviewObject;
                if (targetPreviewObject != null)
                {
                    _targetPreviewEditor = UnityEditor.Editor.CreateEditor(targetPreviewObject);
                }
            }
        }

        private static void DestroyEditor(ref UnityEditor.Editor editor)
        {
            if (editor == null)
            {
                return;
            }

            Object.DestroyImmediate(editor);
            editor = null;
        }

        private bool TryBuildHierarchyRigComponent(GameObject characterInstance, out KRigComponent rigComponent,
            out bool temporary, out string error)
        {
            rigComponent = null;
            temporary = false;
            error = string.Empty;

            if (characterInstance == null)
            {
                error = "Character instance is null.";
                return false;
            }

            try
            {
                rigComponent = characterInstance.GetComponent<KRigComponent>();
                if (rigComponent == null)
                {
                    rigComponent = characterInstance.AddComponent<KRigComponent>();
                    temporary = true;
                    rigComponent.hideFlags = HideFlags.HideAndDontSave;
                }
#if UNITY_EDITOR
                rigComponent.RefreshHierarchy();
#endif
                rigComponent.Initialize();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (rigComponent != null)
                {
                    Object.DestroyImmediate(rigComponent);
                    rigComponent = null;
                }

                temporary = false;
                return false;
            }
        }

        private void DestroyTemporaryRigComponents()
        {
            if (_sourceRigComponentTemporary && _sourceRigComponent != null)
            {
                Object.DestroyImmediate(_sourceRigComponent);
            }

            if (_targetRigComponentTemporary && _targetRigComponent != null)
            {
                Object.DestroyImmediate(_targetRigComponent);
            }

            _sourceRigComponent = null;
            _targetRigComponent = null;
            _sourceRigComponentTemporary = false;
            _targetRigComponentTemporary = false;
        }

        private void EnsurePreviewScene()
        {
            if (_previewScene.IsValid())
            {
                return;
            }

            _previewScene = EditorSceneManager.NewPreviewScene();
        }

        private void ClosePreviewScene()
        {
            if (!_previewScene.IsValid())
            {
                return;
            }

            EditorSceneManager.ClosePreviewScene(_previewScene);
            _previewScene = default;
        }

        private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                child.gameObject.hideFlags = hideFlags;
            }
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                child.gameObject.layer = layer;
            }
        }

        private static void DisablePreviewOnlyComponents(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                camera.enabled = false;
            }

            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
            {
                listener.enabled = false;
            }

            foreach (ParticleSystem particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Clear(true);
            }

            foreach (TrailRenderer trailRenderer in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                trailRenderer.enabled = false;
            }

            foreach (LineRenderer lineRenderer in root.GetComponentsInChildren<LineRenderer>(true))
            {
                lineRenderer.enabled = false;
            }
        }

        private GameObject CreatePreviewInstance(GameObject asset, bool resetRootRotation)
        {
            if (asset == null)
            {
                return null;
            }

            EnsurePreviewScene();

            GameObject instance = Object.Instantiate(asset);
            if (instance == null)
            {
                return null;
            }

            SetHideFlagsRecursive(instance, HideFlags.HideAndDontSave);
            SetLayerRecursive(instance, PreviewLayer);
            DisablePreviewOnlyComponents(instance);
            SceneManager.MoveGameObjectToScene(instance, _previewScene);
            instance.transform.position = Vector3.zero;
            if (resetRootRotation)
            {
                ModelRootRotationUtility.ResetRootRotationPreserveFirstChildren(instance.transform);
            }

            return instance;
        }

        private void CachePreviewRootMotionReferencePose()
        {
            _hasPreviewRootMotionReferencePose = _sourcePreviewAnimator != null && _targetCharacterInstance != null;
            _hasPreviewRootMotionDelta = false;
            if (!_hasPreviewRootMotionReferencePose)
            {
                return;
            }

            _sourceRootRestPose = new KTransform(_sourcePreviewAnimator.transform, false);
            _targetRootRestPose = new KTransform(_targetCharacterInstance.transform, false);
            _sourceRootRestPose.rotation = NormalizeQuaternion(_sourceRootRestPose.rotation,
                Quaternion.identity);
            _targetRootRestPose.rotation = NormalizeQuaternion(_targetRootRestPose.rotation,
                Quaternion.identity);
            _rootMotionDelta = KTransform.Identity;
        }

        private void CachePreviewRootMotionDelta()
        {
            _hasPreviewRootMotionDelta = false;
            if (!_hasPreviewRootMotionReferencePose || _sourcePreviewAnimator == null)
            {
                return;
            }

            KTransform sourcePose = new KTransform(_sourcePreviewAnimator.transform, false);
            Quaternion deltaRotation = NormalizeQuaternion(
                sourcePose.rotation * Quaternion.Inverse(_sourceRootRestPose.rotation),
                Quaternion.identity);

            _rootMotionDelta = new KTransform(
                sourcePose.position - _sourceRootRestPose.position,
                deltaRotation,
                Vector3.one);
            _hasPreviewRootMotionDelta = true;
        }

        private void RestorePreviewRootMotionReferencePose()
        {
            if (!_hasPreviewRootMotionReferencePose)
            {
                return;
            }

            if (_sourcePreviewAnimator != null)
            {
                Transform sourceRoot = _sourcePreviewAnimator.transform;
                sourceRoot.localPosition = _sourceRootRestPose.position;
                sourceRoot.localRotation = _sourceRootRestPose.rotation;
            }

            if (_targetCharacterInstance != null)
            {
                Transform targetRoot = _targetCharacterInstance.transform;
                targetRoot.localPosition = _targetRootRestPose.position;
                targetRoot.localRotation = _targetRootRestPose.rotation;
            }
        }

        private void ApplyPreviewRootMotion(KTransform rootMotionBase)
        {
            if (!_hasPreviewRootMotionReferencePose)
            {
                return;
            }

            if (!UseRootMotion || !_hasPreviewRootMotionDelta)
            {
                RestorePreviewRootMotionReferencePose();
                return;
            }

            KTransform rootMotion = CombineRootMotion(rootMotionBase, _rootMotionDelta);

            if (_sourcePreviewAnimator != null)
            {
                Transform sourceRoot = _sourcePreviewAnimator.transform;
                sourceRoot.localPosition = _sourceRootRestPose.position +
                                           rootMotion.position;
                sourceRoot.localRotation = rootMotion.rotation *
                                           _sourceRootRestPose.rotation;
            }

            if (_targetCharacterInstance != null)
            {
                Transform targetRoot = _targetCharacterInstance.transform;
                targetRoot.localPosition = _targetRootRestPose.position +
                                           rootMotion.position;
                targetRoot.localRotation = rootMotion.rotation *
                                           _targetRootRestPose.rotation;
            }
        }

        private bool EnsureSourcePreviewAnimatorConfigured(out string error)
        {
            error = string.Empty;

            if (_sourceCharacterInstance == null)
            {
                error = "Source preview instance is missing.";
                return false;
            }

            _sourcePreviewAnimator = _sourceCharacterInstance.GetComponent<Animator>();
            if (_sourcePreviewAnimator == null)
            {
                _sourcePreviewAnimator = _sourceCharacterInstance.AddComponent<Animator>();
            }

            Animator[] animators = _sourceCharacterInstance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator == null || animator == _sourcePreviewAnimator)
                {
                    continue;
                }

                animator.enabled = false;
            }

            Avatar sourceAvatar = GetSourceAvatar();
            _sourcePreviewAnimator.avatar = sourceAvatar;
            _sourcePreviewAnimator.runtimeAnimatorController = null;
            _sourcePreviewAnimator.applyRootMotion = true;
            _sourcePreviewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return true;
        }
        
        private void DestroyCharacterInstances()
        {
            if (_itemInstance != null)
            {
                Object.DestroyImmediate(_itemInstance);
            }

            if (_itemAttachmentInstance != null)
            {
                Object.DestroyImmediate(_itemAttachmentInstance);
            }

            if (_sourceCharacterInstance != null)
            {
                Object.DestroyImmediate(_sourceCharacterInstance);
            }

            if (_targetCharacterInstance != null)
            {
                Object.DestroyImmediate(_targetCharacterInstance);
            }

            _itemInstance = null;
            _itemAttachmentInstance = null;
            _sourceCharacterInstance = null;
            _targetCharacterInstance = null;
            _sourcePreviewAnimator = null;
            _hasPreviewRootMotionReferencePose = false;
            _hasPreviewRootMotionDelta = false;
        }

        private void ReleasePreviewCharacters()
        {
            DestroyTemporaryRigComponents();
            DestroyCharacterInstances();
            ClosePreviewScene();
            _sourcePreviewModelAsset = null;
            _targetPreviewModelAsset = null;
            _itemPreviewModelAsset = null;
            _itemPreviewBone = default;
            _itemPreviewRotationOffset = Vector3.zero;
        }

        private bool PreparePreviewCharactersFromProfile(out string error)
        {
            error = string.Empty;

            SyncCharactersFromProfile();
            if (_sourceCharacter == null || _targetCharacter == null)
            {
                error = "Source/Target models are missing on the profile.";
                return false;
            }

            ReleasePreviewCharacters();

            _sourceCharacterInstance = CreatePreviewInstance(_sourceCharacter, true);
            if (_sourceCharacterInstance == null)
            {
                error = "Failed to create source preview instance.";
                return false;
            }

            _targetCharacterInstance = CreatePreviewInstance(_targetCharacter, true);
            if (_targetCharacterInstance == null)
            {
                error = "Failed to create target preview instance.";
                ReleasePreviewCharacters();
                return false;
            }

            bool sourceAnimatorReady = true;
            if (!EnsureSourcePreviewAnimatorConfigured(out string sourceAnimatorError))
            {
                sourceAnimatorReady = false;
                Debug.LogWarning($"Retarget preview source Animator setup failed: {sourceAnimatorError}");
            }

            bool sourceRigReady = true;
            if (!TryBuildHierarchyRigComponent(_sourceCharacterInstance, out _sourceRigComponent,
                    out _sourceRigComponentTemporary, out string sourceBuildError))
            {
                sourceRigReady = false;
                Debug.LogWarning($"Retarget preview source rig setup failed: {sourceBuildError}");
            }

            bool targetRigReady = true;
            if (!TryBuildHierarchyRigComponent(_targetCharacterInstance, out _targetRigComponent,
                    out _targetRigComponentTemporary, out string targetBuildError))
            {
                targetRigReady = false;
                Debug.LogWarning($"Retarget preview target rig setup failed: {targetBuildError}");
            }

            _sourcePreviewModelAsset = _sourceCharacter;
            _targetPreviewModelAsset = _targetCharacter;
            _itemPreviewModelAsset = _itemModel;
            _itemPreviewBone = retargetProfile != null ? retargetProfile.clipItemBone : default;
            _itemPreviewRotationOffset = retargetProfile != null ? retargetProfile.clipItemRotationOffset : Vector3.zero;
            RefreshPreviewEditors();

            var previewWarnings = new List<string>();
            if (!sourceAnimatorReady)
            {
                previewWarnings.Add("Source Animator setup failed. Root motion preview will be disabled.");
            }

            if (!sourceRigReady || !targetRigReady)
            {
                previewWarnings.Add(
                    "Preview is available, but rig initialization failed. Check model rig setup before retargeting.");
            }

            string itemMessage;
            if (!TryPrepareItemPreview(out itemMessage) && !string.IsNullOrEmpty(itemMessage))
            {
                previewWarnings.Add(itemMessage);
            }
            else if (!string.IsNullOrEmpty(itemMessage))
            {
                previewWarnings.Add(itemMessage);
            }

            ApplyProfileReferencePoses();
            error = string.Join(" ", previewWarnings.Where(message => !string.IsNullOrEmpty(message)));
            return true;
        }

        private bool PreviewInstancesNeedRefresh()
        {
            if (_sourceCharacter == null || _targetCharacter == null)
            {
                return false;
            }

            if (_sourceCharacterInstance == null || _targetCharacterInstance == null)
            {
                return true;
            }

            bool expectsItem = _itemModel != null;
            bool hasItem = _itemInstance != null;
            if (expectsItem != hasItem)
            {
                return true;
            }

            if (expectsItem &&
                (_itemPreviewModelAsset != _itemModel ||
                 !RigElementsEqual(_itemPreviewBone, retargetProfile != null ? retargetProfile.clipItemBone : default) ||
                 _itemPreviewRotationOffset !=
                 (retargetProfile != null ? retargetProfile.clipItemRotationOffset : Vector3.zero)))
            {
                return true;
            }

            return _sourcePreviewModelAsset != _sourceCharacter || _targetPreviewModelAsset != _targetCharacter;
        }

        private bool TryPrepareItemPreview(out string message)
        {
            message = string.Empty;

            if (_itemModel == null)
            {
                return true;
            }

            if (_targetCharacterInstance == null || _targetRigComponent == null)
            {
                message = "Clip item preview could not be created because the target rig is not ready.";
                return false;
            }

            _itemInstance = CreatePreviewInstance(_itemModel, false);
            if (_itemInstance == null)
            {
                message = "Failed to create clip item preview instance.";
                return false;
            }

            _itemAttachmentInstance = new GameObject("RetargetPreviewClipItemAttachment");
            _itemAttachmentInstance.hideFlags = HideFlags.HideAndDontSave;
            _itemAttachmentInstance.layer = PreviewLayer;
            SceneManager.MoveGameObjectToScene(_itemAttachmentInstance, _previewScene);

            Transform attachTransform = ResolveItemAttachTransform();
            if (attachTransform == null)
            {
                attachTransform = _targetCharacterInstance.transform;
                if (HasRigElement(retargetProfile.clipItemBone))
                {
                    message = $"Failed to find item attach bone `{retargetProfile.clipItemBone.name}`. Attached item to target root.";
                }
                else
                {
                    message = "Clip item attach bone is not set. Attached item to target root.";
                }
            }

            Vector3 itemScale = _itemInstance.transform.localScale;
            _itemAttachmentInstance.transform.SetParent(attachTransform, false);
            _itemAttachmentInstance.transform.localPosition = Vector3.zero;
            _itemAttachmentInstance.transform.localRotation =
                Quaternion.Euler(retargetProfile != null ? retargetProfile.clipItemRotationOffset : Vector3.zero);
            _itemAttachmentInstance.transform.localScale = Vector3.one;

            _itemInstance.transform.SetParent(_itemAttachmentInstance.transform, false);
            _itemInstance.transform.localPosition = Vector3.zero;
            _itemInstance.transform.localRotation = Quaternion.identity;
            _itemInstance.transform.localScale = itemScale;
            return true;
        }

        private Transform ResolveItemAttachTransform()
        {
            if (_targetRigComponent == null || retargetProfile == null || !HasRigElement(retargetProfile.clipItemBone))
            {
                return null;
            }

            Transform[] hierarchy = _targetRigComponent.GetHierarchy();
            KRigElement attachBone = retargetProfile.clipItemBone;
            if (hierarchy != null && attachBone.index >= 0 && attachBone.index < hierarchy.Length)
            {
                return hierarchy[attachBone.index];
            }

            return !string.IsNullOrEmpty(attachBone.name) ? _targetRigComponent.GetRigTransform(attachBone.name) : null;
        }

        private static bool HasRigElement(KRigElement element)
        {
            return element.index >= 0 || !string.IsNullOrEmpty(element.name);
        }

        private static bool RigElementsEqual(KRigElement left, KRigElement right)
        {
            return left.index == right.index &&
                   left.depth == right.depth &&
                   left.name == right.name;
        }

        private bool EnsureRigComponentsReady(out string error)
        {
            error = string.Empty;

            if (_sourceCharacterInstance == null || _targetCharacterInstance == null)
            {
                error = "Preview models are not ready.";
                return false;
            }

            if (_sourceRigComponent == null)
            {
                if (!TryBuildHierarchyRigComponent(_sourceCharacterInstance, out _sourceRigComponent,
                        out _sourceRigComponentTemporary, out string sourceBuildError))
                {
                    error = $"Failed to initialize source rig: {sourceBuildError}";
                    return false;
                }
            }

            if (_targetRigComponent == null)
            {
                if (!TryBuildHierarchyRigComponent(_targetCharacterInstance, out _targetRigComponent,
                        out _targetRigComponentTemporary, out string targetBuildError))
                {
                    error = $"Failed to initialize target rig: {targetBuildError}";
                    return false;
                }
            }

            return true;
        }
        
        public void RetargetAtTime(AnimationClip clip, AnimationClip itemClip, float time)
        {
            RetargetAtTime(clip, itemClip, time, KTransform.Identity);
        }

        private void RetargetAtTime(AnimationClip clip, AnimationClip itemClip, float time, KTransform rootMotionBase)
        {
            if (!IsInitialized || RetargetComponent == null || _sourceCharacterInstance == null || clip == null)
            {
                return;
            }
            
            clip.SampleAnimation(_sourceCharacterInstance, time);
            CachePreviewRootMotionDelta();
            RestorePreviewRootMotionReferencePose();

            if (itemClip != null && _itemInstance != null)
            {
                itemClip.SampleAnimation(_itemInstance, time);
            }

            RetargetComponent.RetargetTransforms(time);
            ApplyPreviewRootMotion(rootMotionBase);
        }

        private static KTransform CombineRootMotion(KTransform baseTransform, KTransform localTransform)
        {
            Quaternion baseRotation = NormalizeQuaternion(baseTransform.rotation, Quaternion.identity);
            Quaternion localRotation = NormalizeQuaternion(localTransform.rotation, Quaternion.identity);
            return new KTransform(
                baseTransform.position + baseRotation * localTransform.position,
                NormalizeQuaternion(baseRotation * localRotation, baseRotation),
                Vector3.one);
        }

        private static KTransform GetLoopCarryRootMotion(KTransform startTransform, KTransform endTransform)
        {
            Quaternion startRotation = NormalizeQuaternion(startTransform.rotation, Quaternion.identity);
            Quaternion endRotation = NormalizeQuaternion(endTransform.rotation, Quaternion.identity);
            Quaternion carryRotation = NormalizeQuaternion(endRotation * Quaternion.Inverse(startRotation),
                Quaternion.identity);
            Vector3 carryPosition = endTransform.position - carryRotation * startTransform.position;
            return new KTransform(carryPosition, carryRotation, Vector3.one);
        }

        private static void RemoveRootTransformCurves(AnimationClip clip)
        {
            if (clip == null)
            {
                return;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform) || !string.IsNullOrEmpty(binding.path))
                {
                    continue;
                }

                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
        }

        private static void ExpandClipToLoopCount(AnimationClip clip, int loopCount, bool accumulateRootMotion,
            bool hasRootMotionTransformPath, string rootMotionTransformPath)
        {
            if (clip == null || loopCount <= DefaultLoopCount)
            {
                return;
            }

            float loopDuration = clip.length;
            if (loopDuration <= CurveTimeEpsilon)
            {
                return;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings == null || bindings.Length == 0)
            {
                return;
            }

            var sourceCurves = new Dictionary<string, CurveRecord>(bindings.Length, StringComparer.Ordinal);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                sourceCurves[GetBindingKey(binding)] = new CurveRecord
                {
                    binding = binding,
                    curve = curve
                };
            }

            var processedBindings = new HashSet<string>(StringComparer.Ordinal);
            if (accumulateRootMotion && TryCollectRootMotionCurves(sourceCurves,
                    out EditorCurveBinding[] rootPositionBindings,
                    out AnimationCurve[] rootPositionCurves, out EditorCurveBinding[] rootRotationBindings,
                    out AnimationCurve[] rootRotationCurves))
            {
                ApplyLoopedRootMotionCurves(clip, rootPositionBindings, rootPositionCurves, rootRotationBindings,
                    rootRotationCurves, loopDuration, loopCount, processedBindings);
            }

            if (accumulateRootMotion && hasRootMotionTransformPath &&
                TryCollectRootTransformCurves(sourceCurves, rootMotionTransformPath,
                    out EditorCurveBinding[] rootTransformPositionBindings,
                    out AnimationCurve[] rootTransformPositionCurves,
                    out EditorCurveBinding[] rootTransformRotationBindings,
                    out AnimationCurve[] rootTransformRotationCurves))
            {
                ApplyLoopedRootMotionCurves(clip, rootTransformPositionBindings, rootTransformPositionCurves,
                    rootTransformRotationBindings, rootTransformRotationCurves, loopDuration, loopCount,
                    processedBindings);
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                string bindingKey = GetBindingKey(binding);
                if (processedBindings.Contains(bindingKey) || !sourceCurves.TryGetValue(bindingKey, out CurveRecord record))
                {
                    continue;
                }

                AnimationCurve expandedCurve = CreateRepeatedCurveSimple(record.curve, loopDuration, loopCount);
                AnimationUtility.SetEditorCurve(clip, binding, expandedCurve);
                processedBindings.Add(bindingKey);
            }
        }

        private static bool TryCollectRootMotionCurves(IReadOnlyDictionary<string, CurveRecord> sourceCurves,
            out EditorCurveBinding[] rootPositionBindings, out AnimationCurve[] rootPositionCurves,
            out EditorCurveBinding[] rootRotationBindings, out AnimationCurve[] rootRotationCurves)
        {
            rootPositionBindings = null;
            rootPositionCurves = null;
            rootRotationBindings = null;
            rootRotationCurves = null;

            if (sourceCurves == null || sourceCurves.Count == 0)
            {
                return false;
            }

            if (TryGetCurveGroup(sourceCurves, typeof(Animator), string.Empty, AnimatorRootPositionPropertyNames,
                    out EditorCurveBinding[] foundPositionBindings, out AnimationCurve[] foundPositionCurves))
            {
                rootPositionBindings = foundPositionBindings;
                rootPositionCurves = foundPositionCurves;
            }

            if (TryGetCurveGroup(sourceCurves, typeof(Animator), string.Empty, AnimatorRootRotationPropertyNames,
                    out EditorCurveBinding[] foundRotationBindings, out AnimationCurve[] foundRotationCurves))
            {
                rootRotationBindings = foundRotationBindings;
                rootRotationCurves = foundRotationCurves;
            }

            return rootPositionCurves != null || rootRotationCurves != null;
        }

        private static bool TryCollectRootTransformCurves(IReadOnlyDictionary<string, CurveRecord> sourceCurves,
            string rootMotionTransformPath, out EditorCurveBinding[] rootPositionBindings,
            out AnimationCurve[] rootPositionCurves, out EditorCurveBinding[] rootRotationBindings,
            out AnimationCurve[] rootRotationCurves)
        {
            rootPositionBindings = null;
            rootPositionCurves = null;
            rootRotationBindings = null;
            rootRotationCurves = null;

            if (sourceCurves == null || sourceCurves.Count == 0)
            {
                return false;
            }

            if (TryGetCurveGroup(sourceCurves, typeof(Transform), rootMotionTransformPath,
                    TransformLocalPositionPropertyNames, out EditorCurveBinding[] foundPositionBindings,
                    out AnimationCurve[] foundPositionCurves))
            {
                rootPositionBindings = foundPositionBindings;
                rootPositionCurves = foundPositionCurves;
            }

            if (TryGetCurveGroup(sourceCurves, typeof(Transform), rootMotionTransformPath,
                    TransformLocalRotationPropertyNames, out EditorCurveBinding[] foundRotationBindings,
                    out AnimationCurve[] foundRotationCurves))
            {
                rootRotationBindings = foundRotationBindings;
                rootRotationCurves = foundRotationCurves;
            }

            return rootPositionCurves != null || rootRotationCurves != null;
        }

        private static bool TryGetCurveGroup(IReadOnlyDictionary<string, CurveRecord> sourceCurves, Type bindingType,
            string path, string[] propertyNames, out EditorCurveBinding[] bindings, out AnimationCurve[] curves)
        {
            bindings = new EditorCurveBinding[propertyNames.Length];
            curves = new AnimationCurve[propertyNames.Length];

            for (int i = 0; i < propertyNames.Length; i++)
            {
                var binding = new EditorCurveBinding
                {
                    path = path,
                    type = bindingType,
                    propertyName = propertyNames[i]
                };

                if (!sourceCurves.TryGetValue(GetBindingKey(binding), out CurveRecord record) || record.curve == null)
                {
                    bindings = null;
                    curves = null;
                    return false;
                }

                bindings[i] = record.binding;
                curves[i] = record.curve;
            }

            return true;
        }

        private static void ApplyLoopedRootMotionCurves(AnimationClip clip, EditorCurveBinding[] positionBindings,
            AnimationCurve[] sourcePositionCurves, EditorCurveBinding[] rotationBindings,
            AnimationCurve[] sourceRotationCurves, float loopDuration, int loopCount, ISet<string> processedBindings)
        {
            CreateLoopedRootMotionCurves(sourcePositionCurves, sourceRotationCurves, loopDuration, loopCount,
                out AnimationCurve[] loopedPositionCurves, out AnimationCurve[] loopedRotationCurves);

            if (positionBindings != null && loopedPositionCurves != null)
            {
                for (int i = 0; i < positionBindings.Length; i++)
                {
                    AnimationUtility.SetEditorCurve(clip, positionBindings[i], loopedPositionCurves[i]);
                    processedBindings.Add(GetBindingKey(positionBindings[i]));
                }
            }

            if (rotationBindings != null && loopedRotationCurves != null)
            {
                for (int i = 0; i < rotationBindings.Length; i++)
                {
                    AnimationUtility.SetEditorCurve(clip, rotationBindings[i], loopedRotationCurves[i]);
                    processedBindings.Add(GetBindingKey(rotationBindings[i]));
                }
            }
        }

        private static void CreateLoopedRootMotionCurves(AnimationCurve[] sourcePositionCurves,
            AnimationCurve[] sourceRotationCurves, float loopDuration, int loopCount,
            out AnimationCurve[] loopedPositionCurves, out AnimationCurve[] loopedRotationCurves)
        {
            bool hasPosition = sourcePositionCurves != null && sourcePositionCurves.Length == 3;
            bool hasRotation = sourceRotationCurves != null && sourceRotationCurves.Length == 4;

            loopedPositionCurves = hasPosition ? CreateEmptyCurvesLike(sourcePositionCurves) : null;
            loopedRotationCurves = hasRotation ? CreateEmptyCurvesLike(sourceRotationCurves) : null;

            var allCurves = new List<AnimationCurve>();
            if (hasPosition)
            {
                allCurves.AddRange(sourcePositionCurves);
            }

            if (hasRotation)
            {
                allCurves.AddRange(sourceRotationCurves);
            }

            float[] keyTimes = CollectUniqueKeyTimes(allCurves.ToArray(), loopDuration);
            if (keyTimes.Length == 0)
            {
                return;
            }

            Vector3[] sourcePositions = null;
            if (hasPosition)
            {
                sourcePositions = new Vector3[keyTimes.Length];
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    sourcePositions[i] = EvaluateVector3(sourcePositionCurves, keyTimes[i]);
                }
            }

            Quaternion[] sourceRotations = null;
            Quaternion deltaRotation = Quaternion.identity;
            if (hasRotation)
            {
                sourceRotations = new Quaternion[keyTimes.Length];
                Quaternion previousSourceRotation = EvaluateQuaternion(sourceRotationCurves, keyTimes[0], Quaternion.identity);
                sourceRotations[0] = previousSourceRotation;

                for (int i = 1; i < keyTimes.Length; i++)
                {
                    Quaternion currentRotation = EvaluateQuaternion(sourceRotationCurves, keyTimes[i], previousSourceRotation);
                    if (Quaternion.Dot(previousSourceRotation, currentRotation) < 0f)
                    {
                        currentRotation = NegateQuaternion(currentRotation);
                    }

                    sourceRotations[i] = currentRotation;
                    previousSourceRotation = currentRotation;
                }

                deltaRotation = NormalizeQuaternion(sourceRotations[sourceRotations.Length - 1] *
                                                    Quaternion.Inverse(sourceRotations[0]), Quaternion.identity);
            }

            Vector3 deltaPosition = Vector3.zero;
            if (hasPosition)
            {
                deltaPosition = sourcePositions[sourcePositions.Length - 1] - sourcePositions[0];
                if (hasRotation)
                {
                    deltaPosition = sourcePositions[sourcePositions.Length - 1] - deltaRotation * sourcePositions[0];
                }
            }

            Vector3 accumulatedPosition = Vector3.zero;
            Quaternion accumulatedRotation = Quaternion.identity;
            Quaternion previousOutputRotation = Quaternion.identity;
            bool hasPreviousOutputRotation = false;

            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                float timeOffset = loopIndex * loopDuration;
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    float localTime = keyTimes[i];
                    if (loopIndex > 0 && IsLoopStartTime(localTime))
                    {
                        continue;
                    }

                    if (hasPosition)
                    {
                        Vector3 outputPosition = accumulatedPosition + sourcePositions[i];
                        if (hasRotation)
                        {
                            outputPosition = accumulatedPosition + accumulatedRotation * sourcePositions[i];
                        }

                        AddLinearKey(loopedPositionCurves[0], timeOffset + localTime, outputPosition.x);
                        AddLinearKey(loopedPositionCurves[1], timeOffset + localTime, outputPosition.y);
                        AddLinearKey(loopedPositionCurves[2], timeOffset + localTime, outputPosition.z);
                    }

                    if (hasRotation)
                    {
                        Quaternion outputRotation =
                            NormalizeQuaternion(accumulatedRotation * sourceRotations[i],
                                hasPreviousOutputRotation ? previousOutputRotation : sourceRotations[i]);
                        if (hasPreviousOutputRotation && Quaternion.Dot(previousOutputRotation, outputRotation) < 0f)
                        {
                            outputRotation = NegateQuaternion(outputRotation);
                        }

                        AddLinearKey(loopedRotationCurves[0], timeOffset + localTime, outputRotation.x);
                        AddLinearKey(loopedRotationCurves[1], timeOffset + localTime, outputRotation.y);
                        AddLinearKey(loopedRotationCurves[2], timeOffset + localTime, outputRotation.z);
                        AddLinearKey(loopedRotationCurves[3], timeOffset + localTime, outputRotation.w);

                        previousOutputRotation = outputRotation;
                        hasPreviousOutputRotation = true;
                    }
                }

                if (hasPosition)
                {
                    accumulatedPosition = hasRotation
                        ? accumulatedPosition + accumulatedRotation * deltaPosition
                        : accumulatedPosition + deltaPosition;
                }

                if (hasRotation)
                {
                    accumulatedRotation = NormalizeQuaternion(accumulatedRotation * deltaRotation, accumulatedRotation);
                }
            }
        }

        private static AnimationCurve[] CreateEmptyCurvesLike(AnimationCurve[] sourceCurves)
        {
            var result = new AnimationCurve[sourceCurves.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new AnimationCurve
                {
                    preWrapMode = sourceCurves[i].preWrapMode,
                    postWrapMode = sourceCurves[i].postWrapMode
                };
            }

            return result;
        }

        private static AnimationCurve CreateRepeatedCurveSimple(AnimationCurve sourceCurve, float loopDuration,
            int loopCount)
        {
            if (sourceCurve == null)
            {
                return null;
            }

            Keyframe[] sourceKeys = sourceCurve.keys;
            if (sourceKeys == null || sourceKeys.Length == 0)
            {
                return new AnimationCurve
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };
            }

            List<Keyframe> normalizedSourceKeys = NormalizeLoopSourceKeys(sourceKeys, loopDuration);
            if (normalizedSourceKeys.Count == 0)
            {
                return new AnimationCurve
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };
            }

            var repeatedKeys = new List<Keyframe>(normalizedSourceKeys.Count * loopCount);
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                float timeOffset = loopIndex * loopDuration;
                for (int i = 0; i < normalizedSourceKeys.Count; i++)
                {
                    Keyframe key = normalizedSourceKeys[i];
                    if (loopIndex > 0 && IsLoopStartTime(key.time))
                    {
                        continue;
                    }

                    key.time = timeOffset + key.time;
                    repeatedKeys.Add(key);
                }
            }

            return new AnimationCurve(repeatedKeys.ToArray())
            {
                preWrapMode = sourceCurve.preWrapMode,
                postWrapMode = sourceCurve.postWrapMode
            };
        }

        private static List<Keyframe> NormalizeLoopSourceKeys(Keyframe[] sourceKeys, float loopDuration)
        {
            var normalizedKeys = new List<Keyframe>(sourceKeys.Length);
            var sourceTimes = new List<float>(sourceKeys.Length);
            for (int i = 0; i < sourceKeys.Length; i++)
            {
                Keyframe key = sourceKeys[i];
                float sourceTime = key.time;
                float normalizedTime = NormalizeLoopLocalTime(sourceTime, loopDuration);
                if (normalizedTime < -CurveTimeEpsilon || normalizedTime > loopDuration + CurveTimeEpsilon)
                {
                    continue;
                }

                key.time = normalizedTime;
                if (TryMergeNormalizedLoopKey(normalizedKeys, sourceTimes, key, sourceTime))
                {
                    continue;
                }

                normalizedKeys.Add(key);
                sourceTimes.Add(sourceTime);
            }

            return normalizedKeys;
        }

        private static bool TryMergeNormalizedLoopKey(IList<Keyframe> normalizedKeys, IList<float> sourceTimes,
            Keyframe candidateKey, float candidateSourceTime)
        {
            int lastIndex = normalizedKeys.Count - 1;
            if (lastIndex < 0 || Mathf.Abs(normalizedKeys[lastIndex].time - candidateKey.time) > CurveTimeEpsilon)
            {
                return false;
            }

            float canonicalTime = candidateKey.time;
            float previousDistance = Mathf.Abs(sourceTimes[lastIndex] - canonicalTime);
            float candidateDistance = Mathf.Abs(candidateSourceTime - canonicalTime);
            if (candidateDistance <= previousDistance)
            {
                normalizedKeys[lastIndex] = candidateKey;
                sourceTimes[lastIndex] = candidateSourceTime;
            }

            return true;
        }


        private static float[] CollectUniqueKeyTimes(AnimationCurve[] curves, float loopDuration)
        {
            var times = new List<float> { 0f, loopDuration };
            for (int i = 0; i < curves.Length; i++)
            {
                AnimationCurve curve = curves[i];
                if (curve == null)
                {
                    continue;
                }

                Keyframe[] keys = curve.keys;
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    float localTime = NormalizeLoopLocalTime(keys[keyIndex].time, loopDuration);
                    if (localTime < -CurveTimeEpsilon || localTime > loopDuration + CurveTimeEpsilon)
                    {
                        continue;
                    }

                    times.Add(localTime);
                }
            }

            times.Sort();
            var uniqueTimes = new List<float>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                if (uniqueTimes.Count > 0 &&
                    Mathf.Abs(uniqueTimes[uniqueTimes.Count - 1] - times[i]) <= CurveTimeEpsilon)
                {
                    continue;
                }

                uniqueTimes.Add(times[i]);
            }

            return uniqueTimes.ToArray();
        }

        private static Vector3 EvaluateVector3(AnimationCurve[] curves, float time)
        {
            return new Vector3(
                curves[0] != null ? curves[0].Evaluate(time) : 0f,
                curves[1] != null ? curves[1].Evaluate(time) : 0f,
                curves[2] != null ? curves[2].Evaluate(time) : 0f);
        }

        private static Quaternion EvaluateQuaternion(AnimationCurve[] curves, float time, Quaternion fallback)
        {
            Quaternion rotation = new Quaternion(
                curves[0] != null ? curves[0].Evaluate(time) : fallback.x,
                curves[1] != null ? curves[1].Evaluate(time) : fallback.y,
                curves[2] != null ? curves[2].Evaluate(time) : fallback.z,
                curves[3] != null ? curves[3].Evaluate(time) : fallback.w);
            return NormalizeQuaternion(rotation, fallback);
        }

        private static Quaternion NormalizeQuaternion(Quaternion rotation, Quaternion fallback)
        {
            if (float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) ||
                float.IsNaN(rotation.w) || float.IsInfinity(rotation.x) || float.IsInfinity(rotation.y) ||
                float.IsInfinity(rotation.z) || float.IsInfinity(rotation.w))
            {
                return fallback;
            }

            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y +
                                         rotation.z * rotation.z + rotation.w * rotation.w);
            if (magnitude <= CurveTimeEpsilon)
            {
                return fallback;
            }

            float inverseMagnitude = 1f / magnitude;
            return new Quaternion(rotation.x * inverseMagnitude, rotation.y * inverseMagnitude,
                rotation.z * inverseMagnitude, rotation.w * inverseMagnitude);
        }

        private static Quaternion NegateQuaternion(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
        }

        private static void AddLinearKey(AnimationCurve curve, float time, float value)
        {
            int index = curve.length - 1;
            if (index >= 0 && Mathf.Abs(curve.keys[index].time - time) <= CurveTimeEpsilon)
            {
                Keyframe key = curve.keys[index];
                key.value = value;
                curve.MoveKey(index, key);
            }
            else
            {
                index = curve.AddKey(time, value);
            }

            if (index < 0 || index >= curve.length)
            {
                return;
            }

            AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
        }

        private static string GetBindingKey(EditorCurveBinding binding)
        {
            string typeName = binding.type != null ? binding.type.FullName : string.Empty;
            return $"{binding.path}|{typeName}|{binding.propertyName}";
        }

        private static bool IsLoopStartTime(float time)
        {
            return Mathf.Abs(time) <= CurveTimeEpsilon;
        }

        private static float NormalizeLoopLocalTime(float time, float loopDuration)
        {
            if (Mathf.Abs(time) <= CurveTimeEpsilon)
            {
                return 0f;
            }

            return Mathf.Abs(time - loopDuration) <= CurveTimeEpsilon ? loopDuration : time;
        }

        private static void NormalizeClipRange(AnimationClip clip, float startTime, float endTime,
            out float normalizedStartTime, out float normalizedEndTime)
        {
            float clipLength = clip != null ? Mathf.Max(0f, clip.length) : 0f;
            normalizedStartTime = Mathf.Clamp(startTime, 0f, clipLength);
            normalizedEndTime = Mathf.Clamp(endTime, normalizedStartTime, clipLength);
        }

        private static float GetRangeDuration(float startTime, float endTime)
        {
            return Mathf.Max(0f, endTime - startTime);
        }

        private static bool IsTrimmedRange(AnimationClip clip, float startTime, float endTime)
        {
            NormalizeClipRange(clip, startTime, endTime, out float normalizedStartTime, out float normalizedEndTime);
            float clipLength = clip != null ? Mathf.Max(0f, clip.length) : 0f;
            return normalizedStartTime > CurveTimeEpsilon ||
                   Mathf.Abs(normalizedEndTime - clipLength) > CurveTimeEpsilon;
        }

        private static AnimationCurve CreateTrimmedCurve(AnimationCurve sourceCurve, float sourceStartTime,
            float sourceEndTime)
        {
            if (sourceCurve == null)
            {
                return null;
            }

            float duration = GetRangeDuration(sourceStartTime, sourceEndTime);
            if (sourceCurve.length == 0)
            {
                return new AnimationCurve();
            }

            if (duration <= CurveTimeEpsilon)
            {
                return new AnimationCurve(new Keyframe(0f, sourceCurve.Evaluate(sourceStartTime)))
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };
            }

            Keyframe[] sourceKeys = sourceCurve.keys;
            if (sourceStartTime <= CurveTimeEpsilon &&
                Mathf.Abs(sourceEndTime - sourceKeys[sourceKeys.Length - 1].time) <= CurveTimeEpsilon)
            {
                return new AnimationCurve(sourceKeys)
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };
            }

            var trimmedKeys = new List<Keyframe>(sourceCurve.length + 2);
            AddTrimBoundaryKey(trimmedKeys, sourceCurve, sourceStartTime, 0f);

            for (int i = 0; i < sourceKeys.Length; i++)
            {
                Keyframe key = sourceKeys[i];
                if (key.time <= sourceStartTime + CurveTimeEpsilon || key.time >= sourceEndTime - CurveTimeEpsilon)
                {
                    continue;
                }

                key.time -= sourceStartTime;
                trimmedKeys.Add(key);
            }

            AddTrimBoundaryKey(trimmedKeys, sourceCurve, sourceEndTime, duration);
            return new AnimationCurve(trimmedKeys.ToArray())
            {
                preWrapMode = sourceCurve.preWrapMode,
                postWrapMode = sourceCurve.postWrapMode
            };
        }

        private static void AddTrimBoundaryKey(ICollection<Keyframe> trimmedKeys, AnimationCurve sourceCurve,
            float sourceTime, float outputTime)
        {
            if (TryGetBoundaryKey(sourceCurve, sourceTime, out Keyframe key))
            {
                key.time = outputTime;
                trimmedKeys.Add(key);
                return;
            }

            trimmedKeys.Add(new Keyframe(outputTime, sourceCurve.Evaluate(sourceTime)));
        }

        private static bool TryGetBoundaryKey(AnimationCurve sourceCurve, float sourceTime, out Keyframe key)
        {
            Keyframe[] sourceKeys = sourceCurve.keys;
            for (int i = 0; i < sourceKeys.Length; i++)
            {
                if (Mathf.Abs(sourceKeys[i].time - sourceTime) > CurveTimeEpsilon)
                {
                    continue;
                }

                key = sourceKeys[i];
                return true;
            }

            key = default;
            return false;
        }

        private void ApplyCopiedClipSettings(AnimationClip sourceClip, AnimationClip targetClip, float sourceStartTime,
            float sourceEndTime)
        {
            if (!_copyClipSettings || sourceClip == null || targetClip == null)
            {
                return;
            }

            NormalizeClipRange(sourceClip, sourceStartTime, sourceEndTime,
                out float normalizedStartTime, out float normalizedEndTime);
            bool trimmedRange = IsTrimmedRange(sourceClip, normalizedStartTime,
                normalizedEndTime);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
            settings.startTime = 0f;
            settings.stopTime = targetClip.length;
            if (LoopCount > DefaultLoopCount || trimmedRange)
            {
                settings.loopTime = false;
            }

            AnimationUtility.SetAnimationClipSettings(targetClip, settings);
            AnimationUtility.SetAnimationEvents(targetClip,
                CreateLoopedAnimationEvents(AnimationUtility.GetAnimationEvents(sourceClip), normalizedStartTime,
                    normalizedEndTime, LoopCount));
        }

        private static AnimationEvent[] CreateLoopedAnimationEvents(AnimationEvent[] sourceEvents, float sourceStartTime,
            float sourceEndTime, int loopCount)
        {
            float loopDuration = Mathf.Max(0f, sourceEndTime - sourceStartTime);
            if (sourceEvents == null || sourceEvents.Length == 0)
            {
                return Array.Empty<AnimationEvent>();
            }

            int repeatCount = Mathf.Max(DefaultLoopCount, loopCount);
            var repeatedEvents = new List<AnimationEvent>(sourceEvents.Length * repeatCount);
            for (int loopIndex = 0; loopIndex < repeatCount; loopIndex++)
            {
                float timeOffset = loopIndex * loopDuration;
                for (int i = 0; i < sourceEvents.Length; i++)
                {
                    AnimationEvent sourceEvent = sourceEvents[i];
                    if (sourceEvent == null)
                    {
                        continue;
                    }

                    float sourceEventTime = sourceEvent.time;
                    if (sourceEventTime < sourceStartTime - CurveTimeEpsilon ||
                        sourceEventTime > sourceEndTime + CurveTimeEpsilon)
                    {
                        continue;
                    }

                    repeatedEvents.Add(new AnimationEvent
                    {
                        functionName = sourceEvent.functionName,
                        stringParameter = sourceEvent.stringParameter,
                        floatParameter = sourceEvent.floatParameter,
                        intParameter = sourceEvent.intParameter,
                        objectReferenceParameter = sourceEvent.objectReferenceParameter,
                        messageOptions = sourceEvent.messageOptions,
                        time = timeOffset + Mathf.Clamp(sourceEventTime - sourceStartTime, 0f, loopDuration)
                    });
                }
            }

            repeatedEvents.Sort((left, right) => left.time.CompareTo(right.time));
            return repeatedEvents.ToArray();
        }

        private static void CopyTrimmedSupplementalAnimatorCurves(AnimationClip sourceClip, AnimationClip targetClip,
            float sourceStartTime, float sourceEndTime)
        {
            if (sourceClip == null || targetClip == null ||
                !IsTrimmedRange(sourceClip, sourceStartTime, sourceEndTime))
            {
                return;
            }

            NormalizeClipRange(sourceClip, sourceStartTime, sourceEndTime,
                out float normalizedStartTime, out float normalizedEndTime);

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path) ||
                    !IsSupportedSupplementalAnimatorProperty(binding.propertyName))
                {
                    continue;
                }

                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                if (sourceCurve == null)
                {
                    continue;
                }

                AnimationCurve copiedCurve = CreateTrimmedCurve(sourceCurve,
                    normalizedStartTime, normalizedEndTime);
                AnimationUtility.SetEditorCurve(targetClip, binding, copiedCurve);
            }
        }

        private static void RepeatSupplementalAnimatorCurves(AnimationClip clip, float loopDuration, int loopCount)
        {
            if (clip == null || loopCount <= DefaultLoopCount || loopDuration <= CurveTimeEpsilon)
            {
                return;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path) ||
                    !IsSupportedSupplementalAnimatorProperty(binding.propertyName))
                {
                    continue;
                }

                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(clip, binding);
                if (sourceCurve == null)
                {
                    continue;
                }

                AnimationCurve repeatedCurve = CreateRepeatedCurveSimple(sourceCurve, loopDuration, loopCount);
                AnimationUtility.SetEditorCurve(clip, binding, repeatedCurve);
            }
        }

        private static bool IsSupportedSupplementalAnimatorProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || IsRootMotionProperty(propertyName) ||
                IsMuscleProperty(propertyName))
            {
                return false;
            }

            return IsHumanoidIkGoalProperty(propertyName) || IsHumanoidLookAtProperty(propertyName);
        }

        private static bool IsHumanoidIkGoalProperty(string propertyName)
        {
            return propertyName.StartsWith("LeftHandQ.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("LeftHandT.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("RightHandQ.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("RightHandT.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("LeftFootQ.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("LeftFootT.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("RightFootQ.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("RightFootT.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHumanoidLookAtProperty(string propertyName)
        {
            return propertyName.StartsWith("LookAt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRootMotionProperty(string propertyName)
        {
            return propertyName.StartsWith("RootT.", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.StartsWith("RootQ.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMuscleProperty(string propertyName)
        {
            if (HumanoidMusclePropertyNames.Contains(propertyName))
            {
                return true;
            }

            string canonical = CanonicalizeDottedHandMuscleName(propertyName);
            return !string.IsNullOrEmpty(canonical) && HumanoidMusclePropertyNames.Contains(canonical);
        }

        private static string CanonicalizeDottedHandMuscleName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            if (propertyName.StartsWith("LeftHand.", StringComparison.Ordinal))
            {
                return "Left " + propertyName.Substring("LeftHand.".Length).Replace('.', ' ');
            }

            if (propertyName.StartsWith("RightHand.", StringComparison.Ordinal))
            {
                return "Right " + propertyName.Substring("RightHand.".Length).Replace('.', ' ');
            }

            return null;
        }

        private static HashSet<string> BuildHumanoidMusclePropertyNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                names.Add(HumanTrait.MuscleName[i]);
            }

            return names;
        }

        public void InitializeBaker()
        {
            TryInitializeBaker(out _);
        }

        public void SyncFrameRateFromSource(AnimationClip sourceAnimation)
        {
            if (!_useSourceFrameRateByDefault || sourceAnimation == null)
            {
                return;
            }

            if (sourceAnimation.frameRate > 0f)
            {
                _frameRate = sourceAnimation.frameRate;
            }
        }

        public void PromptSaveFolderSelection()
        {
            if (retargetProfile == null)
            {
                return;
            }

            string path = EditorUtility.OpenFolderPanel("Select Directory", "Assets", "");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (TryConvertToAssetFolderPath(path, out string assetFolderPath))
            {
                SetProfileSavePath(assetFolderPath);
            }
            else
            {
                Debug.LogWarning("Save folder must be inside the project's Assets folder.");
            }
        }

        public bool TryGetValidationMessage(out string message, out MessageType messageType)
        {
            message = string.Empty;
            messageType = MessageType.Info;

            if (retargetProfile == null)
            {
                message = "Select a Retarget Profile.";
                messageType = MessageType.Warning;
                return false;
            }

            SyncCharactersFromProfile();

            if (string.IsNullOrEmpty(SavePath))
            {
                message = "Select a save folder inside Assets.";
                messageType = MessageType.Warning;
                return false;
            }

            if (_sourceCharacter == null)
            {
                message = "Assign a Source model on the Retarget Profile.";
                messageType = MessageType.Warning;
                return false;
            }

            if (_targetCharacter == null)
            {
                message = "Assign a Target model on the Retarget Profile.";
                messageType = MessageType.Warning;
                return false;
            }

            EnsureRigTypeSelection();

            if(!RetargetProBakerRegistry.TryCreate(_selectedBakerId, out _, out string bakerError))
            {
                message = bakerError;
                messageType = MessageType.Warning;
                return false;
            }

            if (IsHumanoidRigSelected())
            {
                Avatar targetAvatar = GetTargetAvatar(true);
                if (targetAvatar == null || !targetAvatar.isValid || !targetAvatar.isHuman ||
                    !EditorUtility.IsPersistent(targetAvatar))
                {
                    message = "Humanoid baking requires a valid Humanoid avatar on the Target model.";
                    messageType = MessageType.Warning;
                    return false;
                }
            }

            return true;
        }

        private static bool TryConvertToAssetFolderPath(string absolutePath, out string assetFolderPath)
        {
            assetFolderPath = string.Empty;

            if (string.IsNullOrEmpty(absolutePath))
            {
                return false;
            }

            string normalizedPath = absolutePath.Replace('\\', '/');
            string normalizedAssetsPath = Application.dataPath.Replace('\\', '/');
            bool isAssetsRoot = string.Equals(normalizedPath, normalizedAssetsPath, StringComparison.OrdinalIgnoreCase);
            bool isAssetsChild = normalizedPath.StartsWith($"{normalizedAssetsPath}/", StringComparison.OrdinalIgnoreCase);
            if (!isAssetsRoot && !isAssetsChild)
            {
                return false;
            }

            assetFolderPath = $"Assets{normalizedPath.Substring(normalizedAssetsPath.Length)}";
            return true;
        }

        private void SetProfileSavePath(string path)
        {
            if (retargetProfile == null 
                || string.Equals(retargetProfile.saveFolderPath, path, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(retargetProfile, "Change Save Folder");
            retargetProfile.saveFolderPath = path;
            EditorUtility.SetDirty(retargetProfile);
        }

        public bool TryInitializeBaker(out string error)
        {
            error = string.Empty;

            if (IsInitialized) return true;

            if (retargetProfile == null)
            {
                error = "Select a Retarget Profile before initializing.";
                return false;
            }

            if (!RetargetProfileModelRigUtility.TryComposeProfileRigs(retargetProfile, false, out string composeMessage))
            {
                error = $"Failed to compose profile rigs: {composeMessage}";
                return false;
            }

            SyncCharactersFromProfile();
            RefreshPreviewEditors();

            if (_sourceCharacter == null || _targetCharacter == null)
            {
                error = "Assign Source and Target characters on the Retarget Profile asset.";
                return false;
            }

            try
            {
                if (PreviewInstancesNeedRefresh())
                {
                    if (!PreparePreviewCharactersFromProfile(out string prepareError))
                    {
                        error = string.IsNullOrEmpty(prepareError)
                            ? "Preview models are not ready. Re-open or reassign the Retarget Profile."
                            : prepareError;
                        return false;
                    }
                }

                if (!EnsureRigComponentsReady(out string rigError))
                {
                    error = rigError;
                    return false;
                }

                RetargetComponent = new RetargetProComponent();
                RetargetComponent.Initialize(_sourceCharacterInstance, _targetCharacterInstance, retargetProfile,
                    _sourceRigComponent, _targetRigComponent);

                _sourceRigComponent.CacheHierarchyPose();
                _targetRigComponent.CacheHierarchyPose();
                CachePreviewRootMotionReferencePose();

                IsInitialized = true;
                RefreshPreviewEditors();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Initialization failed: {ex.Message}";
                Debug.LogException(ex);
                CleanupBaker();
                return false;
            }
        }

        public bool RefreshAfterProfileSettingsChange(out string error)
        {
            error = string.Empty;

            GameObject previousTargetCharacter = _targetCharacter;
            SyncCharactersFromProfile();
            if (previousTargetCharacter != _targetCharacter)
            {
                SelectRigTypeFromTargetAvatar();
            }

            if (_sourceCharacter == null || _targetCharacter == null)
            {
                ReleasePreviewCharacters();
                RefreshPreviewEditors(true);
                return true;
            }

            if (IsInitialized)
            {
                UnInitializeBaker();
                return TryInitializeBaker(out error);
            }

            bool prepared = PreparePreviewCharactersFromProfile(out error);
            if (!prepared)
            {
                RefreshPreviewEditors(true);
            }

            return prepared;
        }

        public void UnInitializeBaker()
        {
            if (!IsInitialized) return;

            IsInitialized = false;

            RestorePreviewCharactersToCachedPose();

            if (RetargetComponent != null)
            {
                RetargetComponent.DestroyRetargetFeatures();
            }

            ApplyProfileReferencePoses();
            RetargetComponent = null;
        }

        private void RestorePreviewCharactersToCachedPose()
        {
            if (_sourceRigComponent != null)
            {
                _sourceRigComponent.ApplyHierarchyCachedPose();
            }

            if (_targetRigComponent != null)
            {
                _targetRigComponent.ApplyHierarchyCachedPose();
            }
        }

        private void SnapPreviewCharactersToDefaults()
        {
            RestorePreviewCharactersToCachedPose();
            ApplyProfileReferencePoses();
        }

        public AnimationClip BakeAnimation(AnimationClip animationToRetarget)
        {
            float sourceEndTime = animationToRetarget != null ? Mathf.Max(0f, animationToRetarget.length) : 0f;
            return BakeAnimation(animationToRetarget, 0f, sourceEndTime);
        }

        public AnimationClip BakeAnimation(AnimationClip animationToRetarget, float sourceStartTime, float sourceEndTime)
        {
            if (_sourceRigComponent == null || _targetRigComponent == null || retargetProfile == null ||
                animationToRetarget == null)
            {
                Debug.LogError("RetargetAnimBaker: Rig Component is NULL!");
                return null;
            }
            
            // Bake paths relative to the target character root (not preview anchors/scene roots).
            GameObject bakeRoot = _targetCharacterInstance != null
                ? _targetCharacterInstance
                : _targetRigComponent.gameObject;

            EnsureRigTypeSelection();

            bool directFbxOutput = OutputType == BakeOutputType.Fbx;
            IRetargetProBaker baker = null;
            if (!directFbxOutput && !RetargetProBakerRegistry.TryCreate(_selectedBakerId, out baker,
                    out string bakerError))
            {
                Debug.LogError($"RetargetAnimBaker: {bakerError}");
                return null;
            }

            SnapPreviewCharactersToDefaults();

            Quaternion originalRootRotation = bakeRoot.transform.rotation;
            bakeRoot.transform.rotation *= Quaternion.Euler(_rootRotationOffsetEuler);
            GenericAnimationBaker directFbxBaker = null;

            try
            {
                if (!directFbxOutput)
                {
                    baker.Initialize(_targetRigComponent, retargetProfile.rootMotionBone);
                }

                float sourceFrameRate = animationToRetarget.frameRate > 0f
                    ? animationToRetarget.frameRate
                    : DefaultBakeFrameRate;
                if (_useSourceFrameRateByDefault)
                {
                    _frameRate = sourceFrameRate;
                }

                float bakeFrameRate = Mathf.Max(1f, _frameRate);
                string bakedClipName = GetBakedClipName(animationToRetarget);
                AnimationClip clip = directFbxOutput
                    ? null
                    : new AnimationClip
                    {
                        name = bakedClipName,
                        frameRate = bakeFrameRate
                    };

                NormalizeClipRange(animationToRetarget, sourceStartTime, sourceEndTime,
                    out float normalizedStartTime, out float normalizedEndTime);
                float clipLength = GetRangeDuration(normalizedStartTime, normalizedEndTime);
                int repeatCount = Mathf.Max(DefaultLoopCount, LoopCount);
                float bakedClipLength = clipLength * repeatCount;
                float delta = 1f / bakeFrameRate;
                float playback = 0f;
                int totalSamples = Mathf.Max(1, Mathf.CeilToInt(bakedClipLength / delta) + 1);
                if (directFbxOutput)
                {
                    totalSamples = 1;
                    for (float sampleTime = 0f; sampleTime < bakedClipLength; sampleTime += delta)
                    {
                        totalSamples++;
                    }
                }

                int processedSamples = 0;
                int currentLoopIndex = 0;
                bool hasLoopCarryRootMotion = false;
                KTransform accumulatedRootMotion = KTransform.Identity;
                KTransform loopCarryRootMotion = KTransform.Identity;

                string fbxPath = string.Empty;
                bool humanoidOutput = IsHumanoidRigSelected();
                if (directFbxOutput)
                {
                    string fbxSavePath = SavePath;
                    if (!Directory.Exists(fbxSavePath))
                    {
                        Directory.CreateDirectory(fbxSavePath);
                    }

                    fbxPath = AssetDatabase.GenerateUniqueAssetPath($"{fbxSavePath}/{bakedClipName}.fbx");
                    directFbxBaker = new GenericAnimationBaker();
                    if (!directFbxBaker.TryInitializeDirectPoseRecorder(_targetRigComponent,
                            retargetProfile.rootMotionBone, fbxPath, bakeFrameRate, totalSamples,
                            out string initializeFbxError))
                    {
                        Debug.LogError($"RetargetAnimBaker: FBX export failed. {initializeFbxError}");
                        return null;
                    }
                }

                RetargetAtTime(animationToRetarget, null, normalizedStartTime, KTransform.Identity);
                if (_useRootMotion && _hasPreviewRootMotionDelta)
                {
                    KTransform startRootMotion = _rootMotionDelta;
                    RetargetAtTime(animationToRetarget, null, normalizedEndTime, KTransform.Identity);
                    if (_hasPreviewRootMotionDelta)
                    {
                        loopCarryRootMotion = GetLoopCarryRootMotion(startRootMotion, _rootMotionDelta);
                        hasLoopCarryRootMotion = true;
                    }

                    RetargetAtTime(animationToRetarget, null, normalizedStartTime, KTransform.Identity);
                }

                while (playback < bakedClipLength)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Retarget Pro",
                            $"Baking {animationToRetarget.name} ({processedSamples + 1}/{totalSamples})",
                            processedSamples / (float) totalSamples))
                    {
                        Debug.LogWarning("RetargetAnimBaker: Bake cancelled by user.");
                        return null;
                    }

                    int loopIndex = clipLength > CurveTimeEpsilon
                        ? Mathf.Min(repeatCount - 1, Mathf.FloorToInt(playback / clipLength))
                        : 0;

                    while (currentLoopIndex < loopIndex)
                    {
                        if (hasLoopCarryRootMotion)
                        {
                            accumulatedRootMotion = CombineRootMotion(accumulatedRootMotion, loopCarryRootMotion);
                        }

                        currentLoopIndex++;
                    }

                    float localPlayback = clipLength > CurveTimeEpsilon ? playback - loopIndex * clipLength : 0f;
                    RetargetAtTime(animationToRetarget, null, normalizedStartTime + localPlayback, accumulatedRootMotion);
                    if (directFbxOutput)
                    {
                        if (!directFbxBaker.CaptureDirectPose(playback, out string captureError))
                        {
                            Debug.LogError($"RetargetAnimBaker: FBX export failed. {captureError}");
                            return null;
                        }
                    }
                    else
                    {
                        baker.BakeAnimationFrame(playback);
                    }

                    playback += delta;
                    processedSamples++;
                }

                // Ensure exact last sample is always baked.
                EditorUtility.DisplayProgressBar("Retarget Pro", $"Baking {animationToRetarget.name} (final sample)",
                    1f);
                while (currentLoopIndex < repeatCount - 1)
                {
                    if (hasLoopCarryRootMotion)
                    {
                        accumulatedRootMotion = CombineRootMotion(accumulatedRootMotion, loopCarryRootMotion);
                    }

                    currentLoopIndex++;
                }

                RetargetAtTime(animationToRetarget, null, normalizedEndTime, accumulatedRootMotion);
                if (directFbxOutput)
                {
                    if (!directFbxBaker.CaptureDirectPose(bakedClipLength, out string captureError))
                    {
                        Debug.LogError($"RetargetAnimBaker: FBX export failed. {captureError}");
                        return null;
                    }

                    EditorUtility.DisplayProgressBar("Retarget Pro", $"Exporting {bakedClipName}.fbx", 0.96f);
                    if (!directFbxBaker.WriteDirectPose(out string writeError))
                    {
                        Debug.LogError($"RetargetAnimBaker: FBX export failed. {writeError}");
                        return null;
                    }

                    EditorUtility.DisplayProgressBar("Retarget Pro", "Configuring FBX import settings", 0.99f);
                    if (!TryConfigureFbxImportSettings(fbxPath, animationToRetarget, normalizedStartTime,
                            normalizedEndTime, humanoidOutput, bakedClipName, out string importError))
                    {
                        Debug.LogError($"RetargetAnimBaker: FBX export failed. {importError}");
                    }

                    return null;
                }
                else
                {
                    baker.BakeAnimationFrame(bakedClipLength);
                }

                baker.WriteToClip(clip);
                if (_useRootMotion)
                {
                    baker.WriteRootMotion(animationToRetarget, clip);
                    CopyTrimmedSupplementalAnimatorCurves(animationToRetarget, clip, normalizedStartTime,
                        normalizedEndTime);
                    RepeatSupplementalAnimatorCurves(clip, clipLength, repeatCount);
                }
                clip.EnsureQuaternionContinuity();

                ApplyCopiedClipSettings(animationToRetarget, clip, normalizedStartTime, normalizedEndTime);

                string savePath = SavePath;
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                if (humanoidOutput)
                {
                    if (!TryCreateHumanoidAnimationClip(clip, animationToRetarget, normalizedStartTime,
                            normalizedEndTime, out AnimationClip humanoidClip, out string humanoidError))
                    {
                        Debug.LogError($"RetargetAnimBaker: Humanoid animation bake failed. {humanoidError}");
                        return null;
                    }

                    return humanoidClip;
                }

                string path = AssetDatabase.GenerateUniqueAssetPath($"{savePath}/{clip.name}.anim");
                AssetDatabase.CreateAsset(clip, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                AnimationClip savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                return savedClip != null ? savedClip : clip;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                directFbxBaker?.DisposeDirectPoseRecorder();

                if (_sourceRigComponent != null)
                {
                    _sourceRigComponent.ApplyHierarchyCachedPose();
                }

                if (_targetRigComponent != null)
                {
                    _targetRigComponent.ApplyHierarchyCachedPose();
                }
                
                bakeRoot.transform.rotation = originalRootRotation;
            }
        }
        private bool TryExportClipAsFbx(AnimationClip bakedClip, AnimationClip sourceClip, float sourceStartTime,
            float sourceEndTime, bool importAsHumanoid, string outputPath, out string error)
        {
            error = string.Empty;

            if (bakedClip == null)
            {
                error = "Baked clip is null.";
                return false;
            }

            GameObject exportSource = _targetCharacter != null ? _targetCharacter : _targetCharacterInstance;
            if (exportSource == null)
            {
                error = "Target character is not assigned.";
                return false;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                error = "FBX output path is empty.";
                return false;
            }

            string exportClipName = GetBakedClipName(sourceClip);
            bakedClip.name = exportClipName;
            return TryExportClipAsSharedFbx(exportSource, bakedClip, sourceClip, sourceStartTime,
                sourceEndTime, importAsHumanoid, outputPath, exportClipName, out error);
        }

        private bool TryExportClipAsSharedFbx(GameObject exportSource, AnimationClip bakedClip, AnimationClip sourceClip,
            float sourceStartTime, float sourceEndTime, bool importAsHumanoid, string outputPath, string exportClipName,
            out string error)
        {
            error = string.Empty;
            GameObject exportModel = null;

            try
            {
                GameObject model = exportSource;
                if (importAsHumanoid)
                {
                    exportModel = Object.Instantiate(exportSource);
                    if (exportModel == null)
                    {
                        error = "Failed to create the temporary Humanoid export model.";
                        return false;
                    }

                    exportModel.name = exportSource.name;
                    exportModel.hideFlags = HideFlags.HideAndDontSave;
                    foreach (Animator animator in exportModel.GetComponentsInChildren<Animator>(true))
                    {
                        Object.DestroyImmediate(animator);
                    }

                    model = exportModel;
                }

                float exportSampleRate = bakedClip.frameRate > 0f ? bakedClip.frameRate : _frameRate;
                var options = new AsciiFbxExporter.ExportOptions
                {
                    model = model,
                    clip = bakedClip,
                    outputAssetPath = outputPath,
                    startTime = 0f,
                    endTime = bakedClip.length,
                    sampleRate = exportSampleRate,
                    stripRootNode = true,
                    includeScaleCurves = true,
                    optimizeConstantCurves = true
                };

                EditorUtility.DisplayProgressBar("Retarget Pro", $"Exporting {bakedClip.name}.fbx", 0.96f);
                if (!AsciiFbxExporter.Export(options, out error))
                {
                    return false;
                }

                EditorUtility.DisplayProgressBar("Retarget Pro", "Configuring FBX import settings", 0.99f);
                return TryConfigureFbxImportSettings(outputPath, sourceClip, sourceStartTime, sourceEndTime,
                    importAsHumanoid, exportClipName, out error);
            }
            finally
            {
                if (exportModel != null)
                {
                    Object.DestroyImmediate(exportModel);
                }
            }
        }

        private bool IsHumanoidRigSelected()
        {
            EnsureRigTypeSelection();
            return string.Equals(_selectedBakerId, HumanoidAnimationBaker.BakerId, StringComparison.Ordinal);
        }

        private bool TryCreateHumanoidAnimationClip(AnimationClip bakedClip, AnimationClip sourceClip,
            float sourceStartTime, float sourceEndTime, out AnimationClip savedClip, out string error)
        {
            savedClip = null;
            error = string.Empty;

            string clipName = GetBakedClipName(sourceClip);
            string temporaryFbxPath = AssetDatabase.GenerateUniqueAssetPath($"{SavePath}/{clipName}_Temp.fbx");

            try
            {
                if (!TryExportClipAsFbx(bakedClip, sourceClip, sourceStartTime, sourceEndTime, true,
                        temporaryFbxPath, out error))
                {
                    return false;
                }

                AnimationClip importedClip = FindImportedAnimationClip(temporaryFbxPath, clipName);
                if (importedClip == null)
                {
                    error = "The temporary Humanoid FBX does not contain an animation clip.";
                    return false;
                }

                if (!importedClip.isHumanMotion)
                {
                    error = "The temporary FBX clip was not imported as Humanoid.";
                    return false;
                }

                var extractedClip = new AnimationClip();
                EditorUtility.CopySerialized(importedClip, extractedClip);
                extractedClip.name = clipName;
                ApplyCopiedClipSettings(sourceClip, extractedClip, sourceStartTime, sourceEndTime);

                string animationPath = AssetDatabase.GenerateUniqueAssetPath($"{SavePath}/{clipName}.anim");
                AssetDatabase.CreateAsset(extractedClip, animationPath);
                EditorUtility.SetDirty(extractedClip);
                AssetDatabase.SaveAssetIfDirty(extractedClip);
                AssetDatabase.ImportAsset(animationPath, ImportAssetOptions.ForceSynchronousImport);

                savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animationPath);
                if (savedClip == null)
                {
                    error = "Failed to create the standalone Humanoid animation clip.";
                    return false;
                }

                return true;
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(temporaryFbxPath) != null || File.Exists(temporaryFbxPath))
                {
                    if (!AssetDatabase.DeleteAsset(temporaryFbxPath))
                    {
                        Debug.LogWarning(
                            $"RetargetAnimBaker: Failed to remove temporary FBX `{temporaryFbxPath}`.");
                    }
                }
            }
        }

        private static AnimationClip FindImportedAnimationClip(string assetPath, string clipName)
        {
            AnimationClip fallback = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (!(asset is AnimationClip clip))
                {
                    continue;
                }

                if (string.Equals(clip.name, clipName, StringComparison.Ordinal))
                {
                    return clip;
                }

                fallback ??= clip;
            }

            return fallback;
        }

        private bool TryConfigureFbxImportSettings(string fbxAssetPath, AnimationClip sourceClip, float sourceStartTime,
            float sourceEndTime, bool importAsHumanoid, string importedClipName, out string error)
        {
            error = string.Empty;

            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                error = "Exported FBX importer was not found.";
                return false;
            }

            Avatar targetAvatar = null;
            if (importAsHumanoid)
            {
                targetAvatar = GetTargetAvatar(true);
                if (targetAvatar != null && !EditorUtility.IsPersistent(targetAvatar))
                {
                    string avatarPath = AssetDatabase.GetAssetPath(targetAvatar);
                    if (!string.IsNullOrEmpty(avatarPath))
                    {
                        targetAvatar = GetAvatarFromAssetPath(avatarPath, true, targetAvatar.name);
                    }
                }

                if (targetAvatar == null || !targetAvatar.isValid || !targetAvatar.isHuman ||
                    !EditorUtility.IsPersistent(targetAvatar))
                {
                    error = "Selected Humanoid rig type, but the target Humanoid avatar is missing or invalid.";
                    return false;
                }
            }

            bool changed = AsciiFbxExporter.ApplyImporterSettings(importer);
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                changed = true;
            }

            if (importer.sourceAvatar != null)
            {
                importer.sourceAvatar = null;
                changed = true;
            }

            ApplyAvatarSerializedFallback(importer, null, false, ref changed);
            ApplySourceClipSettingsToImporter(importer, sourceClip, sourceStartTime, sourceEndTime, importedClipName,
                ref changed);

            if (changed)
            {
                importer.SaveAndReimport();
            }

            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                error = "Exported FBX importer was not preserved after the Generic import.";
                return false;
            }

            if (!importAsHumanoid)
            {
                if (importer.animationType != ModelImporterAnimationType.Generic ||
                    importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar || importer.sourceAvatar != null)
                {
                    error = "Exported FBX did not retain its Generic rig without an avatar.";
                    return false;
                }

                return true;
            }

            changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                changed = true;
            }

            if (importer.sourceAvatar != targetAvatar)
            {
                importer.sourceAvatar = targetAvatar;
                changed = true;
            }

            ApplyAvatarSerializedFallback(importer, targetAvatar, true, ref changed);
            if (changed)
            {
                importer.SaveAndReimport();
            }

            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter verifyImporter = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (verifyImporter == null || verifyImporter.animationType != ModelImporterAnimationType.Human ||
                verifyImporter.avatarSetup != ModelImporterAvatarSetup.CopyFromOther ||
                verifyImporter.sourceAvatar != targetAvatar)
            {
                error = "Exported FBX did not retain the target Humanoid avatar settings.";
                return false;
            }

            return true;
        }

        private void ApplySourceClipSettingsToImporter(ModelImporter importer, AnimationClip sourceClip,
            float sourceStartTime, float sourceEndTime, string importedClipName, ref bool changed)
        {
            if (importer == null)
            {
                return;
            }

            ModelImporterClipAnimation[] clipAnimations = importer.defaultClipAnimations;
            if (clipAnimations == null || clipAnimations.Length == 0)
            {
                return;
            }

            bool applySourceSettings = _copyClipSettings && sourceClip != null;
            bool trimmedRange = IsTrimmedRange(sourceClip, sourceStartTime, sourceEndTime);
            AnimationClipSettings sourceSettings = default;
            if (applySourceSettings)
            {
                sourceSettings = AnimationUtility.GetAnimationClipSettings(sourceClip);
            }

            bool clipSettingsChanged = false;
            for (int i = 0; i < clipAnimations.Length; i++)
            {
                ModelImporterClipAnimation clipAnimation = clipAnimations[i];

                if (!string.IsNullOrEmpty(importedClipName) && clipAnimation.name != importedClipName)
                {
                    clipAnimation.name = importedClipName;
                    clipSettingsChanged = true;
                }

                if (applySourceSettings)
                {
                    clipAnimation.loopTime = !trimmedRange && LoopCount <= DefaultLoopCount && sourceSettings.loopTime;
                    clipAnimation.keepOriginalOrientation = sourceSettings.keepOriginalOrientation;
                    clipAnimation.keepOriginalPositionY = sourceSettings.keepOriginalPositionY;
                    clipAnimation.keepOriginalPositionXZ = sourceSettings.keepOriginalPositionXZ;
                    clipAnimation.heightFromFeet = sourceSettings.heightFromFeet;
                    clipAnimation.mirror = sourceSettings.mirror;
                    clipAnimation.cycleOffset = sourceSettings.cycleOffset;
                    clipSettingsChanged = true;
                }

                clipAnimations[i] = clipAnimation;
            }

            if (!clipSettingsChanged)
            {
                return;
            }

            importer.clipAnimations = clipAnimations;
            changed = true;
        }

        private static void ApplyAvatarSerializedFallback(ModelImporter importer, Avatar sourceAvatar, bool copyFromOther,
            ref bool changed)
        {
            if (importer == null)
            {
                return;
            }

            SerializedObject serializedImporter = new SerializedObject(importer);
            bool serializedChanged = false;

            SerializedProperty avatarSetupProperty = serializedImporter.FindProperty("m_AvatarSetup");
            if (avatarSetupProperty == null)
            {
                avatarSetupProperty = serializedImporter.FindProperty("avatarSetup");
            }
            if (avatarSetupProperty != null)
            {
                int desiredSetup = copyFromOther ? (int)ModelImporterAvatarSetup.CopyFromOther : (int)ModelImporterAvatarSetup.NoAvatar;
                if (avatarSetupProperty.intValue != desiredSetup)
                {
                    avatarSetupProperty.intValue = desiredSetup;
                    serializedChanged = true;
                }
            }

            SerializedProperty avatarSourceProperty =
                serializedImporter.FindProperty("m_LastHumanDescriptionAvatarSource") ??
                serializedImporter.FindProperty("lastHumanDescriptionAvatarSource") ??
                serializedImporter.FindProperty("m_SourceAvatar") ??
                serializedImporter.FindProperty("sourceAvatar");
            if (avatarSourceProperty != null && avatarSourceProperty.objectReferenceValue != sourceAvatar)
            {
                avatarSourceProperty.objectReferenceValue = sourceAvatar;
                serializedChanged = true;
            }

            if (serializedChanged)
            {
                serializedImporter.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
        }

        private Avatar GetSourceAvatar()
        {
            GameObject sourceObject = _sourceCharacter != null ? _sourceCharacter : _sourceCharacterInstance;
            if (sourceObject == null)
            {
                return null;
            }

            Animator animator = sourceObject.GetComponentInChildren<Animator>(true);
            Avatar animatorAvatar = null;
            if (animator != null)
            {
                animatorAvatar = animator.avatar;
                if (IsAvatarMatch(animatorAvatar, false) && EditorUtility.IsPersistent(animatorAvatar))
                {
                    return animatorAvatar;
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(sourceObject);
            if (string.IsNullOrEmpty(assetPath) && _sourceCharacter != null)
            {
                assetPath = AssetDatabase.GetAssetPath(_sourceCharacter);
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (animatorAvatar != null)
            {
                string avatarAssetPath = AssetDatabase.GetAssetPath(animatorAvatar);
                Avatar avatarFromAnimatorAssetPath = GetAvatarFromAssetPath(avatarAssetPath, false,
                    animatorAvatar.name);
                if (avatarFromAnimatorAssetPath != null)
                {
                    return avatarFromAnimatorAssetPath;
                }
            }

            return GetAvatarFromAssetPath(assetPath, false, animatorAvatar != null ? animatorAvatar.name : null);
        }

        private Avatar GetTargetAvatar(bool requireHumanoid)
        {
            GameObject targetObject = _targetCharacter != null ? _targetCharacter : _targetCharacterInstance;
            if (targetObject == null)
            {
                return null;
            }

            Animator animator = targetObject.GetComponentInChildren<Animator>(true);
            Avatar animatorAvatar = null;
            if (animator != null)
            {
                animatorAvatar = animator.avatar;
                if (IsAvatarMatch(animatorAvatar, requireHumanoid) && EditorUtility.IsPersistent(animatorAvatar))
                {
                    return animatorAvatar;
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) && _targetCharacter != null)
            {
                assetPath = AssetDatabase.GetAssetPath(_targetCharacter);
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (animatorAvatar != null)
            {
                string avatarAssetPath = AssetDatabase.GetAssetPath(animatorAvatar);
                Avatar avatarFromAnimatorAssetPath = GetAvatarFromAssetPath(avatarAssetPath, requireHumanoid,
                    animatorAvatar.name);
                if (avatarFromAnimatorAssetPath != null)
                {
                    return avatarFromAnimatorAssetPath;
                }
            }

            return GetAvatarFromAssetPath(assetPath, requireHumanoid, animatorAvatar != null ? animatorAvatar.name : null);
        }

        private Avatar GetAvatarFromAssetPath(string assetPath, bool requireHumanoid, string preferredAvatarName = null)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // If a model already copies an avatar from another source, prefer that explicit source.
            ModelImporter modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (modelImporter != null && IsAvatarMatch(modelImporter.sourceAvatar, requireHumanoid) &&
                (string.IsNullOrEmpty(preferredAvatarName) ||
                 string.Equals(modelImporter.sourceAvatar.name, preferredAvatarName, StringComparison.Ordinal)))
            {
                return modelImporter.sourceAvatar;
            }

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (!string.IsNullOrEmpty(preferredAvatarName))
            {
                for (int i = 0; i < subAssets.Length; i++)
                {
                    Avatar avatar = subAssets[i] as Avatar;
                    if (avatar == null || !string.Equals(avatar.name, preferredAvatarName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsAvatarMatch(avatar, requireHumanoid))
                    {
                        return avatar;
                    }
                }
            }

            for (int i = 0; i < subAssets.Length; i++)
            {
                Avatar avatar = subAssets[i] as Avatar;
                if (avatar == null)
                {
                    continue;
                }
                if (IsAvatarMatch(avatar, requireHumanoid))
                {
                    return avatar;
                }
            }

            return null;
        }

        private static bool IsAvatarMatch(Avatar avatar, bool requireHumanoid)
        {
            if (avatar == null || !avatar.isValid)
            {
                return false;
            }

            if (requireHumanoid)
            {
                return avatar.isHuman;
            }

            return true;
        }

        private void SwapSourceAndTarget(out string message, out MessageType messageType)
        {
            message = string.Empty;
            messageType = MessageType.Info;
            if (retargetProfile == null)
            {
                return;
            }

            if (IsInitialized)
            {
                UnInitializeBaker();
            }

            Undo.RecordObject(retargetProfile, "Swap Source and Target");

            (retargetProfile.sourceCharacter, retargetProfile.targetCharacter) =
                (retargetProfile.targetCharacter, retargetProfile.sourceCharacter);
            (retargetProfile.sourceRig, retargetProfile.targetRig) =
                (retargetProfile.targetRig, retargetProfile.sourceRig);
            (retargetProfile.sourcePose, retargetProfile.targetPose) =
                (retargetProfile.targetPose, retargetProfile.sourcePose);

            if (!RetargetProfileModelRigUtility.TryComposeProfileRigs(retargetProfile, true, out string composeMessage))
            {
                message = $"Failed to compose swapped profile rigs for `{retargetProfile.name}`: {composeMessage}";
                messageType = MessageType.Warning;
            }
            else
            {
                message = composeMessage;
            }

            EditorUtility.SetDirty(retargetProfile);
            AssetDatabase.SaveAssets();

            SyncCharactersFromProfile();
            SelectRigTypeFromTargetAvatar();
            if (!PreparePreviewCharactersFromProfile(out string prepareError))
            {
                string prepareMessage =
                    $"Failed to rebuild swapped previews for `{retargetProfile.name}`: {prepareError}";
                message = string.IsNullOrEmpty(message) ? prepareMessage : $"{message}\n{prepareMessage}";
                messageType = MessageType.Warning;
            }
            RefreshPreviewEditors();
            onProfileChanged?.Invoke(retargetProfile);
        }
        
        private void ApplyProfileReferencePoses()
        {
            if (retargetProfile == null)
            {
                return;
            }

            if (_sourceCharacterInstance != null && retargetProfile.sourcePose != null)
            {
                retargetProfile.sourcePose.SampleAnimation(_sourceCharacterInstance, 0f);
            }

            if (_targetCharacterInstance != null && retargetProfile.targetPose != null)
            {
                retargetProfile.targetPose.SampleAnimation(_targetCharacterInstance, 0f);
            }

            if (_itemInstance != null && retargetProfile.clipItemAnimation != null)
            {
                retargetProfile.clipItemAnimation.SampleAnimation(_itemInstance, 0f);
            }
            
            CachePreviewRootMotionReferencePose();
            RestorePreviewRootMotionReferencePose();
        }
        
        private void CleanupBaker()
        {
            if (IsInitialized && _sourceRigComponent != null)
            {
                _sourceRigComponent.ApplyHierarchyCachedPose();
            }

            if (IsInitialized && _targetRigComponent != null)
            {
                _targetRigComponent.ApplyHierarchyCachedPose();
            }

            if (RetargetComponent != null)
            {
                RetargetComponent.DestroyRetargetFeatures();
            }

            ReleasePreviewCharacters();

            RetargetComponent = null;
            IsInitialized = false;
            RefreshPreviewEditors();
        }
    }
}
