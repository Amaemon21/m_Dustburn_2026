// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KINEMATION.FPSAnimationPack.Scripts.Player
{
    [Serializable]
    public struct IKTransforms
    {
        public Transform tip;
        public Transform mid;
        public Transform root;

        [NonSerialized] public TransformStreamHandle tipHandle;
        [NonSerialized] public TransformStreamHandle midHandle;
        [NonSerialized] public TransformStreamHandle rootHandle;

        public void Initialize(Animator animator)
        {
            tipHandle = animator.BindStreamTransform(tip);
            midHandle = animator.BindStreamTransform(mid);
            rootHandle = animator.BindStreamTransform(root);
        }
    }
    
    [AddComponentMenu("KINEMATION/FPS Animation Pack/Character/FPS Player")]
    public class FPSPlayer : MonoBehaviour
    {
        [Range(0f, 1f)] public float ikWeight = 1f;
        public float AdsWeight => _adsWeight;
        
        public FPSPlayerSettings playerSettings;
        
        [Header("Skeleton")]
        [SerializeField] private Transform skeletonRoot;
        [SerializeField] private Transform weaponBone;
        [SerializeField] private Transform weaponBoneAdditive;
        [SerializeField] private Transform cameraPoint;
        [SerializeField] private IKTransforms rightHand;
        [SerializeField] private IKTransforms leftHand;
        
        private KTwoBoneIkData _rightHandIk;
        private KTwoBoneIkData _leftHandIk;
        
        private RecoilAnimation _recoilAnimation;
        private float _adsWeight;

        private List<FPSWeapon> _weapons = new List<FPSWeapon>();
        private List<FPSWeapon> _prefabComponents = new List<FPSWeapon>();
        private int _activeWeaponIndex = 0;

        private Animator _animator;

        private static int RIGHT_HAND_WEIGHT = Animator.StringToHash("RightHandWeight");
        private static int TAC_SPRINT_WEIGHT = Animator.StringToHash("TacSprintWeight");
        private static int GRENADE_WEIGHT = Animator.StringToHash("GrenadeWeight");
        private static int THROW_GRENADE = Animator.StringToHash("ThrowGrenade");
        private static int GAIT = Animator.StringToHash("Gait");
        private static int IS_IN_AIR = Animator.StringToHash("IsInAir");
        private static int INSPECT = Animator.StringToHash("Inspect");
        
        private int _tacSprintLayerIndex;
        private int _triggerDisciplineLayerIndex;
        private int _rightHandLayerIndex;
        
        private bool _isAiming;

        private Vector2 _moveInput;
        private float _smoothGait;

        private Vector2 _lookInput;

        private bool _bSprinting;
        private bool _bTacSprinting;

        private FPSPlayerSound _playerSound;

        private KTransform _localCameraPoint;
        private CharacterController _controller;

        private FPSProceduralJob _job;
        private AnimationScriptPlayable _playable;
        
        private void EquipWeapon_Incremental()
        {
            GetActiveWeapon().gameObject.SetActive(false);
            _activeWeaponIndex = _activeWeaponIndex + 1 > _weapons.Count - 1 ? 0 : _activeWeaponIndex + 1;
            EquipWeapon();
        }

        private void EquipWeapon(bool fastEquip = false, bool equipImmediately = false)
        {
            if (equipImmediately)
            {
                GetActiveWeapon().OnEquipped_Immediate();
            }
            else
            {
                GetActiveWeapon().OnEquipped(fastEquip);
            }

            _job.defaultRightHandPose = GetActiveWeapon().rightHandPose;
            _job.additiveAdsPose = GetActiveWeapon().adsPose;
            _job.gunSettings = GetActiveWeapon().weaponSettings;
            _job.aimPointTransform = GetActiveWeapon().aimPoint;
            _playable.SetJobData(_job);

            if (equipImmediately) return;
            
            Invoke(nameof(SetWeaponVisible), 0.05f);
        }

        private void FastEquipWeapon()
        {
            EquipWeapon(true);
        }

        private void ThrowGrenade()
        {
            GetActiveWeapon().gameObject.SetActive(false);
            Invoke(nameof(FastEquipWeapon), playerSettings.grenadeDelay);
        }

        private void OnLand()
        {
            _animator.SetBool(IS_IN_AIR, false);
        }

        public void OnThrowGrenade()
        {
            _animator.SetTrigger(THROW_GRENADE);
            Invoke(nameof(ThrowGrenade), GetActiveWeapon().UnEquipDelay);
        }

        public void OnChangeWeapon()
        {
            if (_weapons.Count <= 1) return;
            float delay = GetActiveWeapon().OnUnEquipped();
            Invoke(nameof(EquipWeapon_Incremental), delay);
        }

        public void OnChangeFireMode()
        {
            var prevFireMode = GetActiveWeapon().ActiveFireMode;
            GetActiveWeapon().OnFireModeChange();

            if (prevFireMode != GetActiveWeapon().ActiveFireMode)
            {
                _playerSound.PlayFireModeSwitchSound();
                _job.PlayIkMotion(playerSettings.fireModeMotion);
            }
        }
        
        public void OnReload()
        {
            GetActiveWeapon().OnReload();
        }
        
        public void OnJump()
        {
            _animator.SetBool(IS_IN_AIR, true);
            Invoke(nameof(OnLand), 0.4f);
        }
        
        public void OnInspect()
        {
            _animator.CrossFade(INSPECT, 0.1f);
        }
        
#if ENABLE_INPUT_SYSTEM
        public void OnMouseWheel(InputValue value)
        {
            float mouseWheelValue = value.Get<float>();
            if (mouseWheelValue == 0f) return;
            
            GetActiveWeapon().gameObject.SetActive(false);
            
            _activeWeaponIndex += mouseWheelValue > 0f ? 1 : -1;

            if (_activeWeaponIndex < 0) _activeWeaponIndex = _weapons.Count - 1;
            if(_activeWeaponIndex > _weapons.Count - 1) _activeWeaponIndex = 0;
            
            GetActiveWeapon().gameObject.SetActive(true);
            EquipWeapon(false, true);
        }
        
        public void OnFire(InputValue value)
        {
            if(value.isPressed)
            {
                GetActiveWeapon().OnFirePressed();
                return;
            }
            
            GetActiveWeapon().OnFireReleased();
        }

        public void OnAim(InputValue value)
        {
            bool wasAiming = _isAiming;
            _isAiming = value.isPressed;
            _recoilAnimation.isAiming = _isAiming;

            if (wasAiming != _isAiming)
            {
                _playerSound.PlayAimSound(_isAiming);
                _job.PlayIkMotion(playerSettings.aimingMotion);
            }
        }

        public void OnMove(InputValue value)
        {
            _moveInput = value.Get<Vector2>();
        }

        public void OnSprint(InputValue value)
        {
            _bSprinting = value.isPressed;
            if(!_bSprinting) _bTacSprinting = false;
        }
        
        public void OnTacSprint(InputValue value)
        {
            if (!_bSprinting) return;
            _bTacSprinting = value.isPressed;
        }

        public void OnLook(InputValue value)
        {
            Vector2 input = value.Get<Vector2>() * playerSettings.sensitivity;
            _lookInput.y = Mathf.Clamp(_lookInput.y - input.y, -90f, 90f);
            _lookInput.x = input.x;
        }
#endif
#if !ENABLE_INPUT_SYSTEM
        private void OnLookLegacy()
        {
            Vector2 input = new Vector2()
            {
                x = Input.GetAxis("Horizontal"),
                y = Input.GetAxis("Vertical")
            };
            
            _lookInput.y = Mathf.Clamp(_lookInput.y + input.y, -90f, 90f);
            _lookInput.x = input.x;
        }

        private void OnMouseWheelLegacy()
        {
            float mouseWheelValue = Input.GetAxis("Mouse ScrollWheel");
            if (mouseWheelValue == 0f) return;
            
            GetActiveWeapon().gameObject.SetActive(false);
            _activeWeaponIndex += mouseWheelValue > 0f ? 1 : -1;

            if (_activeWeaponIndex < 0) _activeWeaponIndex = _weapons.Count - 1;
            if(_activeWeaponIndex > _weapons.Count - 1) _activeWeaponIndex = 0;
            
            GetActiveWeapon().gameObject.SetActive(true);
            GetActiveWeapon().OnEquipped_Immediate();
        }

        private void OnAimLegacy(bool isPressed)
        {
            bool wasAiming = _isAiming;
            _isAiming = isPressed;
            _recoilAnimation.isAiming = _isAiming;
            
            if(wasAiming != _isAiming) 
            {
                _playerSound.PlayAimSound(_isAiming);
                PlayIkMotion(playerSettings.aimingMotion);
            }
        }
        
        private void OnMoveLegacy()
        {
            _moveInput.x = Input.GetAxis("Horizontal");
            _moveInput.y = Input.GetAxis("Vertical");
            _moveInput.Normalize();
        }

        private void OnSprintLegacy(bool isPressed)
        {
            _bSprinting = isPressed;
            if(!_bSprinting) _bTacSprinting = false;
        }

        private void OnTacSprintLegacy(bool isPressed)
        {
            if (!_bSprinting) return;
            _bTacSprinting = isPressed;
        }
        
        private void ProcessLegacyInputs()
        {
            OnMouseWheelLegacy();
            if (Input.GetKeyDown(KeyCode.G)) OnThrowGrenade();
            if (Input.GetKeyDown(KeyCode.F)) OnChangeWeapon();
            if (Input.GetKeyDown(KeyCode.B)) OnChangeFireMode();
            if (Input.GetKeyDown(KeyCode.R)) OnReload();
            if (Input.GetKeyDown(KeyCode.Space)) OnJump();
            if (Input.GetKeyDown(KeyCode.I)) OnInspect();
            
            if (Input.GetKeyDown(KeyCode.Mouse0)) GetActiveWeapon().OnFirePressed();
            if (Input.GetKeyUp(KeyCode.Mouse0)) GetActiveWeapon().OnFireReleased();

            OnAimLegacy(Input.GetKey(KeyCode.Mouse1));
            OnMoveLegacy();
            OnLookLegacy();
            OnSprintLegacy(Input.GetKey(KeyCode.LeftShift));
            OnTacSprintLegacy(Input.GetKey(KeyCode.X));
        }
#endif
        private void SetWeaponVisible()
        {
            GetActiveWeapon().gameObject.SetActive(true);
        }

        public FPSWeapon GetActiveWeapon()
        {
            return _weapons[_activeWeaponIndex];
        }

        public FPSWeapon GetActivePrefab()
        {
            return _prefabComponents[_activeWeaponIndex];
        }

        private void Start()
        {
            _animator = GetComponent<Animator>();
            _controller = transform.root.GetComponent<CharacterController>();
            _recoilAnimation = GetComponent<RecoilAnimation>();
            _playerSound = GetComponent<FPSPlayerSound>();

            _job = new FPSProceduralJob()
            {
                animator = _animator,
                skeletonRoot = skeletonRoot,
                rightArm = rightHand,
                leftArm = leftHand,
                weaponBone = weaponBone,
                weaponBoneAdditive = weaponBoneAdditive,
                cameraSocket = cameraPoint,
                recoilAnimation = _recoilAnimation
            };
            _job.Setup();
            
            _playable = AnimationScriptPlayable.Create(_animator.playableGraph, _job);
            var output = AnimationPlayableOutput.Create(_animator.playableGraph, "FPS Procedural Output", 
                _animator);
            
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            output.SetSourcePlayable(_playable);
            
            _triggerDisciplineLayerIndex = _animator.GetLayerIndex("TriggerDiscipline");
            _rightHandLayerIndex = _animator.GetLayerIndex("RightHand");
            _tacSprintLayerIndex = _animator.GetLayerIndex("TacSprint");
            
            KTransform root = new KTransform(transform);
            _localCameraPoint = root.GetRelativeTransform(new KTransform(cameraPoint), false);

            foreach (var prefab in playerSettings.weaponPrefabs)
            {
                var prefabComponent = prefab.GetComponent<FPSWeapon>();
                if(prefabComponent == null) continue;
                
                _prefabComponents.Add(prefabComponent);
                
                var instance = Instantiate(prefab, weaponBone, false);
                instance.SetActive(false);
                
                var component = instance.GetComponent<FPSWeapon>();
                component.Initialize(gameObject);

                KTransform weaponT = new KTransform(weaponBone);
                component.rightHandPose = new KTransform(rightHand.tip).GetRelativeTransform(weaponT, false);
                
                var localWeapon = root.GetRelativeTransform(weaponT, false);

                localWeapon.rotation *= prefabComponent.weaponSettings.rotationOffset;
                
                component.adsPose.position = _localCameraPoint.position - localWeapon.position;
                component.adsPose.rotation = Quaternion.Inverse(localWeapon.rotation);

                _weapons.Add(component);
            }

            EquipWeapon();
        }

        private float GetDesiredGait()
        {
            if (_bTacSprinting) return 3f;
            if (_bSprinting) return 2f;
            return _moveInput.magnitude;
        }
        
        private void Update()
        {
#if !ENABLE_INPUT_SYSTEM
            ProcessLegacyInputs();
#endif
            _adsWeight = Mathf.Clamp01(_adsWeight + playerSettings.aimSpeed * Time.deltaTime * (_isAiming ? 1f : -1f));

            _smoothGait = Mathf.Lerp(_smoothGait, GetDesiredGait(), 
                KMath.ExpDecayAlpha(playerSettings.gaitSmoothing, Time.deltaTime));
            
            _animator.SetFloat(GAIT, _smoothGait);
            _animator.SetLayerWeight(_tacSprintLayerIndex, Mathf.Clamp01(_smoothGait - 2f));

            bool triggerAllowed = GetActiveWeapon().weaponSettings.useSprintTriggerDiscipline;

            _animator.SetLayerWeight(_triggerDisciplineLayerIndex,
                triggerAllowed ? _animator.GetFloat(TAC_SPRINT_WEIGHT) : 0f);

            _animator.SetLayerWeight(_rightHandLayerIndex, _animator.GetFloat(RIGHT_HAND_WEIGHT));
            
            _job.Update();
            _job.adsWeight = _adsWeight;
            _job.ikWeight = ikWeight;
            _playable.SetJobData(_job);
            
            Vector3 cameraPosition = -_localCameraPoint.position;
            
            transform.localRotation = Quaternion.Euler(_lookInput.y, 0f, 0f);
            transform.localPosition = transform.localRotation * cameraPosition - cameraPosition;

            if (_controller != null)
            {
                Transform root = _controller.transform;
                root.rotation *= Quaternion.Euler(0f, _lookInput.x, 0f);
                Vector3 movement = root.forward * _moveInput.y + root.right * _moveInput.x;
                movement *= _smoothGait * 1.5f;
                _controller.Move(movement * Time.deltaTime);
            }
        }

        private void OnFire()
        {
            _recoilAnimation.Play();
        }
    }
}