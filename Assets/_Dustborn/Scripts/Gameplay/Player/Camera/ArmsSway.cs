using System;
using UnityEngine;
using Random = System.Random;

public sealed class ArmsSway
{
    private readonly ArmsSwaySettings _settings;
    private readonly Random _random = new();
    private SpringVector3 _look;
    private SpringFloat _roll;
    private SpringFloat _pull;
    private SpringFloat _land;
    private SpringVector3 _recoil;
    private SpringFloat _push;

    public Vector3 Position { get; private set; }
    public Vector3 Rotation { get; private set; }

    public ArmsSway(ArmsSwaySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Impact(float strength)
    {
        if (strength <= 0f)
            return;

        strength = Mathf.Clamp01(strength);
        float side = _random.Next(2) == 0 ? -1f : 1f;
        _recoil.Velocity += Vector3.Scale(_settings.RecoilAngles, new Vector3(1f, side, side)) * strength;
        _push.Velocity += _settings.RecoilPush * strength;
    }

    public void Tick(in PlayerMovementResult movement, Vector2 turn, CameraHeadBob bob, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        _look.Step(LookTarget(turn, deltaTime), _settings.LookFrequency, _settings.LookDamping, deltaTime);
        _roll.Step(-movement.Move.x * _settings.StrafeRoll, _settings.MoveFrequency, _settings.MoveDamping, deltaTime);
        _pull.Step(-Mathf.Max(0f, movement.Move.y) * _settings.ForwardPull, _settings.MoveFrequency, _settings.MoveDamping, deltaTime);
        if (movement.Landed)
            _land.Velocity -= _settings.LandKick;
        _land.Step(0f, _settings.LandFrequency, _settings.LandDamping, deltaTime);
        _recoil.Step(Vector3.zero, _settings.RecoilFrequency, _settings.RecoilDamping, deltaTime);
        _push.Step(0f, _settings.RecoilFrequency, _settings.RecoilDamping, deltaTime);

        Compose(bob);
    }

    private Vector3 LookTarget(Vector2 turn, float deltaTime)
    {
        Vector2 lag = -turn / deltaTime * _settings.LookLag;
        float limit = _settings.MaxLookAngle;
        return new Vector3(Mathf.Clamp(lag.x, -limit, limit), Mathf.Clamp(lag.y, -limit, limit), 0f);
    }

    private void Compose(CameraHeadBob bob)
    {
        float phase = bob.Phase - _settings.BobLag;
        float weight = bob.Weight;
        float side = Mathf.Sin(phase) * weight;
        float lift = (Mathf.Cos(2f * phase) - 1f) * 0.5f * weight;

        Position = new Vector3(
            side * _settings.BobSway + _look.Value.y * _settings.LookShift,
            lift * _settings.BobHeight + _land.Value,
            _pull.Value + _push.Value);
        Rotation = new Vector3(
            _look.Value.x - _land.Value * _settings.LandPitch + _recoil.Value.x,
            _look.Value.y + _recoil.Value.y,
            _roll.Value + _look.Value.y * _settings.LookRoll - side * _settings.BobRoll + _recoil.Value.z);
    }
}
