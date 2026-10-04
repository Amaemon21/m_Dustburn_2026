using UnityEngine;

public sealed class PlayerLook
{
    private readonly Transform _body;
    private readonly Transform _pitchPivot;
    private readonly PlayerMovementConfig _config;
    private float _pitch;

    public PlayerLook(Transform body, Transform pitchPivot, PlayerMovementConfig config)
    {
        _body = body;
        _pitchPivot = pitchPivot;
        _config = config;
        _pitch = Mathf.DeltaAngle(0f, pitchPivot.localEulerAngles.x);
    }

    public void Tick(Vector2 delta)
    {
        Vector2 scaled = delta * _config.LookSensitivity;
        _body.Rotate(Vector3.up * scaled.x);
        _pitch = Mathf.Clamp(_pitch - scaled.y, -_config.PitchLimit, _config.PitchLimit);
        _pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
