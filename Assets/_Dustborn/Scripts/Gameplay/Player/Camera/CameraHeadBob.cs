using System;
using R3;
using UnityEngine;

public sealed class CameraHeadBob : IDisposable
{
    private const float FULL_STRIDE = 2f * Mathf.PI;
    private const float LEFT_STEP = 0.5f * Mathf.PI;
    private const float RIGHT_STEP = 1.5f * Mathf.PI;
    private const float REST_WEIGHT = 0.001f;

    private readonly PlayerCameraConfig _config;
    private readonly Subject<bool> _footstep = new();
    private float _phase;
    private float _weight;
    private float _stepRate;
    private float _height;
    private float _sway;
    private float _roll;
    private float _nod;
    private float _breath;
    private SpringFloat _land;

    public Vector3 Position { get; private set; }
    public Vector3 Rotation { get; private set; }
    public float Phase => _phase;
    public float Weight => _weight;
    public Observable<bool> Footstep => _footstep;

    public CameraHeadBob(PlayerCameraConfig config)
    {
        _config = config != null ? config : throw new ArgumentNullException(nameof(config));
    }

    public void Tick(in PlayerMovementResult movement, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        bool walking = movement.IsGrounded && IsMoving(movement.Move);
        BlendProfile(Profile(movement), walking, deltaTime);
        if (walking)
            Advance(movement.IsCrouching, deltaTime);
        else if (_weight < REST_WEIGHT)
            _phase = 0f;

        if (movement.Landed)
            _land.Velocity -= _config.LandKick;
        _land.Step(0f, _config.LandFrequency, _config.LandDamping, deltaTime);
        _breath = (_breath + deltaTime * _config.BreathRate) % 1f;

        Compose();
    }

    private bool IsMoving(Vector2 move)
    {
        float threshold = _config.MoveThreshold;
        return Mathf.Abs(move.x) > threshold || Mathf.Abs(move.y) > threshold;
    }

    private HeadBobProfile Profile(in PlayerMovementResult movement)
        => movement.IsCrouching ? _config.Crouch : movement.IsSprinting ? _config.Sprint : _config.Walk;

    private void BlendProfile(HeadBobProfile profile, bool walking, float deltaTime)
    {
        float t = 1f - Mathf.Exp(-_config.BobBlendSpeed * deltaTime);
        _weight = Mathf.Lerp(_weight, walking ? 1f : 0f, t);
        _stepRate = Mathf.Lerp(_stepRate, profile.StepRate, t);
        _height = Mathf.Lerp(_height, profile.Height, t);
        _sway = Mathf.Lerp(_sway, profile.Sway, t);
        _roll = Mathf.Lerp(_roll, profile.Roll, t);
        _nod = Mathf.Lerp(_nod, profile.Nod, t);
    }

    private void Advance(bool crouching, float deltaTime)
    {
        float before = _phase;
        _phase += Mathf.PI * _stepRate * deltaTime;
        if (!crouching)
        {
            if (Crossed(before, _phase, LEFT_STEP))
                _footstep.OnNext(true);
            if (Crossed(before, _phase, RIGHT_STEP))
                _footstep.OnNext(false);
        }

        if (_phase >= FULL_STRIDE)
            _phase -= FULL_STRIDE;
    }

    private static bool Crossed(float before, float after, float mark)
        => (before < mark && after >= mark) || (before < mark + FULL_STRIDE && after >= mark + FULL_STRIDE);

    private void Compose()
    {
        float step = StepCurve(_phase) * _weight;
        float side = Mathf.Sin(_phase) * _weight;
        float breath = Mathf.Sin(_breath * FULL_STRIDE) * _config.BreathHeight * (1f - _weight);

        Position = new Vector3(side * _sway, breath - step * _height + _land.Value, 0f);
        Rotation = new Vector3(step * _nod - _land.Value * _config.LandPitch, 0f, -side * _roll);
    }

    private float StepCurve(float phase)
    {
        float smooth = 0.5f - 0.5f * Mathf.Cos(2f * phase);
        float sharp = 1f - Mathf.Abs(Mathf.Cos(phase));
        return Mathf.Lerp(smooth, sharp, _config.StepSharpness);
    }

    public void Dispose() => _footstep.Dispose();
}
