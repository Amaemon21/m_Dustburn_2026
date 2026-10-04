using System;
using DG.Tweening;
using UnityEngine;

public sealed class PlayerMotor : IDisposable
{
    private const float GROUNDED_VELOCITY = -2f;
    private const float CEILING_PROBE_SCALE = 0.95f;

    private readonly CharacterController _controller;
    private readonly Transform _body;
    private readonly PlayerMovementConfig _config;
    private readonly float _groundSlopeLimit;
    private readonly float _jumpVelocity;
    private float _verticalVelocity;
    private float _airTime;
    private bool _crouching;
    private Tween _heightTween;

    public bool IsCrouching => _crouching;

    public PlayerMotor(CharacterController controller, PlayerMovementConfig config)
    {
        _controller = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
        _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        _body = controller.transform;
        _groundSlopeLimit = controller.slopeLimit;
        _jumpVelocity = Mathf.Sqrt(config.JumpHeight * -2f * config.Gravity);
        SetHeight(config.StandingHeight);
    }

    public PlayerMovementResult Tick(PlayerMovementRequest request, float deltaTime)
    {
        bool wasGrounded = _controller.isGrounded;
        UpdateCrouch(request.Crouch);

        bool sprinting = request.Sprint && !_crouching && request.Move.y > 0f;
        float speed = (_crouching ? _config.CrouchSpeed : _config.WalkSpeed) * (sprinting ? _config.SprintMultiplier : 1f) * request.SpeedMultiplier;
        Vector3 planar = Vector3.ClampMagnitude(_body.right * request.Move.x + _body.forward * request.Move.y, 1f) * speed;

        bool jumped = request.Jump && wasGrounded && !_crouching;
        if (wasGrounded && _verticalVelocity < 0f)
            _verticalVelocity = GROUNDED_VELOCITY;
        if (jumped)
            _verticalVelocity = _jumpVelocity;
        _verticalVelocity += _config.Gravity * deltaTime;

        _controller.slopeLimit = wasGrounded ? _groundSlopeLimit : _config.AirSlopeLimit;
        CollisionFlags flags = _controller.Move((planar + Vector3.up * _verticalVelocity) * deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            _verticalVelocity = 0f;

        bool grounded = _controller.isGrounded;
        bool landed = grounded && !wasGrounded && _airTime > _config.MinAirTimeToLand;
        _airTime = grounded ? 0f : _airTime + deltaTime;

        return new PlayerMovementResult(request.Move, grounded, sprinting && request.Move.sqrMagnitude > 0f, _crouching,
            jumped, landed, planar.magnitude);
    }

    private void UpdateCrouch(bool crouch)
    {
        if (crouch == _crouching)
            return;
        if (!crouch && !CanStand())
            return;

        _crouching = crouch;
        float height = crouch ? _config.CrouchHeight : _config.StandingHeight;

        _heightTween?.Kill();
        _heightTween = DOTween.To(() => _controller.height, SetHeight, height, _config.CrouchTransitionTime)
            .SetEase(Ease.InOutQuad).SetLink(_controller.gameObject);
    }

    private void SetHeight(float height)
    {
        float feetDrop = (_config.StandingHeight - height) * 0.5f;
        _controller.height = height;
        _controller.center = _config.StandingCenter + Vector3.down * feetDrop;
    }

    private bool CanStand()
    {
        float radius = _controller.radius * CEILING_PROBE_SCALE;
        Vector3 top = _body.TransformPoint(_controller.center + Vector3.up * (_controller.height * 0.5f - _controller.radius));
        float distance = _config.StandingHeight - _controller.height;
        return distance <= 0f || !Physics.SphereCast(top, radius, Vector3.up, out _, distance, _config.CeilingMask, QueryTriggerInteraction.Ignore);
    }

    public void Dispose()
    {
        _heightTween?.Kill();
    }
}
