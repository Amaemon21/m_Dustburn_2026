// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

#if UNITY_EDITOR
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
#endif

namespace KINEMATION.RetargetPro.Runtime.Features.FPSRetargeting
{
    public class FPSRetargetFeatureState : RetargetFeatureState
    {
        private const float FpsScaleEpsilon = 0.000001f;

        protected KTransformChain _sourceRightArmChain;
        protected KTransformChain _sourceLeftArmChain;
        
        protected KTransformChain _targetRightArmChain;
        protected KTransformChain _targetLeftArmChain;
        
        protected KTransformChain _sourceWeaponChain;
        protected KTransformChain _targetWeaponChain;

        protected FPSRetargetFeature _asset;

        protected KTransform _rightHandIk = KTransform.Identity;
        protected KTransform _leftHandIk = KTransform.Identity;

        protected KTransform _rightPole = KTransform.Identity;
        protected KTransform _leftPole = KTransform.Identity;

        protected Vector3 _cachedTargetWeaponPosition;

        private float _fpsScale = 1f;
        private float _featureWeight = 1f;

        private static float GetFinalArmLength(KTransformChain armChain)
        {
            int count = armChain?.cachedTransforms?.Count ?? 0;
            if (count < 3) return 0f;

            Vector3 upperArm = armChain.cachedTransforms[count - 3].position;
            Vector3 forearm = armChain.cachedTransforms[count - 2].position;
            Vector3 hand = armChain.cachedTransforms[count - 1].position;
            return Vector3.Distance(upperArm, forearm) + Vector3.Distance(forearm, hand);
        }

        private static void CacheWeaponChainAtRootPose(KTransformChain weaponChain, Transform characterRoot)
        {
            if (weaponChain == null || characterRoot == null || weaponChain.transformChain.Count == 0)
            {
                return;
            }

            Transform weaponBone = weaponChain.transformChain[0];
            if (weaponBone == null)
            {
                return;
            }

            Vector3 localPosition = weaponBone.localPosition;
            Quaternion localRotation = weaponBone.localRotation;

            try
            {
                weaponBone.position = characterRoot.position;
                weaponBone.rotation = characterRoot.rotation;
                weaponChain.CacheTransforms(ESpaceType.ComponentSpace, characterRoot);
            }
            finally
            {
                weaponBone.localPosition = localPosition;
                weaponBone.localRotation = localRotation;
            }
        }
        
        protected void RetargetChains(KTransformChain source, KTransformChain target)
        {
            if (source == null || target == null) return;
            if (source.transformChain.Count != target.transformChain.Count) return;

            Quaternion inverseSourceRootRotation = Quaternion.Inverse(GetSourceRoot().rotation);
            Quaternion targetRootRotation = GetTargetRoot().rotation;
            int count = source.transformChain.Count;
            for (int i = 0; i < count; i++)
            {
                KTransform cachedSourcePose = source.cachedTransforms[i];
                KTransform cachedTargetPose = target.cachedTransforms[i];

                Quaternion sourceComponentRotation = inverseSourceRootRotation * source.transformChain[i].rotation;
                var baseRotation = targetRootRotation * target.cachedTransforms[i].rotation;

                // Compute the delta dynamically, as bone sizes might differ.
                Quaternion delta = Quaternion.Inverse(cachedSourcePose.rotation) * cachedTargetPose.rotation;
                var outRotation = targetRootRotation * sourceComponentRotation * delta;
                outRotation = Quaternion.Slerp(baseRotation, outRotation, _featureWeight);
                
                target.transformChain[i].rotation = outRotation;
            }
        }
        
