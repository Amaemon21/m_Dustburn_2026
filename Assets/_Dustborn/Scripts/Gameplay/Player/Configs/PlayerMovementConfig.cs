using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Dustborn/Player/Movement Config")]
public sealed class PlayerMovementConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Speed"), HorizontalLine(2f, EColor.Blue), Min(0f)] public float WalkSpeed { get; private set; } = 4.5f;
    [field: SerializeField, BoxGroup("Speed"), Min(1f)] public float SprintMultiplier { get; private set; } = 1.8f;
    [field: SerializeField, BoxGroup("Speed"), Min(0f)] public float CrouchSpeed { get; private set; } = 2f;

    [field: SerializeField, BoxGroup("Jump"), HorizontalLine(2f, EColor.Green), Min(0f)] public float JumpHeight { get; private set; } = 1.1f;
    [field: SerializeField, BoxGroup("Jump")] public float Gravity { get; private set; } = -20f;
    [field: SerializeField, BoxGroup("Jump"), Range(0f, 90f)] public float AirSlopeLimit { get; private set; }
    [field: SerializeField, BoxGroup("Jump"), Min(0f)] public float MinAirTimeToLand { get; private set; } = 0.4f;

    [field: SerializeField, Foldout("Crouch"), Min(0.1f)] public float StandingHeight { get; private set; } = 1.8f;
    [field: SerializeField, Foldout("Crouch")] public Vector3 StandingCenter { get; private set; } = new(0f, 0.9f, 0f);
    [field: SerializeField, Foldout("Crouch"), Min(0.1f)] public float CrouchHeight { get; private set; } = 1.1f;
    [field: SerializeField, Foldout("Crouch"), Min(0f)] public float CrouchTransitionTime { get; private set; } = 0.25f;
    [field: SerializeField, Foldout("Crouch")] public LayerMask CeilingMask { get; private set; } = ~0;

    [field: SerializeField, BoxGroup("Look"), HorizontalLine(2f, EColor.Orange), Min(0f)] public float LookSensitivity { get; private set; } = 0.15f;
    [field: SerializeField, BoxGroup("Look"), Range(0f, 90f)] public float PitchLimit { get; private set; } = 89f;
}
