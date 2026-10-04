// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Tools
{
    public class CasFootPelvisTranslationTool : IEditorTool
    {
        [Serializable]
        private struct AnimationCurveWrapperJson
        {
            public AnimationCurve curve;
        }

        [Serializable]
        private struct GenericPropertyJson
        {
            public string name;
            public int type;
            public GenericPropertyChildJson[] children;
        }

        [Serializable]
        private struct GenericPropertyChildJson
        {
            public string name;
            public int type;
            public string val;
        }

        internal const string CurveClipboardPrefix = "UnityEditor.AnimationCurveWrapperJSON:";
        internal const string GenericPropertyClipboardPrefix = "GenericPropertyJSON:";
        private const int GenericPropertyType = -1;
        private const int AnimationCurvePropertyType = 14;

        private static readonly string[] LeftFootTokens =
        {
            "leftfoot", "foot_l", "l_foot", "foot.l", "left_foot", "lfoot"
        };

        private static readonly string[] RightFootTokens =
        {
            "rightfoot", "foot_r", "r_foot", "foot.r", "right_foot", "rfoot"
        };

        private static readonly string[] PelvisTokens =
        {
            "pelvis", "hips", "hip", "root_hips"
        };

        private GameObject _characterModel;
        private AnimationClip _clip;
        private VectorCurve _rightFootCurve;
        private VectorCurve _leftFootCurve;
        private VectorCurve _pelvisCurve;
        private bool _hasExtractedCurves;
        private string _statusMessage = string.Empty;

        public void Init()
        {
        }

        public void Render()
        {
            EditorGUI.BeginChangeCheck();
            _characterModel = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Character Model", "Character model asset/prefab (scene objects are not allowed)."),
                _characterModel, typeof(GameObject), false);

            _clip = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("Animation Clip", "Clip to extract component-space translation curves from."),
                _clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                _hasExtractedCurves = false;
            }

            if (GUILayout.Button("Use Selected Clip"))
            {
                AnimationClip selectedClip = KEditorUtility.GetAnimationClipFromSelection();
                if (selectedClip != null)
                {
                    _clip = selectedClip;
                    _hasExtractedCurves = false;
                }
            }

            EditorGUILayout.Space();

            bool isValid = IsInputValid(out string validationMessage);
            if (!isValid)
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }

            GUI.enabled = isValid;
            if (GUILayout.Button("Extract Foot/Pelvis Translation Curves"))
            {
                ExtractTranslationCurves();
            }

            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }

            DrawExtractedCurves();
        }

        private bool IsInputValid(out string validationMessage)
        {
            if (_characterModel == null)
            {
                validationMessage = "Assign a character model asset.";
                return false;
            }

            if (!EditorUtility.IsPersistent(_characterModel))
            {
                validationMessage = "Character Model must be an asset reference, not a scene object.";
                return false;
            }

            if (!IsClipValid(_clip))
            {
                validationMessage = "Assign a valid animation clip.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private void ExtractTranslationCurves()
        {
            _statusMessage = string.Empty;
            if (!IsInputValid(out string validationMessage))
            {
                _statusMessage = validationMessage;
                return;
            }

            GameObject modelInstance = null;
            try
            {
                modelInstance = CreateModelInstance(_characterModel);
                if (modelInstance == null)
                {
                    _statusMessage = "Could not instantiate the character model.";
                    return;
                }

                if (!TryGetTrackedBones(modelInstance.transform, out var componentRoot, out var pelvis,
                        out var leftFoot, out var rightFoot))
                {
                    _statusMessage = "Could not resolve pelvis/left foot/right foot on the character model.";
                    return;
                }

                BuildTranslationCurves(_clip, modelInstance, componentRoot, pelvis, leftFoot, rightFoot,
                    out _rightFootCurve, out _leftFootCurve, out _pelvisCurve);
                _hasExtractedCurves = true;

                _statusMessage =
                    $"Extracted 3 VectorCurve outputs for '{_clip.name}'. Curves are preview-only and not written to the clip.";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _statusMessage = "Curve extraction failed. Check Console for details.";
            }
            finally
            {
                if (modelInstance != null)
                {
                    Object.DestroyImmediate(modelInstance);
                }
            }
        }

        private void DrawExtractedCurves()
        {
            if (!_hasExtractedCurves)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Extracted VectorCurves", KEditorUtility.boldLabel);

            DrawVectorCurveGroup("Right Foot", "rightFootMotion", ref _rightFootCurve);

            DrawVectorCurveGroup("Left Foot", "leftFootMotion", ref _leftFootCurve);

            DrawVectorCurveGroup("Pelvis", "pelvisMotion", ref _pelvisCurve);
        }

        private void DrawVectorCurveGroup(string title, string propertyName, ref VectorCurve curve)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (GUILayout.Button("Copy VectorCurve", GUILayout.Width(130f)))
            {
                CopyVectorCurveToSystemBuffer(propertyName, curve);
                _statusMessage = $"Copied {propertyName} to system buffer.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            curve.x = DrawCurveField("X", curve.x);
            curve.y = DrawCurveField("Y", curve.y);
            curve.z = DrawCurveField("Z", curve.z);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2f);
        }

        private static AnimationCurve DrawCurveField(string axisLabel, AnimationCurve curve)
        {
            return EditorGUILayout.CurveField(axisLabel, curve ?? new AnimationCurve());
        }

        private static void CopyVectorCurveToSystemBuffer(string propertyName, VectorCurve vectorCurve)
        {
            var payload = new GenericPropertyJson
            {
                name = propertyName,
                type = GenericPropertyType,
                children = new[]
                {
                    CreateCurveChild("x", vectorCurve.x),
                    CreateCurveChild("y", vectorCurve.y),
                    CreateCurveChild("z", vectorCurve.z)
                }
            };

            EditorGUIUtility.systemCopyBuffer = GenericPropertyClipboardPrefix + JsonUtility.ToJson(payload);
        }

        private static GenericPropertyChildJson CreateCurveChild(string axisName, AnimationCurve curve)
        {
            return new GenericPropertyChildJson
            {
                name = axisName,
                type = AnimationCurvePropertyType,
                val = SerializeCurveWrapper(curve ?? new AnimationCurve())
            };
        }

        private static string SerializeCurveWrapper(AnimationCurve source)
        {
            var payload = new AnimationCurveWrapperJson
            {
                curve = source ?? new AnimationCurve()
            };

            return CurveClipboardPrefix + JsonUtility.ToJson(payload);
        }

        private static void BuildTranslationCurves(AnimationClip clip, GameObject modelInstance, Transform componentRoot,
            Transform pelvis, Transform leftFoot, Transform rightFoot, out VectorCurve rightFootCurve,
            out VectorCurve leftFootCurve, out VectorCurve pelvisCurve)
        {
            rightFootCurve = CreateEmptyVectorCurve();
            leftFootCurve = CreateEmptyVectorCurve();
            pelvisCurve = CreateEmptyVectorCurve();

            if (clip == null || modelInstance == null || componentRoot == null || pelvis == null || leftFoot == null ||
                rightFoot == null)
            {
                return;
            }
            
            Debug.Log($"{rightFoot.name} {leftFoot.name} {pelvis.name}");

            float frameRate = clip.frameRate > Mathf.Epsilon ? clip.frameRate : 30f;
            float clipLength = clip.length;

            if (clipLength <= Mathf.Epsilon)
            {
                float endTime = 1f / frameRate;
                rightFootCurve = VectorCurve.Constant(0f, endTime, 0f);
                leftFootCurve = VectorCurve.Constant(0f, endTime, 0f);
                pelvisCurve = VectorCurve.Constant(0f, endTime, 0f);
                return;
            }

            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(clipLength * frameRate) + 1);

            AnimationCurve rightX = new AnimationCurve();
            AnimationCurve rightY = new AnimationCurve();
            AnimationCurve rightZ = new AnimationCurve();

            AnimationCurve leftX = new AnimationCurve();
            AnimationCurve leftY = new AnimationCurve();
            AnimationCurve leftZ = new AnimationCurve();

            AnimationCurve pelvisX = new AnimationCurve();
            AnimationCurve pelvisY = new AnimationCurve();
            AnimationCurve pelvisZ = new AnimationCurve();

            Vector3 pelvisReference = Vector3.zero;
            Vector3 leftFootReference = Vector3.zero;
            Vector3 rightFootReference = Vector3.zero;
            bool hasReference = false;

            for (int i = 0; i < sampleCount; i++)
            {
                float sampleTime = i == sampleCount - 1 ? clipLength : Mathf.Min(clipLength, i / frameRate);
                clip.SampleAnimation(modelInstance, sampleTime);

                Vector3 pelvisComponent = componentRoot.InverseTransformPoint(pelvis.position);
                Vector3 leftFootComponent = componentRoot.InverseTransformPoint(leftFoot.position);
                Vector3 rightFootComponent = componentRoot.InverseTransformPoint(rightFoot.position);

                if (!hasReference)
                {
                    pelvisReference = pelvisComponent;
                    leftFootReference = leftFootComponent;
                    rightFootReference = rightFootComponent;
                    hasReference = true;
                }

                Vector3 rightDelta = rightFootComponent - rightFootReference;
                Vector3 leftDelta = leftFootComponent - leftFootReference;
                Vector3 pelvisDelta = pelvisComponent - pelvisReference;

                AddVectorKey(sampleTime, rightDelta, rightX, rightY, rightZ);
                AddVectorKey(sampleTime, leftDelta, leftX, leftY, leftZ);
                AddVectorKey(sampleTime, pelvisDelta, pelvisX, pelvisY, pelvisZ);
            }

            rightFootCurve = new VectorCurve() { x = rightX, y = rightY, z = rightZ };
            leftFootCurve = new VectorCurve() { x = leftX, y = leftY, z = leftZ };
            pelvisCurve = new VectorCurve() { x = pelvisX, y = pelvisY, z = pelvisZ };
        }

        private static VectorCurve CreateEmptyVectorCurve()
        {
            return new VectorCurve()
            {
                x = new AnimationCurve(),
                y = new AnimationCurve(),
                z = new AnimationCurve()
            };
        }

        private static void AddVectorKey(float time, Vector3 value, AnimationCurve curveX, AnimationCurve curveY,
            AnimationCurve curveZ)
        {
            curveX.AddKey(time, value.x);
            curveY.AddKey(time, value.y);
            curveZ.AddKey(time, value.z);
        }

        private static GameObject CreateModelInstance(GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            GameObject instance = null;

            if (PrefabUtility.IsPartOfPrefabAsset(model))
            {
                instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            }

            if (instance == null)
            {
                instance = Object.Instantiate(model);
            }

            if (instance != null)
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
            }

            return instance;
        }

        private static bool TryGetTrackedBones(Transform root, out Transform componentRoot, out Transform pelvis,
            out Transform leftFoot, out Transform rightFoot)
        {
            componentRoot = root;
            pelvis = null;
            leftFoot = null;
            rightFoot = null;

            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                componentRoot = animator.transform;
                if (animator.isHuman)
                {
                    pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);

                    leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    if (leftFoot == null)
                    {
                        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                    }

                    rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (rightFoot == null)
                    {
                        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightToes);
                    }
                }
            }

            if (pelvis == null)
            {
                pelvis = FindBoneByTokens(root, PelvisTokens);
            }

            if (leftFoot == null)
            {
                leftFoot = FindBoneByTokens(root, LeftFootTokens);
            }

            if (rightFoot == null)
            {
                rightFoot = FindBoneByTokens(root, RightFootTokens);
            }

            return componentRoot != null && pelvis != null && leftFoot != null && rightFoot != null;
        }

        private static Transform FindBoneByTokens(Transform root, IReadOnlyList<string> tokens)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform bestMatch = null;
            int bestScore = int.MinValue;

            foreach (Transform bone in transforms)
            {
                string boneName = bone.name.ToLowerInvariant();

                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];

                    int score;
                    if (boneName.Equals(token))
                    {
                        score = 1000;
                    }
                    else
                    {
                        int index = boneName.IndexOf(token, StringComparison.Ordinal);
                        if (index < 0) continue;

                        score = 100 - index;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = bone;
                    }
                }
            }

            return bestMatch;
        }

        private static bool IsClipValid(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            if ((clip.hideFlags & HideFlags.HideInHierarchy) != 0)
            {
                return false;
            }

            if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public string GetToolCategory()
        {
            return "Animation/CAS";
        }

        public string GetToolName()
        {
            return "Foot/Pelvis Translation";
        }

        public string GetDocsURL()
        {
            return string.Empty;
        }

        public string GetToolDescription()
        {
            return
                "Extracts component-space translation deltas (frame 0 as reference) for right foot, left foot and pelvis.";
        }
    }
}