        protected void ApplyIK(KTransformChain source, KTransformChain target, Vector3 effectorOffset, 
            ref KTransform ikTarget, Vector3 poleOffset, ref KTransform pole)
        {
            if (!IsArmChainValid(source, target)) return;

            RetargetChains(source, target);
            
            if (_sourceWeaponChain == null || _targetWeaponChain == null) return;
            if (_sourceWeaponChain.transformChain.Count != _targetWeaponChain.transformChain.Count) return;

            Transform sourceWeapon = _sourceWeaponChain.transformChain[0];
            Transform targetWeapon = _targetWeaponChain.transformChain[0];

            if (sourceWeapon == null || targetWeapon == null) return;

            Transform sourceRoot = GetSourceRoot();
            Transform targetRoot = GetTargetRoot();
            
            int count = source.transformChain.Count;
            
            Transform tip = target.transformChain[count - 1];
            Transform mid = target.transformChain[count - 2];
            Transform root = target.transformChain[count - 3];
            
            KTransform sourceWeaponComponent = new KTransform(
                sourceRoot.InverseTransformPoint(sourceWeapon.position),
                Quaternion.Inverse(sourceRoot.rotation) * sourceWeapon.rotation);
            KTransform sourceHandComponent = new KTransform(
                sourceRoot.InverseTransformPoint(source.transformChain[count - 1].position),
                Quaternion.Inverse(sourceRoot.rotation) * tip.rotation);
            KTransform sourceHandRelative = sourceWeaponComponent.GetRelativeTransform(sourceHandComponent, false);
            sourceHandRelative.position *= _fpsScale;

            KTransform targetWeaponComponent = new KTransform(
                targetRoot.InverseTransformPoint(targetWeapon.position),
                Quaternion.Inverse(targetRoot.rotation) * targetWeapon.rotation);
            ikTarget = targetWeaponComponent.GetWorldTransform(sourceHandRelative, false);
            ikTarget.position = targetRoot.TransformPoint(ikTarget.position);
            ikTarget.rotation = targetRoot.rotation * ikTarget.rotation;
            ikTarget.position = ikTarget.TransformPoint(effectorOffset, false);
            
            KTransform rootBone = new KTransform(targetRoot);
            
            pole = new KTransform(mid);
            pole.position = KAnimationMath.MoveInSpace(rootBone, pole, poleOffset, 1f);
            
            KTwoBoneIkData twoBoneIkData = new KTwoBoneIkData()
            {
                root = new KTransform(root),
                mid = new KTransform(mid),
                tip = new KTransform(tip),
                hint = pole,
                target = ikTarget,
                hasValidHint = true,
                rotWeight = _featureWeight,
                posWeight = _featureWeight,
                hintWeight = 1f
            };
            
            KTwoBoneIK.Solve(ref twoBoneIkData);

            root.rotation = twoBoneIkData.root.rotation;
            mid.rotation = twoBoneIkData.mid.rotation;
            tip.rotation = twoBoneIkData.tip.rotation;
        }

        private static bool IsArmChainValid(KTransformChain source, KTransformChain target)
        {
            return source != null && target != null && source.IsValid() && target.IsValid() &&
                   source.transformChain.Count >= 3 && source.transformChain.Count == target.transformChain.Count;
        }

        private static bool IsWeaponChainValid(KTransformChain chain)
        {
            if (chain == null || !chain.IsValid() || chain.transformChain.Count == 0) return false;
            Transform weapon = chain.transformChain[0];
            return weapon != null && weapon.GetComponent<Renderer>() == null;
        }

        public override bool IsValid()
        {
            return _asset != null &&
                   sourceRigComponent != null &&
                   targetRigComponent != null &&
                   FPSRetargetFeature.IsArmPairValid(_asset.sourceRightArm, _asset.targetRightArm) &&
                   FPSRetargetFeature.IsArmPairValid(_asset.sourceLeftArm, _asset.targetLeftArm) &&
                   IsArmChainValid(_sourceRightArmChain, _targetRightArmChain) &&
                   IsArmChainValid(_sourceLeftArmChain, _targetLeftArmChain) &&
                   FPSRetargetFeature.IsWeaponChainStructurallyValid(_asset.sourceWeapon) &&
                   FPSRetargetFeature.IsWeaponChainStructurallyValid(_asset.targetWeapon) &&
                   IsWeaponChainValid(_sourceWeaponChain) &&
                   IsWeaponChainValid(_targetWeaponChain);
        }

