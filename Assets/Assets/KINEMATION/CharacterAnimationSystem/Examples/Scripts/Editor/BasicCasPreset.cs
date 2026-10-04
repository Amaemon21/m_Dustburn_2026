// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Linq;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.BoneLookups;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Setup;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KINEMATION.CharacterAnimationSystem.Examples.Scripts.Editor
{
    [CustomSetupName("Basic (Perfect for beginners)")]
    public class BasicCasPreset : CasSetupPreset
    {
        protected const string CasExamplesPath = "Assets/KINEMATION/CharacterAnimationSystem/Examples/";
        protected const string CasDemoPath = "Assets/CAS Demo/";
        
        protected const string CasInputsPath = CasDemoPath + "Inputs_CAS.inputactions";
        protected const string CasHumanoidAnimatorPath 
            = CasDemoPath + "Animations/Humanoid/OC_CAS_Humanoid.overrideController";
        protected const string CasStepInPlace = CasExamplesPath + "Steps/Step_InPlace.asset";
        protected const string CasStepCrouch = CasExamplesPath + "Steps/Step_Crouch.asset";
        protected const string CasStepUncrouch = CasExamplesPath + "Steps/Step_Uncrouch.asset";
        protected const string CasStepWalkStart = CasExamplesPath + "Steps/Step_WalkStart.asset";
        protected const string CasStepWalkStop = CasExamplesPath + "Steps/Step_WalkStop.asset";
        
        protected SetupRigElementProperty _rightHand;
        protected SetupRigElementProperty _leftHand;
        protected SetupRigElementProperty _pelvis;
        protected SetupRigElementProperty _rightFoot;
        protected SetupRigElementProperty _leftFoot;
        protected SetupRigElementChainProperty _spineBoneChain;
        
        protected CharacterExampleController _controller;

        protected virtual bool FindMatchingBones(CharacterSkeletonBone skeletonBone)
        {
            var lookupPreset = _layeredBlendingWidget.GetSelectedPreset();
            
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetRightHandLookups()))
            {
                _rightHand.element = skeletonBone.rigElement;
                return true;
            }
            
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetLeftHandLookups()))
            {
                _leftHand.element = skeletonBone.rigElement;
                return true;
            }
                
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetRightFootLookups()))
            {
                _rightFoot.element = skeletonBone.rigElement;
                return true;
            }
                
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetLeftFootLookups()))
            {
                _leftFoot.element = skeletonBone.rigElement;
                return true;
            }
                
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetPelvisLookups()))
            {
                _pelvis.element = skeletonBone.rigElement;
                return true;
            }

            return false;
        }
        
        public override void Initialize()
        {
            base.Initialize();
            
            _rightHand = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _leftHand = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _rightFoot = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _leftFoot = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _pelvis = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _spineBoneChain = ScriptableObject.CreateInstance<SetupRigElementChainProperty>();

            UpdateBoneReferences();
            
            _rightHand.Initialize(setupData.skeleton, "Right Hand");
            _leftHand.Initialize(setupData.skeleton, "Left Hand");
            _rightFoot.Initialize(setupData.skeleton, "Right Foot");
            _leftFoot.Initialize(setupData.skeleton, "Left Foot");
            _pelvis.Initialize(setupData.skeleton, "Pelvis");
            _spineBoneChain.Initialize(setupData.skeleton, "Spine Bones");
        }

        public override void UpdateBoneReferences()
        {
            _rightHand.element = new KRigElement(-1);
            _leftHand.element = new KRigElement(-1);
            _rightFoot.element = new KRigElement(-1);
            _leftFoot.element = new KRigElement(-1);
            _pelvis.element = new KRigElement(-1);
            _spineBoneChain.chain = new KRigElementChain();
            
            foreach (var skeletonBone in setupData.skeleton.SkeletonBones)
            {
                FindMatchingBones(skeletonBone);
            }

            var lookupPreset = _layeredBlendingWidget.GetSelectedPreset();
            foreach (var chain in setupData.skeleton.ElementChains)
            {
                if (!BoneLookupUtility.IsNameMatching(chain.chainName, lookupPreset.GetSpineLookups()))
                {
                    continue;
                }

                foreach (var element in chain.elementChain) _spineBoneChain.chain.elementChain.Add(element);
            }
            
            _rightHand.Update();
            _leftHand.Update();
            _rightFoot.Update();
            _leftFoot.Update();
            _pelvis.Update();
            _spineBoneChain.Update();
        }

        protected virtual bool ValidateBoneProperties()
        {
            _rightHand.DrawProperty();
            if (_rightHand.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select right hand bone!", MessageType.Warning);
                return false;
            }

            _leftHand.DrawProperty();
            if (_leftHand.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select left hand bone!", MessageType.Warning);
                return false;
            }
            
            _rightFoot.DrawProperty();
            if (_rightFoot.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select right foot bone!", MessageType.Warning);
                return false;
            }

            _leftFoot.DrawProperty();
            if (_leftFoot.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select left foot bone!", MessageType.Warning);
                return false;
            }
            
            _pelvis.DrawProperty();
            if (_pelvis.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select pelvis/hip bone!", MessageType.Warning);
                return false;
            }

            return true;
        }

        protected virtual GameObject CreateOrGetCharacterGameObject()
        {
            Transform modelTransform = setupData.characterModel.transform;
            var prefabObject = modelTransform.parent;
            if (prefabObject == null)
            {
                prefabObject = new GameObject($"CAS_{modelTransform.name}").transform;
                prefabObject.position = modelTransform.position;
                prefabObject.rotation = modelTransform.rotation;
            }

            GameObject characterPrefab = prefabObject.gameObject;
            modelTransform.parent = prefabObject;

            return characterPrefab;
        }

        protected GameObject SaveCharacterPrefab(GameObject prefab, string prefabPath)
        {
            PrefabInstanceStatus prefabStatus = PrefabUtility.GetPrefabInstanceStatus(prefab);
            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefab);
            
            if (prefabStatus == PrefabInstanceStatus.Connected && prefabAssetType != PrefabAssetType.Model)
            {
                PrefabUtility.ApplyPrefabInstance(prefab, InteractionMode.UserAction);
                return PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            }
            
            return PrefabUtility.SaveAsPrefabAssetAndConnect(prefab, prefabPath, 
                InteractionMode.UserAction);
        }

        protected void SaveCharacterPrefabs(GameObject characterPrefab, ProceduralAnimationSettings pas)
        {
            var activeFolder = KEditorUtility.GetProjectActiveFolder();
            
            var skeletonPrefabName = $"Skeleton_{setupData.characterModel.name}.prefab";
            SaveCharacterPrefab(setupData.characterModel, $"{activeFolder}/{skeletonPrefabName}");
            
            characterPrefab = SaveCharacterPrefab(characterPrefab, 
                $"{activeFolder}/{characterPrefab.name}.prefab");
            
            pas.characterPrefab = characterPrefab;
            pas.UpdateCharacterPrefab();
            EditorUtility.SetDirty(pas);
        }

        public override bool Validate()
        {
            _layeredBlendingWidget.OnGUI();
            if (!ValidateAnimations()) return false;
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Bones", KEditorUtility.boldLabel);
            
            if (!ValidateBoneProperties()) return false;

            _spineBoneChain.DrawProperty();
            if (_spineBoneChain.chain.elementChain.Count == 0 ||
                _spineBoneChain.chain.elementChain.Any(e => e.index == -1))
            {
                EditorGUILayout.HelpBox("Assign all spine bones!", MessageType.Warning);
                return false;
            }

            return true;
        }

        public override void Apply()
        {
            // 1. Create or get a parent Game Object.
            GameObject characterPrefab = CreateOrGetCharacterGameObject();
            
            // 2. Setup the controller and camera.
            SetupCharacterController<CharacterExampleController>(characterPrefab);
            SetupCharacterCamera(characterPrefab, out var camera);
            
            // 3. Create Character and Procedural Animation Settings.
            CreateAndSaveAssets(out var cas, out var pas);
            
            Transform root = setupData.skeleton.SkeletonBones[0].transform;
            AddOrGetTransform(root, CasNames.Bone_IkHandRight);
            AddOrGetTransform(root, CasNames.Bone_IkHandLeft);
            
            AddOrGetTransform(root, CasNames.Bone_IkFootRight);
            AddOrGetTransform(root, CasNames.Bone_IkFootLeft);
            setupData.skeleton.UpdateSkeleton();
            
            AddLookModifier(pas, _spineBoneChain.chain);
            
            AddCopyFeetModifiers(pas, new KRigElement(-1, _rightFoot.element.name),
                new KRigElement(-1, _leftFoot.element.name));
            
            AddAdditiveLean(pas, _spineBoneChain.chain);
            AddHandIkModifiers(pas);
            AddLegIkModifiers(pas);
            
            // 5. Create up-to-date bone chains for layered blending.
            setupData.skeleton.UpdateSkeleton();
            _layeredBlendingWidget.CreateBoneChains(setupData.layeredBlending);
            
            // 6. Create Skeleton and Character prefabs.
            SaveCharacterPrefabs(characterPrefab, pas);
        }

        protected void SetupCharacterController<T>(GameObject parentPrefab) where T: CharacterExampleController
        {
#if UNITY_INPUT_SYSTEM_ENABLED
            var inputs = CharacterAnimationSetup.AddOrGetComponent<PlayerInput>(parentPrefab);
            InputActionAsset defaultInputs = AssetDatabase.LoadAssetAtPath<InputActionAsset>(CasInputsPath);

            if (defaultInputs != null && defaultInputs.actionMaps.Count > 0)
            {
                inputs.actions = defaultInputs;
                inputs.defaultActionMap = defaultInputs.actionMaps[0].name;
            }
            
            inputs.notificationBehavior = PlayerNotifications.SendMessages;
#endif
            _controller = CharacterAnimationSetup.AddOrGetComponent<T>(parentPrefab);
            
            var crouchStep = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(CasStepCrouch);
            if (crouchStep != null) _controller.stepCrouch = crouchStep;
            
            crouchStep = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(CasStepUncrouch);
            if (crouchStep != null) _controller.stepUncrouch = crouchStep;
            
            var stepInPlace = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(CasStepInPlace);
            if (stepInPlace) _controller.stepInPlace = stepInPlace;
            
            var stepWalkStart = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(CasStepWalkStart);
            if (stepWalkStart) _controller.startMoving = stepWalkStart;
            
            var stepWalkStop = AssetDatabase.LoadAssetAtPath<StepModifierSettings>(CasStepWalkStop);
            if (stepWalkStop) _controller.stopMoving = stepWalkStop;
            
            var charController = parentPrefab.GetComponent<CharacterController>();
            if (charController == null)
            {
                charController = parentPrefab.AddComponent<CharacterController>();
                charController.radius = 0.3f;
                charController.center = new Vector3(0f, 0.92f, 0f);
                charController.height = 1.84f;
                charController.skinWidth = 0.001f;
            }
            
            var animator = CharacterAnimationSetup.AddOrGetComponent<Animator>(setupData.characterModel);
            if (animator.runtimeAnimatorController == null && animator.isHuman)
            {
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CasHumanoidAnimatorPath);
                animator.applyRootMotion = false;
            }

            CharacterAnimationSetup.AddOrGetComponent<CharacterTrajectory>(setupData.characterModel);
        }

        protected void SetupCharacterCamera(GameObject parentPrefab, out CharacterCamera charCamera)
        {
            Camera camera = parentPrefab.GetComponentInChildren<Camera>();
            
            if (camera == null)
            {
                var cameraGO = new GameObject("Camera");
                cameraGO.transform.SetParent(parentPrefab.transform);
                cameraGO.transform.localPosition = Vector3.zero;
                cameraGO.transform.localRotation = Quaternion.identity;

                camera = cameraGO.AddComponent<Camera>();
                cameraGO.AddComponent<AudioListener>();
            }
            
            charCamera = CharacterAnimationSetup.AddOrGetComponent<CharacterCamera>(camera.gameObject);
        }

        protected void AddFootIkModifier(ProceduralAnimationSettings pas)
        {
            var modifier = ScriptableObject.CreateInstance<FootIkSettings>();
            modifier.name = "Foot IK";
            modifier.pelvis = _pelvis.element;
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            
            modifier.weightOverrides[0].weight.Bind(typeof(LayeredBlendingComponent), 
                nameof(setupData.layeredBlending.Traversal_Weight) + ".value");
            
            AddModifier(pas, modifier);
        }

        protected void AddStrideModifier(ProceduralAnimationSettings pas)
        {
            var modifier = ScriptableObject.CreateInstance<StrideModifierSettings>();
            modifier.name = "Stride Modifier";

            var lowerBodyBones = new LowerBodyBones()
            {
                pelvis = _pelvis.element,
                rightFoot = _rightFoot.element,
                leftFoot = _leftFoot.element,
                rightFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootRight),
                leftFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootLeft),
            };
            
            modifier.bones = lowerBodyBones;

            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });

            modifier.weightOverrides[0].weight.Bind(typeof(LayeredBlendingComponent),
                nameof(setupData.layeredBlending.Traversal_Weight) + ".value");

            AddModifier(pas, modifier);
        }
        
        protected void AddStepModifier(ProceduralAnimationSettings pas)
        {
            var modifier = ScriptableObject.CreateInstance<StepModifierSettings>();
            modifier.name = "Step Modifier";
            modifier.pelvis = _pelvis.element;
            modifier.rightFoot = _rightFoot.element;
            modifier.leftFoot = _leftFoot.element;
            modifier.rightFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootRight);
            modifier.leftFootIkTarget = new KRigElement(-1, CasNames.Bone_IkFootLeft);
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            
            modifier.weightOverrides[0].weight.Bind(typeof(LayeredBlendingComponent), 
                nameof(setupData.layeredBlending.Traversal_Weight) + ".value");
            
            AddModifier(pas, modifier);
        }

        protected void AddAdditiveLean(ProceduralAnimationSettings settings, KRigElementChain leanChain)
        {
            var pivotSettings = ScriptableObject.CreateInstance<PivotModifierSettings>();
            pivotSettings.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            pivotSettings.weightOverrides[0].weight.Bind(typeof(LayeredBlendingComponent), 
                nameof(setupData.layeredBlending.Traversal_Weight) + ".value");
            pivotSettings.pelvis = _pelvis.element;

            float angle = 90f / leanChain.elementChain.Count;
            foreach (var spine in leanChain.elementChain)
            {
                var layer = new LookLayerElement
                {
                    rigElement = spine,
                    clampedAngle = new Vector2(angle, angle),
                    cachedClampedAngle = new Vector2(angle, angle)
                };
                
                pivotSettings.spineBones.Add(layer);
            }

            pivotSettings.name = "Movement Turns";
            pivotSettings.alpha = 1f;
            AddModifier(settings, pivotSettings);
        }

        protected void AddLookModifier(ProceduralAnimationSettings settings, KRigElementChain spineBones)
        {
            var look = ScriptableObject.CreateInstance<LookModifierSettings>();
            WeightOverride weightOverride = new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, false)
            };
            
            weightOverride.weight.Bind(typeof(CharacterExampleController), 
                nameof(_controller.AimingWeight));
            look.weightOverrides.Add(weightOverride);
            
            weightOverride = new WeightOverride()
            {
                weight = new BindableProperty<float>(0f),
                weightModifier = new WeightModifier(0f, 0f, true)
            };
            weightOverride.weight.Bind(typeof(LayeredBlendingComponent), 
                nameof(setupData.layeredBlending.Mask_Look_Rotation) + ".value");
            
            look.weightOverrides.Add(weightOverride);

            float angle = 90f / spineBones.elementChain.Count;
            foreach (var spine in spineBones.elementChain)
            {
                var element = new LookLayerElement
                {
                    rigElement = spine,
                    clampedAngle = new Vector2(angle, angle),
                    cachedClampedAngle = new Vector2(angle, angle)
                };
                look.pitchOffsetElements.Add(element);
                look.yawOffsetElements.Add(element);
                look.rollOffsetElements.Add(element);
            }

            look.pitchInput.Bind(_controller, $"{nameof(_controller.LookInput)}.y");
            look.yawInput.Bind(_controller, $"{nameof(_controller.DeltaLookInput)}.x");
            look.useYawAsDelta = true;
            look.enableTurnInPlace = true;
            
            look.name = "Look Modifier";
            AddModifier(settings, look);
        }
        
        protected CopyBoneSettings CreateCopyBone(string name, KRigElement from, KRigElement to)
        {
            var c = ScriptableObject.CreateInstance<CopyBoneSettings>();
            c.name = name;
            c.copyFrom = from;
            c.copyTo = to;
            return c;
        }
        
        protected void AddCopyFeetModifiers(ProceduralAnimationSettings pas, KRigElement rightFoot, KRigElement leftFoot)
        {
            KRigElement copyTo = new KRigElement(-1, CasNames.Bone_IkFootRight);
            AddModifier(pas, CreateCopyBone("Copy Right Foot IK", rightFoot, copyTo));
            
            copyTo = new KRigElement(-1, CasNames.Bone_IkFootLeft);
            AddModifier(pas, CreateCopyBone("Copy Left Foot IK", leftFoot, copyTo));
        }
        
        private void AddLegIkModifiers(ProceduralAnimationSettings pas)
        {
            AddStrideModifier(pas);
            AddFootIkModifier(pas);
            AddStepModifier(pas);

            var rightFootIk = ScriptableObject.CreateInstance<TwoBoneIkSettings>();
            rightFootIk.tip.name = _rightFoot.element.name;
            rightFootIk.target.name = CasNames.Bone_IkFootRight;
            rightFootIk.name = "Right Foot IK";
            AddModifier(pas, rightFootIk);
            
            var leftFootIk = ScriptableObject.CreateInstance<TwoBoneIkSettings>();
            leftFootIk.tip.name = _leftFoot.element.name;
            leftFootIk.target.name = CasNames.Bone_IkFootLeft;
            leftFootIk.name = "Left Foot IK";
            AddModifier(pas, leftFootIk);
        }
        
        protected void AddHandIkModifiers(ProceduralAnimationSettings settings)
        {
            var rightHand = CreateTwoBoneIkModifier(nameof(_controller.RightHandIkTarget),
                nameof(setupData.layeredBlending.Enable_HandR_IK));
            rightHand.tip = _rightHand.element;
            rightHand.hintOffset = -Vector3.forward;
            rightHand.name = "Right Hand IK";
            AddModifier(settings, rightHand);

            var leftHand = CreateTwoBoneIkModifier(nameof(_controller.LeftHandIkTarget),
                nameof(setupData.layeredBlending.Enable_HandL_IK));
            leftHand.tip = _leftHand.element;
            leftHand.hintOffset = -Vector3.forward;
            leftHand.name = "Left Hand IK";
            AddModifier(settings, leftHand);
        }

        private TwoBoneIkSettings CreateTwoBoneIkModifier(string targetPath, string weightPath)
        {
            var ik = ScriptableObject.CreateInstance<TwoBoneIkSettings>();
            ik.useWorldTarget = true;
            
            ik.ikTargetTransform.Bind(_controller, $"{targetPath}");

            var weight = new BindableProperty<float>(1f);
            weight.Bind(setupData.layeredBlending, $"{weightPath}.value");

            ik.weightOverrides.Add(new WeightOverride
            {
                weight = weight,
                weightModifier = new WeightModifier(0f, 0f, false)
            });

            return ik;
        }
        
        protected void CreateAndSaveAssets(out CharacterAnimationSettings cas, out ProceduralAnimationSettings pas)
        {
            pas = ScriptableObject.CreateInstance<ProceduralAnimationSettings>();
            cas = ScriptableObject.CreateInstance<CharacterAnimationSettings>();
            
            cas.basePose = _basePose;
            cas.overlayPose = _overlayPose;
            cas.overlayAnimator = _overlayAnimator;
            cas.proceduralSettings = pas;
            
            string characterName = setupData.characterModel.name;
            string dir = KEditorUtility.GetProjectActiveFolder();
            KEditorUtility.SaveAsset(pas, dir, $"PA_{characterName}.asset");
            KEditorUtility.SaveAsset(cas, dir, $"CAS_{characterName}.asset");
            
            setupData.characterAnimation.SetAsset(cas);

            EditorUtility.SetDirty(pas);
            AssetDatabase.SaveAssetIfDirty(pas);
            
            EditorUtility.SetDirty(cas);
            AssetDatabase.SaveAssetIfDirty(cas);
        }
    }
}