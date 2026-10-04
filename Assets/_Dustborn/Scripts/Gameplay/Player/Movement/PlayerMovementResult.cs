using UnityEngine;

public readonly struct PlayerMovementResult
{
    public Vector2 Move { get; }
    public bool IsGrounded { get; }
    public bool IsSprinting { get; }
    public bool IsCrouching { get; }
    public bool Jumped { get; }
    public bool Landed { get; }
    public float PlanarSpeed { get; }

    public bool IsMoving => Move.sqrMagnitude > 0f;

    public PlayerMovementResult(Vector2 move, bool isGrounded, bool isSprinting, bool isCrouching, bool jumped, bool landed,
        float planarSpeed)
    {
        Move = move;
        IsGrounded = isGrounded;
        IsSprinting = isSprinting;
        IsCrouching = isCrouching;
        Jumped = jumped;
        Landed = landed;
        PlanarSpeed = planarSpeed;
    }
}