        protected override void Initialize(RetargetFeature newAsset)
        {
            _fpsScale = 1f;
            _asset = newAsset as FPSRetargetFeature;
            if (_asset == null) return;

            Transform sourceRoot = GetSourceRoot();
            Transform targetRoot = GetTargetRoot();

            // 1. Initialize all the bone chains.

            _sourceRightArmChain = RetargetUtility.GetTransformChain(sourceRigComponent, _asset.sourceRig, 
                _asset.sourceRightArm);
            _sourceLeftArmChain = RetargetUtility.GetTransformChain(sourceRigComponent, _asset.sourceRig, 
                _asset.sourceLeftArm);
            
            _targetRightArmChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, 
                _asset.targetRightArm);
            _targetLeftArmChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, 
                _asset.targetLeftArm);
            
            _sourceWeaponChain = RetargetUtility.GetTransformChain(sourceRigComponent, _asset.sourceRig, 
                _asset.sourceWeapon);
            _targetWeaponChain = RetargetUtility.GetTransformChain(targetRigComponent, _asset.targetRig, 
                _asset.targetWeapon);

            if (_sourceRightArmChain == null || !_sourceRightArmChain.IsValid())
            {
                return;
            }

            if (_sourceLeftArmChain == null || !_sourceLeftArmChain.IsValid())
            {
                return;
            }

            if (_targetRightArmChain == null || !_targetRightArmChain.IsValid())
            {
                return;
            }

            if (_targetLeftArmChain == null || !_targetLeftArmChain.IsValid())
            {
                return;
            }

            if (_sourceWeaponChain == null || !_sourceWeaponChain.IsValid())
            {
                return;
            }

            if (_targetWeaponChain == null || !_targetWeaponChain.IsValid())
            {
                return;
            }

            // 2. Cache the initial poses.

            Transform targetWeaponBone = _targetWeaponChain.transformChain[0];
            if (targetWeaponBone == null)
            {
                return;
            }

            _cachedTargetWeaponPosition = targetRoot.InverseTransformPoint(targetWeaponBone.position);

            CacheWeaponChainAtRootPose(_sourceWeaponChain, sourceRoot);
            _sourceRightArmChain.CacheTransforms(ESpaceType.ComponentSpace, sourceRoot);
            _sourceLeftArmChain.CacheTransforms(ESpaceType.ComponentSpace, sourceRoot);

            CacheWeaponChainAtRootPose(_targetWeaponChain, targetRoot);
            _targetRightArmChain.CacheTransforms(ESpaceType.ComponentSpace, targetRoot);
            _targetLeftArmChain.CacheTransforms(ESpaceType.ComponentSpace, targetRoot);

            float sourceRightArmLength = GetFinalArmLength(_sourceRightArmChain);
            float sourceLeftArmLength = GetFinalArmLength(_sourceLeftArmChain);
            float targetRightArmLength = GetFinalArmLength(_targetRightArmChain);
            float targetLeftArmLength = GetFinalArmLength(_targetLeftArmChain);

            if (sourceRightArmLength > FpsScaleEpsilon && sourceLeftArmLength > FpsScaleEpsilon &&
                targetRightArmLength > FpsScaleEpsilon && targetLeftArmLength > FpsScaleEpsilon)
            {
                float scale = (targetRightArmLength / sourceRightArmLength +
                               targetLeftArmLength / sourceLeftArmLength) * 0.5f;
                if (!float.IsNaN(scale) && !float.IsInfinity(scale))
                {
                    _fpsScale = scale;
                }
            }
        }
        
        public override void Retarget(float time = 0f)
        {
            _featureWeight = _asset.GetFeatureWeight(time);

            Transform sourceWeaponBone = _sourceWeaponChain.transformChain[0];
            Transform targetWeaponBone = _targetWeaponChain.transformChain[0];
            
            RetargetChains(_sourceWeaponChain, _targetWeaponChain);

            Vector3 cachedWeaponPosition = _cachedTargetWeaponPosition;
            Vector3 weaponPosition = GetSourceRoot().InverseTransformPoint(sourceWeaponBone.position) * _fpsScale;

            weaponPosition += _asset.weaponOffset;
            weaponPosition = Vector3.Lerp(cachedWeaponPosition, weaponPosition, _featureWeight);
            targetWeaponBone.position = GetTargetRoot().TransformPoint(weaponPosition);
            
            ApplyIK(_sourceRightArmChain, _targetRightArmChain, _asset.rightHandOffset, ref _rightHandIk, 
                _asset.rightPoleOffset, ref _rightPole);
            ApplyIK(_sourceLeftArmChain, _targetLeftArmChain, _asset.leftHandOffset, ref _leftHandIk, 
                _asset.leftPoleOffset, ref _leftPole);
        }

