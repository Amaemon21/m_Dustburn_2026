// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;

using KINEMATION.MotionWarping.Runtime.Core;
using KINEMATION.MotionWarping.Runtime.Examples;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace KINEMATION.CharacterAnimationSystem.Examples.Scripts
{
    public enum CharacterMovementState
    {
        Default,
        Idle,
        CrouchWalk,
        Walk,
        Jog,
        Sprint
    }

    [Serializable]
    public struct MovementGaitSettings
    {
        [Min(0f)] public float velocity;
        [Min(0f)] public float acceleration;
        [Min(0f)] public float deceleration;
        [Min(0f)] public float rotationSpeed;
    }

    public struct SurfaceTraceInfo
    {
        public bool hasHit;
        public RaycastHit hitInfo;
        public bool canSnapToGround;
    }

    [AddComponentMenu(CasNames.Path_ComponentMenu + "Examples/Character Example Controller")]
    public class CharacterExampleController : MonoBehaviour
    {
        public Transform RightHandIkTarget => GetActiveItem() == null ? null : GetActiveItem().rightHandTarget;
        public Transform LeftHandIkTarget => GetActiveItem() == null ? null : GetActiveItem().leftHandTarget;
        
        public float Gait => _gait;
        public float AimingWeight { get; private set; }
        public Vector2 DeltaLookInput => GetDeltaLookInput();
        public Vector3 LookInput => _lookInput;
        public Vector2 MoveInput => _moveInput;
        public Quaternion AimRotation => _aimRotation;
        public bool IsAiming => _isAiming;
        public bool IsGrounded => !_isInAir;

        [Tab("Controller")]
        [SerializeField, Range(0f, 1f)] protected float timeScale = 1f;
        [SerializeField] protected LayerMask groundLayer = 1;

        [Header("Inputs")] 
        [SerializeField, Min(0f)] protected float mouseSensitivity = 1f;

        [Header("Movement Gaits")]
        [SerializeField, Min(0f)] protected float movingVelocityThreshold = 0.1f;
        [SerializeField, Range(0f, 1f)] protected float walkInputThreshold = 0.5f;
        [SerializeField] protected bool useDefaultWalk = false;
        [SerializeField, Min(0f)] protected float idleDeceleration = 50f;
        [SerializeField, Min(0f)] protected float animGaitSmoothing = 12f;
        [SerializeField] protected MovementGaitSettings walkGait = new MovementGaitSettings()
        {
            velocity = 1.5f,
            acceleration = 2f,
            deceleration = 2f,
            rotationSpeed = 5f,
        };
        [SerializeField] protected MovementGaitSettings jogGait = new MovementGaitSettings()
        {
            velocity = 3f,
            acceleration = 1.8f,
            deceleration = 1.5f,
            rotationSpeed = 5f,
        };
        [SerializeField] protected MovementGaitSettings sprintGait = new MovementGaitSettings()
        {
            velocity = 5f,
            acceleration = 1.7f,
            deceleration = 2f,
            rotationSpeed = 4f,
        };
        
        [Header("Crouching")]
        [SerializeField] protected MovementGaitSettings crouchWalkGait = new MovementGaitSettings()
        {
            velocity = 1.1f,
            acceleration = 2.2f,
            deceleration = 2f,
            rotationSpeed = 4f,
        };

        [Header("In Air")]
        [SerializeField, Min(0f)] protected float jumpHeight = 1f;
        
        [SerializeField, Min(0f)] protected float fallDelay = 0.3f;
        
        [SerializeField, Min(0f)] protected float gravity = 9.81f;
        [SerializeField, Min(0f)] protected float maxFallVelocity = 25f;
        [SerializeField, Min(0f)] protected float airMaxSpeed = 3f;
        [SerializeField, Min(0f)] protected float airAcceleration = 4f;
        [SerializeField, Range(0f, 1f)] protected float landingMomentumFactor = 0.6f;
        [SerializeField, Min(0f)] protected float groundedStickVelocity = 2f;

        [Header("Grounding Stability")]
        [SerializeField, Min(0f)] protected float jumpCoyoteTime = 0.12f;
        [SerializeField, Min(0f)] protected float groundSnapDistance = 0.25f;
        [SerializeField, Range(0f, 89f)] protected float maxStableSlopeAngle = 60f;
        [SerializeField, Range(0.05f, 1f)] protected float groundProbeRadiusFactor = 0.9f;
        [SerializeField, Min(0f)] protected float minFallSpeedToInAir = 1.25f;
        [SerializeField] protected bool debugGrounding = false;
        
        [Header("Rotation")] 
        [SerializeField] public bool isFirstPerson = false;
        [SerializeField] protected bool orientRotationToMovement = true;
        [SerializeField, Min(0f)] protected float aimingRotationSpeed = 10f;

        [Tab("Animation")] 
        [SerializeField, Range(0f, 90f)] protected float leanAngle = 30f;
        
        [Header("Steps")]
        public StepModifierSettings stepCrouch;
        public StepModifierSettings stepUncrouch;
        public StepModifierSettings stepInPlace;
        public StepModifierSettings startMoving;
        public StepModifierSettings stopMoving;
        
        [Header("Smoothing")]
        [SerializeField, Min(0f)] protected float animatorMoveInterpSpeed = 7f;
        
        protected CharacterController _controller;
        protected Animator _animator;
        protected CharacterAnimationComponent _characterAnimation;
        protected ProceduralAnimationComponent _proceduralAnimation;
        
        protected bool _isInAir;
        protected bool _wantsToJump;
        protected bool _isGrounded;
        protected float _timeSinceLastGrounded;
        protected SurfaceTraceInfo _surfaceTrace;
        
        protected bool _isSprintPressed = false;
        protected bool _isCrouching = false;

        protected Vector2 _deltaLookInput;
        protected Vector3 _lookInput;

        protected float _leanInput;
        
        protected Quaternion _aimRotation = Quaternion.identity;
        protected Quaternion _targetRotation = Quaternion.identity;

        protected Vector2 _moveInput;
        protected bool _isAiming;
        
        protected bool _wasMoving;

        protected ClimbComponent _climbComponent;
        protected VaultComponent _vaultComponent;
        protected MotionWarpingComponent _motionWarping;

        protected CharacterMovementState _movementState = CharacterMovementState.Idle;
        protected CharacterMovementState _prevMovementState = CharacterMovementState.Walk;
        protected MovementGaitSettings _activeGait;
        
        protected List<CasProp> _items = new List<CasProp>();
        protected int _activeItemIndex = 0;
        
        protected float _originalHeight;
        protected Vector3 _originalCenter;

        protected float _gait;
        protected float _animatorGait;

        protected ICharacterInteraction _interaction;
        protected Collider _collider;
        protected Vector3 _velocity;

        protected static int Animator_Move_X = Animator.StringToHash("MoveX");
        protected static int Animator_Move_Y = Animator.StringToHash("MoveY");
        
        protected static int Animator_ViewWeight = Animator.StringToHash("ViewWeight");
        protected static int Animator_IsFirstPerson = Animator.StringToHash("IsFirstPerson");
        
        protected static int Animator_Gait = Animator.StringToHash("Gait");
        protected static int Animator_Crouch = Animator.StringToHash("Crouch");
        protected static int Animator_IsMoving = Animator.StringToHash("IsMoving");
        
        protected static int Animator_Jumped = Animator.StringToHash("Jumped");
        protected static int Animator_IsInAir = Animator.StringToHash("IsInAir");
        
        protected static int Animator_AimingWeight = Animator.StringToHash("AimingWeight");
        
        protected static int Animator_IsTraversing = Animator.StringToHash("IsTraversing");

        protected CharacterCamera _characterCamera;
        protected PlayerInput _playerInput;
        
        public ICharacterInteraction GetActiveInteraction()
        {
            return _interaction;
        }

        public CasProp GetActiveItem()
        {
            return _activeItemIndex < 0  || _activeItemIndex > _items.Count - 1 ? null : _items[_activeItemIndex];
        }

        protected virtual Vector2 GetDeltaLookInput()
        {
            return _deltaLookInput;
        }
        
        protected void Crouch()
        {
            float crouchedHeight = _originalHeight * 0.5f;
            float heightDifference = _originalHeight - crouchedHeight;

            _controller.height = crouchedHeight;
            
            Vector3 crouchedCenter = _originalCenter;
            crouchedCenter.y -= heightDifference / 2;
            _controller.center = crouchedCenter;
        }

        protected void UnCrouch()
        {
            _controller.height = _originalHeight;
            _controller.center = _originalCenter;
        }
        
        protected bool CanUnCrouch()
        {
            float height = _originalHeight - _controller.radius * 2f;
            Vector3 position = transform.TransformPoint(_originalCenter + Vector3.up * height / 2f);
            return !Physics.CheckSphere(position, _controller.radius);
        }

        protected void ToggleCrouch()
        {
            if (_isSprintPressed) _isSprintPressed = false;

            bool wantsToCrouch = !_isCrouching;
            if (!wantsToCrouch && !CanUnCrouch()) return;

            _isCrouching = wantsToCrouch;
            _animator.SetBool(Animator_Crouch, _isCrouching);

            if (_characterCamera != null) _characterCamera.isCrouching = _isCrouching;
            
            if (_isGrounded && !HasMoveInputs())
            {
                _proceduralAnimation.UpdateAnimationModifier(_isCrouching ? stepCrouch : stepUncrouch);
            }

            if (_isCrouching)
            {
                Crouch();
                return;
            }

            UnCrouch();
        }

        protected void ToggleDefaultWalk()
        {
            useDefaultWalk = !useDefaultWalk;
        }

        protected bool CanSprint()
        {
            return _moveInput.y > 0f || !isFirstPerson && !_isAiming && orientRotationToMovement;
        }

        protected bool HasMoveInputs()
        {
            return _moveInput.magnitude > 0f;
        }

        protected float GetHorizontalSpeed()
        {
            return new Vector2(_velocity.x, _velocity.z).magnitude;
        }

        protected bool IsMoving()
        {
            return GetHorizontalSpeed() > movingVelocityThreshold;
        }

        protected CharacterMovementState GetDesiredGroundedState()
        {
            if (_isCrouching)
            {
                return CharacterMovementState.CrouchWalk;
            }

            if (_isSprintPressed && CanSprint())
            {
                return CharacterMovementState.Sprint;
            }

            if (_moveInput.magnitude <= walkInputThreshold)
            {
                return CharacterMovementState.Walk;
            }

            return useDefaultWalk ? CharacterMovementState.Walk : CharacterMovementState.Jog;
        }

        protected MovementGaitSettings GetGaitSettings(CharacterMovementState movementState)
        {
            switch (movementState)
            {
                case CharacterMovementState.Sprint:
                    return sprintGait;
                case CharacterMovementState.Jog:
                    return jogGait;
                case CharacterMovementState.Walk:
                    return walkGait;
                case CharacterMovementState.CrouchWalk:
                    return crouchWalkGait;
                default:
                    return new MovementGaitSettings();
            }
        }

        protected virtual void EquipNextWeapon()
        {
            GetActiveItem().SetVisibility(false);
            
            _activeItemIndex = _activeItemIndex + 1 < _items.Count ? _activeItemIndex + 1 : 0;
            
            _characterAnimation.UpdateAnimationSettings(GetActiveItem().animationSettings);

            if (_isGrounded && !HasMoveInputs())
            {
                _proceduralAnimation.UpdateAnimationModifier(stepInPlace);
            }
            
            GetActiveItem().OnEquipped();
            GetActiveItem().SetVisibility(true);
        }
        
#if ENABLE_INPUT_SYSTEM
        public virtual void OnInteract()
        {
            if (_collider == null) return;
            _interaction = _collider.GetComponent<ICharacterInteraction>();
            _interaction?.StartInteraction(gameObject);
        }
        
        public virtual void OnSprint(InputValue value)
        {
            _isSprintPressed = value.isPressed;
        }

        public virtual void OnCrouch()
        {
            ToggleCrouch();
        }

        public virtual void OnToggleWalk()
        {
            ToggleDefaultWalk();
        }

        public virtual void OnLook(InputValue value)
        {
            _deltaLookInput = value.Get<Vector2>() * mouseSensitivity;
        }

        public virtual void OnJump()
        {
            if (_isInAir && _timeSinceLastGrounded > jumpCoyoteTime) return;
            
            if (_motionWarping != null)
            {
                if (_isSprintPressed && _motionWarping.Interact(_vaultComponent)) return;
                if (_motionWarping.Interact(_climbComponent)) return;
            }
            
            _wantsToJump = true;
        }

        public virtual void OnMove(InputValue value)
        {
            _moveInput = value.Get<Vector2>();
        }

        public virtual void OnUseItem(InputValue value)
        {
            var item = GetActiveItem();
            if (item == null) return;
            
            if (value.isPressed)
            {
                item.UseItem();
                return;
            }
            
            item.StopUsingItem();
        }

        public virtual void OnAim(InputValue value)
        {
            _isAiming = value.isPressed;
            if (_characterCamera != null) _characterCamera.isAiming = _isAiming;

            var item = GetActiveItem();
            if(item != null) item.OnAim(_isAiming);
        }

        public virtual void OnTogglePerspective()
        {
            isFirstPerson = !isFirstPerson;
            if (_characterCamera != null) _characterCamera.isFirstPerson = isFirstPerson;
        }

        public virtual void OnChangeItem()
        {
            if (GetActiveItem() == null) return;
            
            float delay = GetActiveItem().OnUnEquipped();
            Invoke(nameof(EquipNextWeapon), delay);
        }

        public virtual void OnChangeShoulder()
        {
            if(_characterCamera != null) _characterCamera.useRightShoulder = !_characterCamera.useRightShoulder;
        }
        
        public virtual void OnLean(InputValue value)
        {
            _leanInput = -value.Get<float>() * leanAngle;
        }
#endif
        
        private void OnTriggerEnter(Collider other)
        {
            _collider = other;
        }

        private void OnTriggerExit(Collider other)
        {
            _collider = null;
        }

        protected Vector3 GetSlopeAdjustedVelocity(Vector3 velocity)
        {
            if (!_isGrounded || velocity.sqrMagnitude < 0.001f)
            {
                return velocity;
            }

            if (!_surfaceTrace.hasHit)
            {
                return velocity;
            }

            Vector3 slopeVelocity =
                Vector3.ProjectOnPlane(velocity, _surfaceTrace.hitInfo.normal).normalized * velocity.magnitude;

            return slopeVelocity;
        }

        protected Vector3 GetMovementInputDirection()
        {
            Vector2 input = _moveInput.normalized;
            Vector3 direction = _aimRotation * Vector3.forward * input.y + _aimRotation * Vector3.right * input.x;
            return direction;
        }

        protected virtual void OnValidate()
        {
            Time.timeScale = timeScale;
        }
        
        protected virtual void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null) return;
            _playerInput.ActivateInput();
        }

        protected virtual void Start()
        {
            _controller = transform.root.GetComponentInChildren<CharacterController>();
            _originalCenter = _controller.center;
            _originalHeight = _controller.height;
            
            _animator = GetComponentInChildren<Animator>();
            _characterAnimation = GetComponentInChildren<CharacterAnimationComponent>();
            _proceduralAnimation = GetComponentInChildren<ProceduralAnimationComponent>();

            _aimRotation = transform.rotation;
            _motionWarping = GetComponent<MotionWarpingComponent>();
            _climbComponent = GetComponent<ClimbComponent>();
            _vaultComponent = GetComponent<VaultComponent>();

            _items = GetComponentsInChildren<CasProp>().ToList();
            foreach (var item in _items) item.gameObject.SetActive(false);

            if (GetActiveItem() != null)
            {
                GetActiveItem().OnEquipped();
                GetActiveItem().SetVisibility(true);
                _characterAnimation.UpdateAnimationSettings(GetActiveItem().animationSettings);
            }

            _characterCamera = GetComponentInChildren<CharacterCamera>();
            
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
            Application.targetFrameRate = 120;
        }

        protected virtual void SetCharacterSmoothRotation(Quaternion targetRotation, float speed = 0f)
        {
            _targetRotation = targetRotation;
            
            if (Mathf.Approximately(speed, 0f))
            {
                transform.rotation = targetRotation;
                return;
            }
            
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 
                KMath.ExpDecayAlpha(speed, Time.deltaTime));
        }

        protected virtual SurfaceTraceInfo TraceSurfaceBelow()
        {
            SurfaceTraceInfo traceInfo = default;

            float probeRadius = Mathf.Max(0.01f, _controller.radius * groundProbeRadiusFactor);
            float halfHeight = Mathf.Max(_controller.height * 0.5f, _controller.radius);
            float hemisphereOffset = Mathf.Max(0f, halfHeight - _controller.radius);

            Vector3 bottom = transform.TransformPoint(_controller.center + Vector3.down * hemisphereOffset);
            Vector3 top = transform.TransformPoint(_controller.center + Vector3.up * hemisphereOffset);
            float castDistance = groundSnapDistance;
            
            bool foundHit = Physics.CapsuleCast(bottom, top, probeRadius, Vector3.down, 
                out RaycastHit hitInfo, castDistance, groundLayer, QueryTriggerInteraction.Ignore);

            if (foundHit)
            {
                traceInfo.hasHit = true;
                traceInfo.hitInfo = hitInfo;
                float distanceFromBottom = Mathf.Max(0f, hitInfo.distance);
                
                bool isStableSlope = Vector3.Angle(hitInfo.normal, transform.up) <= maxStableSlopeAngle;
                bool withinSnapDistance = distanceFromBottom <= groundSnapDistance;
                bool withinSnapSpeed = _velocity.y <= -Mathf.Max(0f, groundedStickVelocity);
                
                traceInfo.canSnapToGround = isStableSlope && withinSnapDistance && withinSnapSpeed;
            }

            if (debugGrounding)
            {
                Vector3 origin = transform.TransformPoint(_controller.center);
                float debugDistance = halfHeight + castDistance;
                Debug.DrawRay(origin, Vector3.down * debugDistance, foundHit ? Color.green : Color.red);
                if (traceInfo.hasHit)
                {
                    bool isStableSlope = Vector3.Angle(traceInfo.hitInfo.normal, transform.up) <= maxStableSlopeAngle;
                    Color normalColor = isStableSlope ? Color.cyan : Color.yellow;
                    Debug.DrawRay(traceInfo.hitInfo.point, traceInfo.hitInfo.normal * 0.4f, normalColor);
                }
            }

            return traceInfo;
        }

        protected virtual bool ResolveGroundedState(SurfaceTraceInfo surfaceTrace)
        {
            if (_motionWarping != null && _motionWarping.IsActive())
            {
                return true;
            }

            bool rawGrounded = _controller.isGrounded;
            return rawGrounded || surfaceTrace.canSnapToGround;
        }

        protected virtual bool ShouldEnterInAirState(SurfaceTraceInfo surfaceTrace)
        {
            if (_timeSinceLastGrounded < fallDelay)
            {
                return false;
            }

            if (surfaceTrace.hasHit && surfaceTrace.canSnapToGround)
            {
                return false;
            }

            float distanceFromBottom = surfaceTrace.hasHit ? Mathf.Max(0f, surfaceTrace.hitInfo.distance) : float.PositiveInfinity;
            bool isStableSlope = surfaceTrace.hasHit &&
                Vector3.Angle(surfaceTrace.hitInfo.normal, transform.up) <= maxStableSlopeAngle;

            bool hasStableSurfaceNearby = surfaceTrace.hasHit && isStableSlope &&
                distanceFromBottom <= groundSnapDistance && _velocity.y <= 0f;
            if (hasStableSurfaceNearby)
            {
                return false;
            }

            bool fallingFastEnough = _velocity.y <= -minFallSpeedToInAir;
            if (fallingFastEnough)
            {
                return true;
            }
            
            bool stillTooCloseToGround = surfaceTrace.hasHit && distanceFromBottom <= groundSnapDistance;
            return !stillTooCloseToGround;
        }

        protected virtual void ApplyLandingMomentum()
        {
            Vector3 landingVelocity = _velocity;
            landingVelocity.y = 0f;
            
            landingVelocity *= landingMomentumFactor;

            if (!HasMoveInputs())
            {
                landingVelocity = Vector3.zero;
            }

            _velocity.x = landingVelocity.x;
            _velocity.z = landingVelocity.z;
        }

        protected virtual void UpdateMovementState()
        {
            bool wasGrounded = _isGrounded;
            bool wasInAir = _isInAir;
            _surfaceTrace = TraceSurfaceBelow();
            _isGrounded = ResolveGroundedState(_surfaceTrace);

            if (_isGrounded)
            {
                _timeSinceLastGrounded = 0f;
            }
            else
            {
                _timeSinceLastGrounded += Time.deltaTime;
            }

            bool canJumpNow = _isGrounded || _timeSinceLastGrounded <= jumpCoyoteTime;
            if (_wantsToJump && canJumpNow)
            {
                _animator.SetTrigger(Animator_Jumped);
                _velocity.y = Mathf.Sqrt(Mathf.Max(0f, 2f * gravity * jumpHeight));
                
                _isGrounded = false;
                _isInAir = true;
            }
            
            _wantsToJump = false;

            if (_isGrounded)
            {
                if (!wasGrounded)
                {
                    // Ground snapping can produce a temporary ungrounded frame without a real in-air transition.
                    // Only apply landing damping when we were actually in air.
                    if (wasInAir)
                    {
                        _isInAir = false;
                        ApplyLandingMomentum();
                    }
                }
            }
            else
            {
                if (!_isInAir && ShouldEnterInAirState(_surfaceTrace)) _isInAir = true;
            }

            _prevMovementState = _movementState;

            if (!HasMoveInputs())
            {
                _movementState = CharacterMovementState.Idle;
                return;
            }

            _movementState = GetDesiredGroundedState();
        }

        protected virtual void UpdateGait()
        {
            if (_motionWarping != null && _motionWarping.IsActive())
            {
                _prevMovementState = _movementState = CharacterMovementState.Default;
                _velocity = Vector3.zero;
                return;
            }
            
            if (_prevMovementState != _movementState)
            {
                _activeGait = GetGaitSettings(_movementState);
                
                if (_movementState == CharacterMovementState.Idle)
                {
                    _activeGait.deceleration = idleDeceleration;
                }
                else if (_prevMovementState > _movementState)
                {
                    _activeGait.deceleration = GetGaitSettings(_prevMovementState).deceleration;
                }
            }
            
            float speed = GetHorizontalSpeed();
            float walkSpeed = Mathf.Max(0.01f, walkGait.velocity);
            float jogSpeed = Mathf.Max(walkSpeed + 0.01f, jogGait.velocity);
            float sprintSpeed = Mathf.Max(jogSpeed + 0.01f, sprintGait.velocity);

            if (speed <= walkSpeed)
            {
                _gait = _movementState == CharacterMovementState.Idle ? 0f : 1f;
            }
            else if (speed > walkSpeed && speed <= jogSpeed)
            {
                _gait = 1f + Mathf.InverseLerp(walkSpeed, jogSpeed, speed);
            }
            else
            {
                _gait = 2f + Mathf.InverseLerp(jogSpeed, sprintSpeed, Mathf.Min(speed, sprintSpeed));
            }
            _animatorGait = KMath.FloatInterp(_animatorGait, _gait, animGaitSmoothing, Time.deltaTime);
        }

        protected virtual void UpdateCharacterMovement()
        {
            if (_motionWarping != null && _motionWarping.IsActive()) return;
            
            if (_isInAir)
            {
                _velocity.y -= gravity * Time.deltaTime;
                _velocity.y = Mathf.Max(_velocity.y, -maxFallVelocity);

                Vector3 horizontalVelocity = new Vector3(_velocity.x, 0f, _velocity.z);
                float currentHorizontalSpeed = horizontalVelocity.magnitude;
                
                Vector3 airInput = GetMovementInputDirection() * (airAcceleration * Time.deltaTime);
                horizontalVelocity += airInput;
                float horizontalCap = Mathf.Max(airMaxSpeed, currentHorizontalSpeed);
                horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, horizontalCap);
                
                _velocity.x = horizontalVelocity.x;
                _velocity.z = horizontalVelocity.z;
            }
            else
            {
                _velocity.y = -Mathf.Max(0f, groundedStickVelocity);

                Vector3 targetVelocity = Vector3.zero;
                if (HasMoveInputs())
                {
                    Vector3 direction = GetSlopeAdjustedVelocity(GetMovementInputDirection());
                    targetVelocity = direction * _activeGait.velocity;
                }

                Vector3 velocity = new Vector3(_velocity.x, 0f, _velocity.z);
                float interpSpeed = targetVelocity.sqrMagnitude > velocity.sqrMagnitude
                    ? _activeGait.acceleration
                    : _activeGait.deceleration;
                
                _velocity.x = KMath.FloatInterp(velocity.x, targetVelocity.x, interpSpeed, Time.deltaTime);
                _velocity.z = KMath.FloatInterp(velocity.z, targetVelocity.z, interpSpeed, Time.deltaTime);
            }
            
            _controller.Move(_velocity * Time.deltaTime);
        }

        protected virtual void UpdateCharacterRotation()
        {
            _lookInput.x = Mathf.Clamp(_lookInput.x + GetDeltaLookInput().x, -90f, 90f);
            _lookInput.y = Mathf.Clamp(_lookInput.y - GetDeltaLookInput().y, -90f, 90f);
            _lookInput.z = KMath.FloatInterp(_lookInput.z, _leanInput, 8f, Time.deltaTime);
            
            _aimRotation *= Quaternion.Euler(0f, GetDeltaLookInput().x, 0f);
            _aimRotation.Normalize();
            
            _characterCamera.pitchInput = _lookInput.y;
            _characterCamera.yawInput = _aimRotation.eulerAngles.y;
            
            if (_motionWarping != null && _motionWarping.IsActive()) return;
            
            if (isFirstPerson)
            {
                AimingWeight = KMath.FloatInterp(AimingWeight, 1f, 8f, Time.deltaTime);
                SetCharacterSmoothRotation(_aimRotation, 20f);
                return;
            }

            if (_isAiming)
            {
                AimingWeight = KMath.FloatInterp(AimingWeight, 1f, 8f, Time.deltaTime);
                SetCharacterSmoothRotation(_aimRotation, aimingRotationSpeed);
                return;
            }
            
            AimingWeight = KMath.FloatInterp(AimingWeight, 0f, 8f, Time.deltaTime);
            
            if (HasMoveInputs())
            {
                Quaternion targetRotation = orientRotationToMovement
                    ? Quaternion.LookRotation(GetMovementInputDirection(), transform.up)
                    : _aimRotation;
                SetCharacterSmoothRotation(targetRotation, _activeGait.rotationSpeed);
            }
        }

        protected virtual void UpdateAnimatorParameters()
        {
            Vector2 moveInput = _moveInput;
            if (!isFirstPerson && !_isAiming && orientRotationToMovement)
            {
                moveInput.y = _moveInput.normalized.magnitude;
                moveInput.x = 0f;
            }

            float moveAlpha = KMath.ExpDecayAlpha(animatorMoveInterpSpeed, Time.deltaTime);
            Vector2 animatorMove = Vector2.Lerp(
                new Vector2(_animator.GetFloat(Animator_Move_X), _animator.GetFloat(Animator_Move_Y)),
                moveInput, moveAlpha);
            _animator.SetFloat(Animator_Move_X, animatorMove.x);
            _animator.SetFloat(Animator_Move_Y, animatorMove.y);
            _animator.SetFloat(Animator_Gait, _animatorGait);
            
            _animator.SetFloat(Animator_ViewWeight, _characterCamera.ViewWeight);
            _animator.SetFloat(Animator_AimingWeight, AimingWeight);
            
            _animator.SetBool(Animator_IsFirstPerson, isFirstPerson);
            _animator.SetBool(Animator_IsInAir, _isInAir);
            
            _animator.SetBool(Animator_IsMoving, HasMoveInputs());
            if(_motionWarping != null) _animator.SetBool(Animator_IsTraversing, _motionWarping.IsActive());
        }

        protected virtual void OnMovementChange(bool isMoving)
        {
            if (_proceduralAnimation == null) return; 
            if(_isGrounded) _proceduralAnimation.UpdateAnimationModifier(isMoving ? startMoving : stopMoving);
        }

        protected virtual void Update()
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_playerInput.inputIsActive)
                {
                    _playerInput.DeactivateInput();
                }
                else
                {
                    _playerInput.ActivateInput();
                    Cursor.visible = false;
                }
            }
#endif
            
            UpdateMovementState();
            UpdateGait();
            UpdateCharacterRotation();
            UpdateCharacterMovement();

            bool isMoving = HasMoveInputs();

            if (_wasMoving != isMoving)
            {
                OnMovementChange(isMoving);
            }

            _wasMoving = isMoving;
            
            UpdateAnimatorParameters();
        }
    }
}