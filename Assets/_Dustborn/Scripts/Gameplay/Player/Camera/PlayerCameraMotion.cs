using System;
using DG.Tweening;
using R3;
using UnityEngine;

public sealed class PlayerCameraMotion : IDisposable
{
    private const float STEP_SINE = -0.95f;

    private readonly Transform _pivot;
    private readonly Transform _tiltRoot;
    private readonly PlayerCameraConfig _config;
    private readonly Subject<bool> _footstep = new();
    private readonly float _baseY;
    private float _crouchOffset;
    private float _bobOffset;
    private float _bobTimer;
    private Vector2 _targetTilt;
    private Tween _tiltTween;
    private bool _leftFoot = true;
    private bool _stepFired;

    public Observable<bool> Footstep => _footstep;

    public PlayerCameraMotion(Transform pivot, Transform tiltRoot, PlayerCameraConfig config)
    {
        _pivot = pivot;
        _tiltRoot = tiltRoot;
        _config = config;
        _baseY = pivot.localPosition.y;
    }

    public void Tick(in PlayerMovementResult movement, float deltaTime)
    {
        UpdateTilt(movement.Move);
        UpdateCrouchOffset(movement.IsCrouching, deltaTime);
        UpdateBob(movement, deltaTime);

        Vector3 position = _pivot.localPosition;
        _pivot.localPosition = new Vector3(position.x, _baseY + _crouchOffset + _bobOffset, position.z);
    }

    private void UpdateCrouchOffset(bool crouching, float deltaTime)
    {
        float target = crouching ? _config.CrouchCameraOffset : 0f;
        _crouchOffset = Mathf.Lerp(_crouchOffset, target, deltaTime * _config.CrouchTransitionSpeed);
    }

    private void UpdateTilt(Vector2 move)
    {
        if (_tiltRoot == null)
            return;

        Vector2 target = new(move.y * _config.TiltForward, -move.x * _config.TiltSide);
        if (target == _targetTilt)
            return;

        _targetTilt = target;
        Vector3 euler = _tiltRoot.localEulerAngles;
        Vector2 current = new(Mathf.DeltaAngle(0f, euler.x), Mathf.DeltaAngle(0f, euler.z));

        _tiltTween?.Kill();
        _tiltTween = DOTween.To(() => current, SetTilt, target, _config.TiltDuration)
            .SetEase(_config.TiltEase)
            .SetLink(_tiltRoot.gameObject);
    }

    private void SetTilt(Vector2 tilt)
    {
        Vector3 euler = _tiltRoot.localEulerAngles;
        _tiltRoot.localEulerAngles = new Vector3(tilt.x, euler.y, tilt.y);
    }

    private void UpdateBob(in PlayerMovementResult movement, float deltaTime)
    {
        if (!movement.IsGrounded)
            return;

        float threshold = _config.MoveThreshold;
        if (Mathf.Abs(movement.Move.x) <= threshold && Mathf.Abs(movement.Move.y) <= threshold)
        {
            ResetBob();
            return;
        }

        float speed = movement.IsCrouching ? _config.CrouchBobSpeed : movement.IsSprinting ? _config.SprintBobSpeed : _config.WalkBobSpeed;
        float amount = movement.IsCrouching ? _config.CrouchBobAmount : movement.IsSprinting ? _config.SprintBobAmount : _config.WalkBobAmount;

        _bobTimer += deltaTime * speed;
        float sine = Mathf.Sin(_bobTimer);
        _bobOffset = sine * amount;
        FireFootstep(sine, movement.IsCrouching);
    }

    private void FireFootstep(float sine, bool crouching)
    {
        if (sine >= STEP_SINE)
        {
            _stepFired = false;
            return;
        }
        if (_stepFired || crouching)
            return;

        _footstep.OnNext(_leftFoot);
        _leftFoot = !_leftFoot;
        _stepFired = true;
    }

    private void ResetBob()
    {
        _bobOffset = 0f;
        _bobTimer = 0f;
        _stepFired = false;
        _leftFoot = true;
    }

    public void Dispose()
    {
        _tiltTween?.Kill();
        _footstep.Dispose();
    }
}