#if UNITY_EDITOR
        private static float GetSelectionHandleSize(Vector3 position)
        {
            return Mathf.Max(0.03f, HandleUtility.GetHandleSize(position) * 0.08f);
        }

        private static float SignedTriangleArea(Vector2 point, Vector2 vertex1, Vector2 vertex2)
        {
            return (point.x - vertex2.x) * (vertex1.y - vertex2.y) -
                   (vertex1.x - vertex2.x) * (point.y - vertex2.y);
        }

        private static bool IsPointInTriangle(Vector2 point, Vector2 vertex1, Vector2 vertex2, Vector2 vertex3)
        {
            if (Mathf.Abs(SignedTriangleArea(vertex1, vertex2, vertex3)) <= 0.001f)
            {
                return false;
            }

            float area1 = SignedTriangleArea(point, vertex1, vertex2);
            float area2 = SignedTriangleArea(point, vertex2, vertex3);
            float area3 = SignedTriangleArea(point, vertex3, vertex1);

            bool hasNegative = area1 < 0f || area2 < 0f || area3 < 0f;
            bool hasPositive = area1 > 0f || area2 > 0f || area3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static bool IsMouseInsideProjectedQuad(Vector2 mousePosition, Vector3 center, Vector3 axis1,
            Vector3 axis2)
        {
            Vector2 vertex1 = HandleUtility.WorldToGUIPoint(center - axis1 - axis2);
            Vector2 vertex2 = HandleUtility.WorldToGUIPoint(center + axis1 - axis2);
            Vector2 vertex3 = HandleUtility.WorldToGUIPoint(center + axis1 + axis2);
            Vector2 vertex4 = HandleUtility.WorldToGUIPoint(center - axis1 + axis2);

            return IsPointInTriangle(mousePosition, vertex1, vertex2, vertex3) ||
                   IsPointInTriangle(mousePosition, vertex1, vertex3, vertex4);
        }

        private static bool IsMouseInsideProjectedCube(Vector3 position, Quaternion rotation, float size)
        {
            Vector2 mousePosition = Event.current.mousePosition;
            float halfSize = size * 0.5f;

            Vector3 right = rotation * Vector3.right * halfSize;
            Vector3 up = rotation * Vector3.up * halfSize;
            Vector3 forward = rotation * Vector3.forward * halfSize;

            return IsMouseInsideProjectedQuad(mousePosition, position + forward, right, up) ||
                   IsMouseInsideProjectedQuad(mousePosition, position - forward, right, up) ||
                   IsMouseInsideProjectedQuad(mousePosition, position + right, forward, up) ||
                   IsMouseInsideProjectedQuad(mousePosition, position - right, forward, up) ||
                   IsMouseInsideProjectedQuad(mousePosition, position + up, right, forward) ||
                   IsMouseInsideProjectedQuad(mousePosition, position - up, right, forward);
        }

        private static void WireCubeHandleCap(int controlId, Vector3 position, Quaternion rotation, float size,
            EventType eventType)
        {
            switch (eventType)
            {
                case EventType.Layout:
                    float distance = IsMouseInsideProjectedCube(position, rotation, size)
                        ? 0f
                        : HandleUtility.DistanceToCube(position, rotation, size);
                    HandleUtility.AddControl(controlId, distance);
                    break;
                case EventType.Repaint:
                    Matrix4x4 originalMatrix = Handles.matrix;
                    try
                    {
                        Handles.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                        Handles.DrawWireCube(Vector3.zero, Vector3.one * size);
                    }
                    finally
                    {
                        Handles.matrix = originalMatrix;
                    }
                    break;
            }
        }

        private static bool RenderBoneSelectionCube(Vector3 position, Quaternion rotation, float size, bool isActive)
        {
            return !isActive && Handles.Button(position, rotation, size, size, WireCubeHandleCap);
        }

        private void SelectHandle(bool weapon, bool rightHand, bool leftHand, bool rightPole, bool leftPole)
        {
            Undo.RecordObject(_asset, "Select FPS Retarget Handle");
            _asset.enableWeaponHandle = weapon;
            _asset.enableRightHandHandle = rightHand;
            _asset.enableLeftHandle = leftHand;
            _asset.enableRightPole = rightPole;
            _asset.enableLeftPole = leftPole;
            EditorUtility.SetDirty(_asset);
        }

        private Vector3 RenderHandle(Transform space, Transform bone, out Vector3 handlePosition)
        {
            handlePosition = KTransformHandles.PositionHandle(bone.position, space.rotation, 
                KTransformHandles.PositionHandleSettings.Default);
            return space.InverseTransformPoint(handlePosition) - space.InverseTransformPoint(bone.position);
        }

        private Vector3 RenderPoleHandle(Transform space, KTransform bone, out Vector3 handlePosition)
        {
            handlePosition = KTransformHandles.PositionHandle(bone.position, space.rotation, 
                KTransformHandles.PositionHandleSettings.Default);
            return space.InverseTransformPoint(handlePosition) - space.InverseTransformPoint(bone.position);
        }
        
        private Vector3 RenderHandHandle(Transform space, KTransform bone, out Vector3 handlePosition)
        {
            handlePosition = KTransformHandles.PositionHandle(bone.position, space.rotation, 
                KTransformHandles.PositionHandleSettings.Default);
            Vector3 delta = bone.InverseTransformPoint(handlePosition, false);

            if (Mathf.Approximately(delta.magnitude, 0f)) delta = Vector3.zero;
            return delta;
        }

        private static void DrawTargetLine(Vector3 bonePosition, Vector3 targetPosition)
        {
            if ((targetPosition - bonePosition).sqrMagnitude <= 0.00000001f)
            {
                return;
            }

            Color originalColor = Handles.color;
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.65f);
            Handles.DrawDottedLine(bonePosition, targetPosition, 4f);
            Handles.color = originalColor;
        }
        
        protected override void OnBoneRenderingSceneGUI()
        {
            RenderSourceAndTargetBoneChains(_sourceWeaponChain, _targetWeaponChain, TargetBoneChainColor);
            RenderSourceAndTargetBoneChains(_sourceRightArmChain, _targetRightArmChain, TargetBoneChainColor);
            RenderSourceAndTargetBoneChains(_sourceLeftArmChain, _targetLeftArmChain, TargetBoneChainColor);
        }

        protected override void OnTransformHandlesSceneGUI()
        {
            Transform root = GetTargetRoot();
            Transform weapon = _targetWeaponChain.transformChain[0];
            Transform rightHand = _targetRightArmChain.transformChain[^1];
            Transform leftHand = _targetLeftArmChain.transformChain[^1];

            float weaponHandleSize = GetSelectionHandleSize(weapon.position);
            float rightHandHandleSize = GetSelectionHandleSize(rightHand.position);
            float leftHandHandleSize = GetSelectionHandleSize(leftHand.position);
            float rightPoleHandleSize = GetSelectionHandleSize(rightHand.parent.position);
            float leftPoleHandleSize = GetSelectionHandleSize(leftHand.parent.position);
            var color = Handles.color;
            Handles.color = Color.cyan;
            
            if (RenderBoneSelectionCube(weapon.position, root.rotation, weaponHandleSize, _asset.enableWeaponHandle))
            {
                SelectHandle(true, false, false, false, false);
            }
            
            if (RenderBoneSelectionCube(rightHand.position, weapon.rotation, rightHandHandleSize,
                    _asset.enableRightHandHandle))
            {
                SelectHandle(false, true, false, false, false);
            }
            
            if (RenderBoneSelectionCube(leftHand.position, weapon.rotation, leftHandHandleSize, _asset.enableLeftHandle))
            {
                SelectHandle(false, false, true, false, false);
            }
            
            if (RenderBoneSelectionCube(rightHand.parent.position, root.rotation, rightPoleHandleSize,
                    _asset.enableRightPole))
            {
                SelectHandle(false, false, false, true, false);
            }
            
            if (RenderBoneSelectionCube(leftHand.parent.position, root.rotation, leftPoleHandleSize,
                    _asset.enableLeftPole))
            {
                SelectHandle(false, false, false, false, true);
            }
            
            if (_asset.enableWeaponHandle)
            {
                Vector3 delta = RenderHandle(root, weapon, out Vector3 weaponHandlePosition);
                if (delta.sqrMagnitude > 0f)
                {
                    Undo.RecordObject(_asset, "Adjust FPS Weapon Goal");
                    _asset.weaponOffset += delta;
                    EditorUtility.SetDirty(_asset);
                }
                DrawTargetLine(weapon.position, weaponHandlePosition);
            }
            
            if (_asset.enableRightHandHandle)
            {
                Vector3 delta = RenderHandHandle(weapon, _rightHandIk, out Vector3 rightHandHandlePosition);
                if (delta.sqrMagnitude > 0f)
                {
                    Undo.RecordObject(_asset, "Adjust FPS Right Hand Goal");
                    _asset.rightHandOffset += delta;
                    EditorUtility.SetDirty(_asset);
                }
                DrawTargetLine(rightHand.position, rightHandHandlePosition);
            }

            if (_asset.enableLeftHandle)
            {
                Vector3 delta = RenderHandHandle(weapon, _leftHandIk, out Vector3 leftHandHandlePosition);
                if (delta.sqrMagnitude > 0f)
                {
                    Undo.RecordObject(_asset, "Adjust FPS Left Hand Goal");
                    _asset.leftHandOffset += delta;
                    EditorUtility.SetDirty(_asset);
                }
                DrawTargetLine(leftHand.position, leftHandHandlePosition);
            }

            if (_asset.enableRightPole)
            {
                Vector3 delta = RenderPoleHandle(root, _rightPole, out Vector3 rightPoleHandlePosition);
                if (delta.sqrMagnitude > 0f)
                {
                    Undo.RecordObject(_asset, "Adjust FPS Right Pole");
                    _asset.rightPoleOffset += delta;
                    EditorUtility.SetDirty(_asset);
                }
                DrawTargetLine(rightHand.parent.position, rightPoleHandlePosition);
            }
            
            if (_asset.enableLeftPole)
            {
                Vector3 delta = RenderPoleHandle(root, _leftPole, out Vector3 leftPoleHandlePosition);
                if (delta.sqrMagnitude > 0f)
                {
                    Undo.RecordObject(_asset, "Adjust FPS Left Pole");
                    _asset.leftPoleOffset += delta;
                    EditorUtility.SetDirty(_asset);
                }
                DrawTargetLine(leftHand.parent.position, leftPoleHandlePosition);
            }
            
            Handles.color = Color.white;
            if (!_asset.enableWeaponHandle) Handles.Label(weapon.position, "Gun Goal");
            if (!_asset.enableRightHandHandle) Handles.Label(rightHand.position, "Right Hand Goal");
            if (!_asset.enableLeftHandle) Handles.Label(leftHand.position, "Left Hand Goal");
            
            if (!_asset.enableRightPole) Handles.Label(rightHand.parent.position, "Right Pole");
            if (!_asset.enableLeftPole) Handles.Label(leftHand.parent.position, "Left Pole");
            Handles.color = color;
        }
#endif
    }
}
