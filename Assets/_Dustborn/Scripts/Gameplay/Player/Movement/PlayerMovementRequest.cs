using UnityEngine;

public struct PlayerMovementRequest
{
    public Vector2 Move;
    public bool Sprint;
    public bool Jump;
    public bool Crouch;
    public float SpeedMultiplier;

    public static PlayerMovementRequest Idle => new() { SpeedMultiplier = 1f };
}
