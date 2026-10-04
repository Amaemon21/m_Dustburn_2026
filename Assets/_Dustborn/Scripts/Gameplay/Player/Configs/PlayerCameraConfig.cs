using DG.Tweening;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerCameraConfig", menuName = "Dustborn/Player/Camera Config")]
public sealed class PlayerCameraConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Tilt"), HorizontalLine(2f, EColor.Blue)] public float TiltForward { get; private set; } = 3f;
    [field: SerializeField, BoxGroup("Tilt")] public float TiltSide { get; private set; } = 3f;
    [field: SerializeField, BoxGroup("Tilt"), Min(0f)] public float TiltDuration { get; private set; } = 0.2f;
    [field: SerializeField, BoxGroup("Tilt")] public Ease TiltEase { get; private set; } = Ease.OutSine;

    [field: SerializeField, Foldout("Bob"), Min(0f)] public float WalkBobSpeed { get; private set; } = 14f;
    [field: SerializeField, Foldout("Bob"), Min(0f)] public float WalkBobAmount { get; private set; } = 0.05f;
    [field: SerializeField, Foldout("Bob"), Min(0f)] public float SprintBobSpeed { get; private set; } = 18f;
    [field: SerializeField, Foldout("Bob"), Min(0f)] public float SprintBobAmount { get; private set; } = 0.08f;
    [field: SerializeField, Foldout("Bob"), Min(0f)] public float CrouchBobSpeed { get; private set; } = 8f;
    [field: SerializeField, Foldout("Bob"), Min(0f)] public float CrouchBobAmount { get; private set; } = 0.03f;
    [field: SerializeField, Foldout("Bob"), Range(0f, 1f)] public float MoveThreshold { get; private set; } = 0.1f;

    [field: SerializeField, BoxGroup("Crouch"), HorizontalLine(2f, EColor.Green)] public float CrouchCameraOffset { get; private set; } = -0.6f;
    [field: SerializeField, BoxGroup("Crouch"), Min(0f)] public float CrouchTransitionSpeed { get; private set; } = 8f;
}
