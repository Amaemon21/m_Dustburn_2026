// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;
using UnityEngine.Animations;

namespace KINEMATION.FPSAnimationPack.Scripts.Player
{
    public struct FPSProceduralJob : IAnimationJob
    {
        public float ikWeight;
        public float adsWeight;
        
        // Bone references.
        public IKTransforms rightArm;
        public IKTransforms leftArm;
        public Transform skeletonRoot;
        public Transform weaponBone;
        public Transform weaponBoneAdditive;
        
        // Ads.
        public Transform aimPointTransform;
        public KTransform additiveAdsPose;
        public Transform cameraSocket;

        public KTransform defaultRightHandPose;
        public FPSWeaponSettings gunSettings;
        public RecoilAnimation recoilAnimation;
        public Animator animator;
        
        // Handles.
        private TransformSceneHandle _rootHandle;
        private TransformStreamHandle _skeletonRootHandle;
        private TransformSceneHandle _cameraSocketHandle;
        
        private TransformStreamHandle _weaponBoneHandle;
        private TransformStreamHandle _weaponBoneAdditiveHandle;
        private TransformStreamHandle _rightClavicleHandle;
        private TransformStreamHandle _leftClavicleHandle;
        
        private static int RIGHT_HAND_WEIGHT = Animator.StringToHash("RightHandWeight");
        private static int TAC_SPRINT_WEIGHT = Animator.StringToHash("TacSprintWeight");
        private static int GRENADE_WEIGHT = Animator.StringToHash("GrenadeWeight");
        private static Quaternion AnimatedOffset = Quaternion.Euler(90f, 0f, 0f);

        private float _rightHandWeight;
        private float _tacSprintWeight;
        private float _grenadeWeight;

        private KTransform _aimPoint;
        private KTransform _recoil;
        
        private float _ikMotionPlayBack;
        private KTransform _ikMotion;
        private KTransform _cachedIkMotion;
        private IKMotion _activeMotion;

        private bool _hasValidSettingsAsset;

        public void Setup()
        {
            _rootHandle = animator.BindSceneTransform(animator.transform);
            _skeletonRootHandle = animator.BindStreamTransform(skeletonRoot);
            if(cameraSocket != null) _cameraSocketHandle = animator.BindSceneTransform(cameraSocket);
            
            _weaponBoneHandle = animator.BindStreamTransform(weaponBone);
            _weaponBoneAdditiveHandle = animator.BindStreamTransform(weaponBoneAdditive);
            
            rightArm.Initialize(animator);
            leftArm.Initialize(animator);
            
            _recoil = _ikMotion = _cachedIkMotion = KTransform.Identity;
            ikWeight = 1f;
        }

        public void Update()
        {
            _hasValidSettingsAsset = gunSettings != null;

            if (weaponBone != null && aimPointTransform != null && _hasValidSettingsAsset)
            {
                _aimPoint.position = -weaponBone.InverseTransformPoint(aimPointTransform.position);
                _aimPoint.position -= gunSettings.aimPointOffset;
                _aimPoint.rotation = Quaternion.Inverse(weaponBone.rotation) * aimPointTransform.rotation;
            }
            else
            {
                _aimPoint = KTransform.Identity;
            }

            if (recoilAnimation != null)
            {
                _recoil.rotation = recoilAnimation.OutRot;
                _recoil.position = recoilAnimation.OutLoc;
            }

            UpdateIkMotion();
            
            if (animator != null)
            {
                _rightHandWeight = animator.GetFloat(RIGHT_HAND_WEIGHT);
                _grenadeWeight = animator.GetFloat(GRENADE_WEIGHT);
                _tacSprintWeight = animator.GetFloat(TAC_SPRINT_WEIGHT);
            }
        }
        
        public void PlayIkMotion(IKMotion newMotion)
        {
            _ikMotionPlayBack = 0f;
            _cachedIkMotion = _ikMotion;
            _activeMotion = newMotion;
        }

