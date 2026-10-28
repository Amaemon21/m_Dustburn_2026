using System;
using UnityEngine;

public sealed class DragSwing
{
    private readonly DragSwingSettings _settings;
    private SpringFloat _angle;
    private SpringFloat _scale = new(1f);
    private Vector2 _lastPosition;
    private Vector2 _velocity;
    private bool _hasPosition;
    private float _targetAngle;

    public float Angle => _angle.Value;
    public float Scale => _scale.Value;

    public bool IsResting
        => Mathf.Abs(_targetAngle) < _settings.RestEpsilon &&
           _angle.IsResting(0f, _settings.RestEpsilon) &&
           _scale.IsResting(_settings.HeldScale, _settings.RestEpsilon);

    public DragSwing(DragSwingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Grab()
    {
        _angle = new SpringFloat(0f);
        _scale = new SpringFloat(1f);
        _velocity = Vector2.zero;
        _targetAngle = 0f;
        _hasPosition = false;
    }

    public void Tick(Vector2 position, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector2 raw = _hasPosition ? (position - _lastPosition) / deltaTime : Vector2.zero;
        _lastPosition = position;
        _hasPosition = true;

        _velocity = Vector2.Lerp(_velocity, raw, 1f - Mathf.Exp(-_settings.VelocitySmoothing * deltaTime));
        _targetAngle = Mathf.Clamp(-_velocity.x * _settings.TiltPerSpeed, -_settings.MaxTilt, _settings.MaxTilt);
        _angle.Step(_targetAngle, _settings.TiltFrequency, _settings.TiltDamping, deltaTime);
        _scale.Step(_settings.HeldScale, _settings.ScaleFrequency, _settings.ScaleDamping, deltaTime);
    }
}
