using System;
using DG.Tweening;
using R3;
using UnityEngine;

public sealed class PlayerCameraMotion : IViewImpact, IDisposable
{
    private readonly Transform _pivot;
    private readonly Transform _tiltRoot;
    private readonly TransformOffset _headBob;
    private readonly TransformOffset _camera;
    private readonly TransformOffset _arms;
    private readonly PlayerCameraConfig _config;
    private readonly CameraHeadBob _bob;
    private readonly CameraShake _shake;
    private readonly ArmsSway _sway;
    private readonly float _baseY;
    private float _crouchOffset;
    private Vector2 _targetTilt;
    private Tween _tiltTween;

    public Observable<bool> Footstep => _bob.Footstep;

    public PlayerCameraMotion(PlayerView view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        _pivot = view.CameraPivot;
        _tiltRoot = view.TiltRoot;
        _config = view.CameraMotion != null ? view.CameraMotion : throw new ArgumentException($"{nameof(PlayerView)} has no camera config", nameof(view));
        _headBob = TransformOffset.Of(view.HeadBobRoot);
        _camera = TransformOffset.Of(view.CameraRoot);
        _arms = TransformOffset.Of(view.ArmsSwayRoot);
        _bob = new CameraHeadBob(_config);
        _shake = new CameraShake(_config);
        _sway = new ArmsSway(_config.Arms);
        _baseY = _pivot.localPosition.y;

        if (_headBob == null || _camera == null || _arms == null)
            Debug.LogWarning($"{nameof(PlayerCameraMotion)}: {nameof(PlayerView)} lacks HeadBobRoot, CameraRoot or ArmsSwayRoot, the missing layers fall back or stay still", view);
    }

    public void Impact(float strength)
    {
        _shake.Impact(strength);
        _sway.Impact(strength);
    }

    public void Tick(in PlayerMovementResult movement, Vector2 turn, float deltaTime)
    {
        UpdateTilt(movement.Move);
        UpdateCrouchOffset(movement.IsCrouching, deltaTime);
        _bob.Tick(movement, deltaTime);
        _shake.Tick(deltaTime);
        _sway.Tick(movement, turn, _bob, deltaTime);
        Apply();
    }

    private void Apply()
    {
        Vector3 pivot = _pivot.localPosition;
        float pivotBob = _headBob == null && _camera == null ? _bob.Position.y : 0f;
        _pivot.localPosition = new Vector3(pivot.x, _baseY + _crouchOffset + pivotBob, pivot.z);

        if (_headBob != null)
        {
            _headBob.Apply(_bob.Position, _bob.Rotation);
            _camera?.Apply(Vector3.zero, _shake.Rotation);
        }
        else
        {
            _camera?.Apply(_bob.Position, _bob.Rotation + _shake.Rotation);
        }

        _arms?.Apply(_sway.Position, _sway.Rotation);
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

    public void Dispose()
    {
        _tiltTween?.Kill();
        _bob.Dispose();
    }
}