        private void UpdateIkMotion()
        {
            if (_activeMotion == null) return;
            
            _ikMotionPlayBack = Mathf.Clamp(_ikMotionPlayBack + _activeMotion.playRate * Time.deltaTime, 0f, 
                _activeMotion.GetLength());

            Vector3 positionTarget = _activeMotion.translationCurves.GetValue(_ikMotionPlayBack);
            positionTarget.x *= _activeMotion.translationScale.x;
            positionTarget.y *= _activeMotion.translationScale.y;
            positionTarget.z *= _activeMotion.translationScale.z;

            Vector3 rotationTarget = _activeMotion.rotationCurves.GetValue(_ikMotionPlayBack);
            rotationTarget.x *= _activeMotion.rotationScale.x;
            rotationTarget.y *= _activeMotion.rotationScale.y;
            rotationTarget.z *= _activeMotion.rotationScale.z;

            _ikMotion.position = positionTarget;
            _ikMotion.rotation = Quaternion.Euler(rotationTarget);

            if (!Mathf.Approximately(_activeMotion.blendTime, 0f))
            {
                _ikMotion = KTransform.Lerp(_cachedIkMotion, _ikMotion,
                    _ikMotionPlayBack / _activeMotion.blendTime);
            }
        }

        private void SolveTwoBoneIK(AnimationStream stream, IKTransforms ikTransforms, KTransform target)
        {
            KTwoBoneIkData data = new KTwoBoneIkData()
            {
                target = target,
                tip = KAnimationMath.GetTransform(stream, ikTransforms.tipHandle),
                mid = KAnimationMath.GetTransform(stream, ikTransforms.midHandle),
                root = KAnimationMath.GetTransform(stream, ikTransforms.rootHandle),
                posWeight = ikWeight,
                rotWeight = ikWeight
            };
            
            KTwoBoneIK.Solve(ref data);

            ikTransforms.rootHandle.SetRotation(stream, data.root.rotation);
            ikTransforms.midHandle.SetRotation(stream, data.mid.rotation);
            ikTransforms.tipHandle.SetRotation(stream, data.tip.rotation);
        }
        
        private KTransform GetWeaponPose(AnimationStream stream)
        {
            KTransform weaponBoneTransform = KAnimationMath.GetTransform(stream, _weaponBoneHandle);
            KTransform rightHandTransform = KAnimationMath.GetTransform(stream, rightArm.tipHandle);
            
            KTransform defaultWorldPose = rightHandTransform.GetWorldTransform(defaultRightHandPose, false);
            return KTransform.Lerp(weaponBoneTransform, defaultWorldPose, _rightHandWeight);
        }
        
        private void ProcessOffsets(AnimationStream stream, ref KTransform weaponT)
        {
            if (!_hasValidSettingsAsset) return;
            
            KTransform root = KAnimationMath.GetTransform(stream, _rootHandle);
            var weaponOffset = gunSettings.ikOffset;

            float mask = 1f - _tacSprintWeight;
            weaponT.position = KAnimationMath.MoveInSpace(root, weaponT, weaponOffset, mask);

            KAnimationMath.MoveInSpace(stream, _rootHandle, rightArm.rootHandle,
                gunSettings.rightClavicleOffset, mask);
            KAnimationMath.MoveInSpace(stream, _rootHandle, leftArm.rootHandle,
                gunSettings.leftClavicleOffset, mask);
        }
        
