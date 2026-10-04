// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Core;
using KINEMATION.Shared.KAnimationCore.Editor;
using KINEMATION.Shared.KAnimationCore.Editor.CurveImporter;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.CharacterAnimationSystem.Scripts.Editor.Tools
{
    public class CasStrideSpeedTool : IEditorTool
    {
        private const string StrideSpeedCurveName = "StrideSpeed";
        private const string StrideWeightCurveName = "StrideWeight";
        private const string StrideXCurveName = "StrideX";
        private const string StrideYCurveName = "StrideY";
        private const float DefaultSampleRate = 30f;
        private const float MinSampleRate = 30f;
        private const float DefaultFootHeightOffset = 0.01f;

        private static readonly string[] GeneratedCurveNames =
        {
            StrideSpeedCurveName,
            StrideWeightCurveName,
            StrideXCurveName,
            StrideYCurveName
        };

        private static readonly string[] LeftFootQueryNames =
        {
            "leftfoot",
            "left_foot",
            "left-foot",
            "footleft",
            "foot_left",
            "lfoot",
            "l_foot",
            "foot_l",
            "leftankle",
            "left_ankle",
            "ankleleft",
            "lankle",
            "l_ankle",
            "ankle_l",
            "foot.l",
            "ankle.l",
            "mixamorig:leftfoot",
            "cc_base_l_foot",
            "def-foot.l",
            "def_foot_l",
            "bip001 l foot",
            "bip01 l foot"
        };

        private static readonly string[] RightFootQueryNames =
        {
            "rightfoot",
            "right_foot",
            "right-foot",
            "footright",
            "foot_right",
            "rfoot",
            "r_foot",
            "foot_r",
            "rightankle",
            "right_ankle",
            "ankleright",
            "rankle",
            "r_ankle",
            "ankle_r",
            "foot.r",
            "ankle.r",
            "mixamorig:rightfoot",
            "cc_base_r_foot",
            "def-foot.r",
            "def_foot_r",
            "bip001 r foot",
            "bip01 r foot"
        };

        private struct FootSample
        {
            public float time;
            public Vector3 localPosition;
        }

        private struct FootContactInterval
        {
            public int startIndex;
            public int lastPinnedIndex;
            public int exitIndex;
        }

        private struct FootAnalysisResult
        {
            public float strideSpeed;
            public Vector3 movementVector;
        }

        private struct StrideCurveSet
        {
            public AnimationCurve strideSpeed;
            public AnimationCurve strideWeight;
            public AnimationCurve strideX;
            public AnimationCurve strideY;
        }

        private GameObject _characterModel;
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();
        private Vector2 _scrollPosition;
        private float _sampleRate = DefaultSampleRate;
        private float _footHeightOffset = DefaultFootHeightOffset;
        private string _statusMessage = string.Empty;

        public void Init()
        {
        }

        public void Render()
        {
            _characterModel = (GameObject) EditorGUILayout.ObjectField("Character Model", _characterModel,
                typeof(GameObject), false);

            _sampleRate = EditorGUILayout.FloatField(new GUIContent("Sample Rate", "Sampling FPS for stride analysis."),
                _sampleRate);
            _sampleRate = Mathf.Max(MinSampleRate, _sampleRate);
            _footHeightOffset = Mathf.Max(0f, _footHeightOffset);

            EditorGUILayout.Space();
            DrawClipDropArea();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Selected Clips"))
            {
                foreach (var selectedObject in Selection.objects)
                {
                    AddClipsFromObject(selectedObject);
                }
            }

            if (GUILayout.Button("Clear List"))
            {
                _clips.Clear();
            }

            EditorGUILayout.EndHorizontal();

            DrawClipList();
            EditorGUILayout.Space();

            bool hasCharacterModel = _characterModel != null;
            bool isCharacterModelAsset = hasCharacterModel && EditorUtility.IsPersistent(_characterModel);
            bool isValid = isCharacterModelAsset && _clips.Count > 0;
            if (!isValid)
            {
                string validationMessage = !hasCharacterModel
                    ? "Assign a character model asset and add at least one animation clip."
                    : !isCharacterModelAsset
                        ? "Character Model must be an asset reference, not a scene object."
                        : "Add at least one animation clip.";
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }

            GUI.enabled = isValid;
            if (GUILayout.Button("Compute Stride Curves"))
            {
                ProcessStrideCurves();
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private void DrawClipDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag & Drop Animation Clips (or folders) here");

            Event currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            switch (currentEvent.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (currentEvent.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var droppedObject in DragAndDrop.objectReferences)
                        {
                            AddClipsFromObject(droppedObject);
                        }
                    }

                    currentEvent.Use();
                    break;
            }
        }

        private void DrawClipList()
        {
            EditorGUILayout.LabelField($"Clips ({_clips.Count})", KEditorUtility.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(200f));

            int removeIndex = -1;
            for (int i = 0; i < _clips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(_clips[i], typeof(AnimationClip), false);

                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (removeIndex >= 0)
            {
                _clips.RemoveAt(removeIndex);
            }
        }

        private void ProcessStrideCurves()
        {
            _statusMessage = string.Empty;
            _sampleRate = Mathf.Max(MinSampleRate, _sampleRate);
            _footHeightOffset = Mathf.Max(0f, _footHeightOffset);

            GameObject instance = null;
            int processed = 0;
            int skipped = 0;

            var fbxCurveCache = new Dictionary<string, Dictionary<string, List<CustomEditorCurveData>>>();

            try
            {
                instance = CreateModelInstance(_characterModel);
                if (instance == null)
                {
                    _statusMessage = "Could not instantiate the character model.";
                    return;
                }

                if (!TryGetTrackedTransforms(instance.transform, out var componentRoot, out var leftFoot,
                        out var rightFoot))
                {
                    _statusMessage = "Could not find left/right foot bones on the character model.";
                    return;
                }

                int count = _clips.Count;
                for (int i = 0; i < count; i++)
                {
                    var clip = _clips[i];
                    if (clip == null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        EditorUtility.DisplayProgressBar("CAS Stride Curves", $"Processing: {clip.name}",
                            (i + 1f) / count);

                        StrideCurveSet curveSet = ComputeStrideCurves(clip, instance, componentRoot, leftFoot,
                            rightFoot, _sampleRate, _footHeightOffset);

                        ApplyCurvesToClip(clip, curveSet);
                        TrackFbxCurveData(clip, curveSet, fbxCurveCache);
                        processed++;
                    }
                    catch (Exception clipException)
                    {
                        Debug.LogWarning(
                            $"CAS Stride Curves Tool: Failed processing '{clip.name}'. {clipException.Message}", clip);
                        skipped++;
                    }
                }

                int updatedImporters = ApplyFbxCurveMetadata(fbxCurveCache);
                AssetDatabase.SaveAssets();

                _statusMessage =
                    $"Processed: {processed} clips. Skipped: {skipped}. Updated FBX importers: {updatedImporters}.";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _statusMessage = "Stride curve processing failed. Check Console for details.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static GameObject CreateModelInstance(GameObject model)
        {
            if (model == null) return null;

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

        private static StrideCurveSet ComputeStrideCurves(AnimationClip clip, GameObject modelInstance,
            Transform componentRoot, Transform leftFoot, Transform rightFoot, float sampleRate,
            float footHeightOffset)
        {
            float endTime = GetCurveEndTime(clip);
            StrideCurveSet curveSet = CreateDefaultCurveSet(endTime);

            if (clip == null || modelInstance == null || componentRoot == null || leftFoot == null || rightFoot == null)
            {
                return curveSet;
            }

            SampleFeet(clip, modelInstance, componentRoot, leftFoot, rightFoot, sampleRate, out var leftSamples,
                out var rightSamples);

            bool allowWrap = clip.isLooping && clip.length > Mathf.Epsilon;
            FootAnalysisResult leftAnalysis = AnalyzeFoot(leftSamples, clip.length, footHeightOffset, allowWrap);
            FootAnalysisResult rightAnalysis = AnalyzeFoot(rightSamples, clip.length, footHeightOffset, allowWrap);

            float strideSpeed = Mathf.Max(leftAnalysis.strideSpeed, rightAnalysis.strideSpeed);
            strideSpeed = strideSpeed > 0.001f ? strideSpeed : 0f;
            curveSet.strideSpeed = AnimationCurve.Constant(0f, endTime, strideSpeed);
            curveSet.strideWeight = AnimationCurve.Constant(0f, endTime, strideSpeed > 0.001f ? 1f : 0f);
            Vector3 strideDirection = ComputeStrideDirection(leftAnalysis.movementVector, rightAnalysis.movementVector);
            curveSet.strideX = AnimationCurve.Constant(0f, endTime, strideDirection.x);
            curveSet.strideY = AnimationCurve.Constant(0f, endTime, strideDirection.z);

            return curveSet;
        }

        private static StrideCurveSet CreateDefaultCurveSet(float endTime)
        {
            return new StrideCurveSet
            {
                strideSpeed = AnimationCurve.Constant(0f, endTime, 0f),
                strideWeight = AnimationCurve.Constant(0f, endTime, 0f),
                strideX = AnimationCurve.Constant(0f, endTime, 0f),
                strideY = AnimationCurve.Constant(0f, endTime, 0f)
            };
        }

        private static void SampleFeet(AnimationClip clip, GameObject modelInstance, Transform componentRoot,
            Transform leftFoot, Transform rightFoot, float sampleRate, out FootSample[] leftSamples,
            out FootSample[] rightSamples)
        {
            leftSamples = Array.Empty<FootSample>();
            rightSamples = Array.Empty<FootSample>();

            if (clip == null || modelInstance == null || componentRoot == null || leftFoot == null || rightFoot == null)
            {
                return;
            }

            float clipLength = Mathf.Max(0f, clip.length);
            if (clipLength <= Mathf.Epsilon)
            {
                clip.SampleAnimation(modelInstance, 0f);

                leftSamples = new[]
                {
                    new FootSample {time = 0f, localPosition = componentRoot.InverseTransformPoint(leftFoot.position)}
                };

                rightSamples = new[]
                {
                    new FootSample {time = 0f, localPosition = componentRoot.InverseTransformPoint(rightFoot.position)}
                };

                return;
            }

            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(clipLength * sampleRate) + 1);
            leftSamples = new FootSample[sampleCount];
            rightSamples = new FootSample[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float sampleTime = i == sampleCount - 1 ? clipLength : Mathf.Min(clipLength, i / sampleRate);
                clip.SampleAnimation(modelInstance, sampleTime);

                leftSamples[i] = new FootSample()
                {
                    time = sampleTime,
                    localPosition = componentRoot.InverseTransformPoint(leftFoot.position)
                };

                rightSamples[i] = new FootSample()
                {
                    time = sampleTime,
                    localPosition = componentRoot.InverseTransformPoint(rightFoot.position)
                };
            }
        }

        private static FootAnalysisResult AnalyzeFoot(FootSample[] samples, float clipLength, float footHeightOffset,
            bool allowWrap)
        {
            FootAnalysisResult result = new FootAnalysisResult()
            {
                strideSpeed = 0f,
                movementVector = Vector3.zero
            };

            if (samples == null || samples.Length == 0)
            {
                return result;
            }

            float minHeight = samples[0].localPosition.y;
            for (int i = 1; i < samples.Length; i++)
            {
                minHeight = Mathf.Min(minHeight, samples[i].localPosition.y);
            }

            float pinThreshold = minHeight + Mathf.Max(0f, footHeightOffset);
            var pinned = new bool[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                pinned[i] = samples[i].localPosition.y <= pinThreshold;
            }

            result.strideSpeed = ComputeMaxPinnedStrideSpeed(samples, pinned, clipLength, allowWrap);
            result.movementVector = ComputeAverageFootMovementVector(samples, pinned, clipLength, allowWrap);
            return result;
        }

        private static float ComputeMaxPinnedStrideSpeed(FootSample[] samples, bool[] pinned, float clipLength,
            bool allowWrap)
        {
            if (samples == null || pinned == null || samples.Length != pinned.Length || samples.Length < 2 ||
                clipLength <= Mathf.Epsilon)
            {
                return 0f;
            }

            List<FootContactInterval> intervals = CollectContactIntervals(pinned);
            if (intervals.Count == 0)
            {
                return 0f;
            }

            float maxSpeed = 0f;
            for (int i = 0; i < intervals.Count; i++)
            {
                maxSpeed = Mathf.Max(maxSpeed, ComputeContactIntervalSpeed(samples, intervals[i]));
            }

            if (allowWrap && intervals.Count > 1 && pinned[0] && pinned[pinned.Length - 1])
            {
                maxSpeed = Mathf.Max(maxSpeed, ComputeWrappedContactIntervalSpeed(samples, intervals[intervals.Count - 1],
                    intervals[0], clipLength));
            }

            return maxSpeed;
        }

        private static List<FootContactInterval> CollectContactIntervals(bool[] pinned)
        {
            var intervals = new List<FootContactInterval>();
            if (pinned == null || pinned.Length == 0)
            {
                return intervals;
            }

            int startIndex = -1;
            for (int i = 0; i < pinned.Length; i++)
            {
                if (pinned[i])
                {
                    if (startIndex < 0)
                    {
                        startIndex = i;
                    }

                    continue;
                }

                if (startIndex < 0)
                {
                    continue;
                }

                intervals.Add(new FootContactInterval()
                {
                    startIndex = startIndex,
                    lastPinnedIndex = i - 1,
                    exitIndex = i
                });

                startIndex = -1;
            }

            if (startIndex >= 0)
            {
                intervals.Add(new FootContactInterval()
                {
                    startIndex = startIndex,
                    lastPinnedIndex = pinned.Length - 1,
                    exitIndex = -1
                });
            }

            return intervals;
        }

        private static float ComputeContactIntervalSpeed(FootSample[] samples, FootContactInterval interval)
        {
            float exitTime = interval.exitIndex >= 0
                ? samples[interval.exitIndex].time
                : samples[interval.lastPinnedIndex].time;

            return ComputeHorizontalSpeed(samples[interval.startIndex].localPosition,
                samples[interval.lastPinnedIndex].localPosition, samples[interval.startIndex].time, exitTime);
        }

        private static float ComputeWrappedContactIntervalSpeed(FootSample[] samples, FootContactInterval lastInterval,
            FootContactInterval firstInterval, float clipLength)
        {
            float wrappedExitTime = firstInterval.exitIndex >= 0
                ? samples[firstInterval.exitIndex].time + clipLength
                : samples[firstInterval.lastPinnedIndex].time + clipLength;

            return ComputeHorizontalSpeed(samples[lastInterval.startIndex].localPosition,
                samples[firstInterval.lastPinnedIndex].localPosition, samples[lastInterval.startIndex].time,
                wrappedExitTime);
        }

        private static float ComputeHorizontalSpeed(Vector3 startLocal, Vector3 endLocal, float startTime, float endTime)
        {
            float duration = endTime - startTime;
            if (duration <= Mathf.Epsilon)
            {
                return 0f;
            }

            startLocal.y = 0f;
            endLocal.y = 0f;
            float distance = Vector3.Distance(startLocal, endLocal);
            return distance / duration;
        }

        private static Vector3 ComputeAverageFootMovementVector(FootSample[] samples, bool[] pinned, float clipLength,
            bool allowWrap)
        {
            if (samples == null || pinned == null || samples.Length != pinned.Length || samples.Length < 2)
            {
                return Vector3.zero;
            }

            List<FootContactInterval> intervals = CollectContactIntervals(pinned);
            if (intervals.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 accumulated = Vector3.zero;
            int vectorCount = 0;

            for (int i = 0; i < intervals.Count; i++)
            {
                if (intervals[i].exitIndex < 0)
                {
                    continue;
                }

                accumulated += ComputeFootMovementVector(samples, intervals[i]);
                vectorCount++;
            }

            if (allowWrap && intervals.Count > 1 && pinned[0] && pinned[pinned.Length - 1] && clipLength > Mathf.Epsilon)
            {
                accumulated += ComputeWrappedFootMovementVector(samples, intervals[intervals.Count - 1], intervals[0]);
                vectorCount++;
            }

            if (vectorCount == 0)
            {
                return Vector3.zero;
            }

            Vector3 averageMovement = accumulated / vectorCount;
            averageMovement.y = 0f;
            return averageMovement;
        }

        private static Vector3 ComputeFootMovementVector(FootSample[] samples, FootContactInterval interval)
        {
            Vector3 pinnedPoint = samples[interval.lastPinnedIndex].localPosition;
            Vector3 unpinnedPoint = samples[interval.exitIndex].localPosition;
            return pinnedPoint - unpinnedPoint;
        }

        private static Vector3 ComputeWrappedFootMovementVector(FootSample[] samples, FootContactInterval lastInterval,
            FootContactInterval firstInterval)
        {
            Vector3 pinnedPoint = samples[lastInterval.lastPinnedIndex].localPosition;
            Vector3 unpinnedPoint = samples[firstInterval.exitIndex].localPosition;
            return pinnedPoint - unpinnedPoint;
        }

        private static Vector3 ComputeStrideDirection(Vector3 leftMovementVector, Vector3 rightMovementVector)
        {
            Vector3 movementVector = (leftMovementVector + rightMovementVector) * 0.5f;
            movementVector.y = 0f;

            if (movementVector.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            float angle = Vector3.SignedAngle(Vector3.forward, movementVector.normalized, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
        }

        private static float GetCurveEndTime(AnimationClip clip)
        {
            if (clip == null)
            {
                return 1f / 30f;
            }

            float frameRate = clip.frameRate > Mathf.Epsilon ? clip.frameRate : 30f;
            return Mathf.Max(clip.length, 1f / frameRate);
        }

        private static void ApplyCurvesToClip(AnimationClip clip, StrideCurveSet curveSet)
        {
            if (clip == null)
            {
                return;
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path.Length > 0 || binding.type != typeof(Animator) ||
                    !IsStrideToolCurveName(binding.propertyName))
                {
                    continue;
                }

                clip.SetCurve(string.Empty, typeof(Animator), binding.propertyName, null);
            }

            foreach (CustomEditorCurveData curveData in CreateCurveDataList(curveSet))
            {
                clip.SetCurve(string.Empty, typeof(Animator), curveData.propertyName, curveData.curve);
            }

            EditorUtility.SetDirty(clip);
        }

        private static List<CustomEditorCurveData> CreateCurveDataList(StrideCurveSet curveSet)
        {
            return new List<CustomEditorCurveData>()
            {
                CreateCurveData(StrideSpeedCurveName, curveSet.strideSpeed),
                CreateCurveData(StrideWeightCurveName, curveSet.strideWeight),
                CreateCurveData(StrideXCurveName, curveSet.strideX),
                CreateCurveData(StrideYCurveName, curveSet.strideY)
            };
        }

        private static CustomEditorCurveData CreateCurveData(string propertyName, AnimationCurve curve)
        {
            return new CustomEditorCurveData()
            {
                curve = curve ?? new AnimationCurve(),
                propertyName = propertyName,
                targetTypeName = typeof(Animator).AssemblyQualifiedName
            };
        }

        private static bool TryGetTrackedTransforms(Transform root, out Transform componentRoot, out Transform leftFoot,
            out Transform rightFoot)
        {
            componentRoot = root;
            leftFoot = null;
            rightFoot = null;
            Transform searchRoot = root;

            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                componentRoot = animator.transform;
                searchRoot = animator.transform;

                if (animator.isHuman)
                {
                    leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

                    if (leftFoot == null)
                    {
                        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                    }

                    if (rightFoot == null)
                    {
                        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightToes);
                    }
                }
            }

            if (leftFoot == null)
            {
                leftFoot = FindBoneByQueries(searchRoot, LeftFootQueryNames);
            }

            if (rightFoot == null)
            {
                rightFoot = FindBoneByQueries(searchRoot, RightFootQueryNames);
            }

            return componentRoot != null && leftFoot != null && rightFoot != null;
        }

        private static Transform FindBoneByQueries(Transform root, IReadOnlyList<string> queryNames)
        {
            if (root == null || queryNames == null || queryNames.Count == 0)
            {
                return null;
            }

            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            var boneNames = new string[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                boneNames[i] = bones[i].name.ToLowerInvariant();
            }

            for (int queryIndex = 0; queryIndex < queryNames.Count; queryIndex++)
            {
                string query = queryNames[queryIndex];
                if (string.IsNullOrEmpty(query))
                {
                    continue;
                }

                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    if (string.Equals(boneNames[boneIndex], query, StringComparison.Ordinal))
                    {
                        return bones[boneIndex];
                    }
                }
            }

            for (int queryIndex = 0; queryIndex < queryNames.Count; queryIndex++)
            {
                string query = queryNames[queryIndex];
                if (string.IsNullOrEmpty(query))
                {
                    continue;
                }

                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    if (boneNames[boneIndex].EndsWith(query, StringComparison.Ordinal))
                    {
                        return bones[boneIndex];
                    }
                }
            }

            return null;
        }

        private void AddClipsFromObject(Object asset)
        {
            if (asset == null) return;

            if (asset is AnimationClip directClip)
            {
                AddClip(directClip);
                return;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(path))
            {
                Object[] nestedAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                bool addedAny = false;

                foreach (var nestedAsset in nestedAssets)
                {
                    if (!(nestedAsset is AnimationClip nestedClip)) continue;

                    AddClip(nestedClip);
                    addedAny = true;
                }

                if (!addedAny)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    AddClip(clip);
                }

                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] {path});
            foreach (var guid in guids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                AddClip(clip);
            }
        }

        private void AddClip(AnimationClip clip)
        {
            if (!IsClipValid(clip) || _clips.Contains(clip))
            {
                return;
            }

            _clips.Add(clip);
        }

        private static bool IsClipValid(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            // Keep the same hidden-clip filtering behavior as KEditorUtility.GetAnimationClipFromSelection.
            if ((clip.hideFlags & HideFlags.HideInHierarchy) != 0)
            {
                return false;
            }

            // Extra guard for preview clips that may still appear in selection contexts.
            if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static void TrackFbxCurveData(AnimationClip clip, StrideCurveSet curveSet,
            Dictionary<string, Dictionary<string, List<CustomEditorCurveData>>> cache)
        {
            if (clip == null) return;

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!KEditorUtility.IsSubAsset(clip))
            {
                return;
            }

            if (!cache.TryGetValue(path, out var clipMap))
            {
                clipMap = new Dictionary<string, List<CustomEditorCurveData>>();
                cache.Add(path, clipMap);
            }

            clipMap[clip.name] = CreateCurveDataList(curveSet);
        }

        private static int ApplyFbxCurveMetadata(
            Dictionary<string, Dictionary<string, List<CustomEditorCurveData>>> cache)
        {
            int updatedImporters = 0;

            foreach (var entry in cache)
            {
                string path = entry.Key;
                var clipCurves = entry.Value;
                if (clipCurves == null || clipCurves.Count == 0)
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                var properties = importer.extraUserProperties != null
                    ? new List<string>(importer.extraUserProperties)
                    : new List<string>();

                var targetClipNames = new HashSet<string>(clipCurves.Keys);
                properties.RemoveAll(property => ShouldRemoveStrideCurveProperty(property, targetClipNames));

                foreach (var clipEntry in clipCurves)
                {
                    for (int i = 0; i < clipEntry.Value.Count; i++)
                    {
                        CustomEditorCurveData curveData = clipEntry.Value[i];
                        string serialized = JsonUtility.ToJson(curveData);
                        properties.Add($"{CurveProcessorUtility.CustomCurvePrefix}~{clipEntry.Key}~{serialized}");
                    }

                }

                importer.importAnimatedCustomProperties = true;
                importer.extraUserProperties = properties.ToArray();
                importer.SaveAndReimport();
                updatedImporters++;
            }

            return updatedImporters;
        }

        private static bool ShouldRemoveStrideCurveProperty(string property, HashSet<string> targetClipNames)
        {
            if (!TryParseCasCurveProperty(property, out var clipName, out var curveData))
            {
                return false;
            }

            if (!targetClipNames.Contains(clipName))
            {
                return false;
            }

            return string.Equals(curveData.targetTypeName, typeof(Animator).AssemblyQualifiedName,
                       StringComparison.Ordinal) &&
                   IsStrideToolCurveName(curveData.propertyName);
        }

        private static bool IsStrideToolCurveName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            for (int i = 0; i < GeneratedCurveNames.Length; i++)
            {
                if (string.Equals(propertyName, GeneratedCurveNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return propertyName.StartsWith("Stride", StringComparison.Ordinal);
        }

        private static bool TryParseCasCurveProperty(string property, out string clipName,
            out CustomEditorCurveData curveData)
        {
            clipName = string.Empty;
            curveData = new CustomEditorCurveData();

            if (string.IsNullOrEmpty(property) ||
                !property.StartsWith(CurveProcessorUtility.CustomCurvePrefix + "~", StringComparison.Ordinal))
            {
                return false;
            }

            string[] query = property.Split(new[] {'~'}, 3);
            if (query.Length < 3)
            {
                return false;
            }

            clipName = query[1];
            if (string.IsNullOrEmpty(clipName) || string.IsNullOrEmpty(query[2]))
            {
                return false;
            }

            curveData = JsonUtility.FromJson<CustomEditorCurveData>(query[2]);
            return !string.IsNullOrEmpty(curveData.propertyName);
        }

        public string GetToolCategory()
        {
            return "Animation/CAS";
        }

        public string GetToolName()
        {
            return "Stride Curves";
        }

        public string GetDocsURL()
        {
            return string.Empty;
        }

        public string GetToolDescription()
        {
            return
                "Computes StrideSpeed, StrideWeight, StrideX and StrideY curves for clips and preserves them on FBX reimport.";
        }
    }
}