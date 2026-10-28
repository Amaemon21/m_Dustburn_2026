using System;
using UnityEngine;
using Random = System.Random;

public sealed class CameraShake
{
    private const float NOISE_AXIS_OFFSET = 37.13f;
    private const float MIN_SIDE = 0.5f;

    private readonly PlayerCameraConfig _config;
    private readonly Random _random = new();
    private readonly float _seed;
    private SpringVector3 _kick;
    private float _trauma;
    private float _time;

    public Vector3 Rotation { get; private set; }

    public CameraShake(PlayerCameraConfig config)
    {
        _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        _seed = (float)_random.NextDouble() * 100f;
    }

    public void Impact(float strength)
    {
        if (strength <= 0f)
            return;

        strength = Mathf.Clamp01(strength);
        Vector3 direction = new(1f, Side(), Side());
        _kick.Velocity += Vector3.Scale(_config.KickAngles, direction) * strength;
        _trauma = Mathf.Min(1f, _trauma + strength * _config.TraumaPerImpact);
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        _time += deltaTime;
        _kick.Step(Vector3.zero, _config.KickFrequency, _config.KickDamping, deltaTime);
        _trauma = Mathf.Max(0f, _trauma - _config.TraumaDecay * deltaTime);

        float shake = _trauma * _trauma;
        Vector3 noise = new(Noise(0), Noise(1), Noise(2));
        Rotation = _kick.Value + Vector3.Scale(_config.NoiseAngles, noise) * shake;
    }

    private float Side()
    {
        float magnitude = Mathf.Lerp(MIN_SIDE, 1f, (float)_random.NextDouble());
        return _random.Next(2) == 0 ? -magnitude : magnitude;
    }

    private float Noise(int axis)
        => Mathf.PerlinNoise(_seed + axis * NOISE_AXIS_OFFSET, _time * _config.NoiseFrequency) * 2f - 1f;
}