        private void ProcessAds(AnimationStream stream, ref KTransform weaponT)
        {
            if (!_hasValidSettingsAsset) return;
            
            var weaponOffset = gunSettings.ikOffset;
            var adsPose = weaponT;

            KTransform root = KAnimationMath.GetTransform(stream, _rootHandle);
            adsPose.position = KAnimationMath.MoveInSpace(root, adsPose, 
                additiveAdsPose.position - weaponOffset, 1f);
            adsPose.rotation = KAnimationMath.RotateInSpace(root, adsPose, 
                additiveAdsPose.rotation, 1f);

            KTransform aimTarget = KAnimationMath.GetTransform(stream, _cameraSocketHandle);
            
            float adsBlendWeight = gunSettings.adsBlend;
            adsPose.position = Vector3.Lerp(aimTarget.position, adsPose.position, adsBlendWeight);
            adsPose.rotation = Quaternion.Slerp(root.rotation, adsPose.rotation, adsBlendWeight);

            adsPose.position = KAnimationMath.MoveInSpace(root, adsPose, _aimPoint.rotation * _aimPoint.position, 1f);
            adsPose.rotation = KAnimationMath.RotateInSpace(root, adsPose, _aimPoint.rotation, 1f);

            float weight = KCurves.EaseSine(0f, 1f, adsWeight);
            
            weaponT.position = Vector3.Lerp(weaponT.position, adsPose.position, weight);
            weaponT.rotation = Quaternion.Slerp(weaponT.rotation, adsPose.rotation, weight);
        }
        
        private void ProcessAdditives(AnimationStream stream, ref KTransform weaponT)
        {
            KTransform root = KAnimationMath.GetTransform(stream, _skeletonRootHandle);

            KTransform additive = KAnimationMath.GetTransform(stream, _weaponBoneAdditiveHandle);
            additive = root.GetRelativeTransform(additive, false);
            
            float weight = Mathf.Lerp(1f, 0.3f, adsWeight) * (1f - _grenadeWeight);
            
            weaponT.position = KAnimationMath.MoveInSpace(root, weaponT, additive.position, weight);
            weaponT.rotation = KAnimationMath.RotateInSpace(root, weaponT, additive.rotation, weight);
        }
        
        private void ProcessIkMotion(AnimationStream stream, ref KTransform weaponT)
        {
            KTransform root = KAnimationMath.GetTransform(stream, _rootHandle);
            weaponT.position = KAnimationMath.MoveInSpace(root, weaponT, _ikMotion.position, 1f);
            weaponT.rotation = KAnimationMath.RotateInSpace(root, weaponT, _ikMotion.rotation, 1f);
        }
        
        private void ProcessRecoil(AnimationStream stream, ref KTransform weaponT)
        {
            KTransform root = KAnimationMath.GetTransform(stream, _rootHandle);
            weaponT.position = KAnimationMath.MoveInSpace(root, weaponT, _recoil.position, 1f);
            weaponT.rotation = KAnimationMath.RotateInSpace(root, weaponT, _recoil.rotation, 1f);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (_hasValidSettingsAsset)
            {
                KAnimationMath.RotateInSpace(stream, _rootHandle, rightArm.tipHandle, gunSettings.rightHandSprintOffset,
                    _tacSprintWeight);
            }

            KTransform weaponTransform = GetWeaponPose(stream);

            weaponTransform.rotation = KAnimationMath.RotateInSpace(weaponTransform, weaponTransform,
                _hasValidSettingsAsset ? gunSettings.rotationOffset : AnimatedOffset, 1f);

            KTransform rightHandTarget = KAnimationMath.GetTransform(stream, rightArm.tipHandle);
            rightHandTarget = weaponTransform.GetRelativeTransform(rightHandTarget, false);
            
            KTransform leftHandTarget = KAnimationMath.GetTransform(stream, leftArm.tipHandle);
            leftHandTarget = weaponTransform.GetRelativeTransform(leftHandTarget, false);
            
            ProcessOffsets(stream, ref weaponTransform);
            ProcessAds(stream, ref weaponTransform);
            ProcessAdditives(stream, ref weaponTransform);
            ProcessIkMotion(stream, ref weaponTransform);
            ProcessRecoil(stream, ref weaponTransform);
            
            _weaponBoneHandle.SetPosition(stream, weaponTransform.position);
            _weaponBoneHandle.SetRotation(stream, weaponTransform.rotation);
            
            rightHandTarget = weaponTransform.GetWorldTransform(rightHandTarget, false);
            leftHandTarget = weaponTransform.GetWorldTransform(leftHandTarget, false);

            SolveTwoBoneIK(stream, rightArm, rightHandTarget);
            SolveTwoBoneIK(stream, leftArm, leftHandTarget);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
}