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

    [field: SerializeField, Foldout("Head Bob")] public HeadBobProfile Walk { get; private set; } = new(2.2f, 0.045f, 0.025f, 0.6f, 0.4f);
    [field: SerializeField, Foldout("Head Bob")] public HeadBobProfile Sprint { get; private set; } = new(2.85f, 0.075f, 0.04f, 1.1f, 0.8f);
    [field: SerializeField, Foldout("Head Bob")] public HeadBobProfile Crouch { get; private set; } = new(1.3f, 0.025f, 0.015f, 0.35f, 0.2f);
    [field: SerializeField, Foldout("Head Bob"), Range(0f, 1f)] public float StepSharpness { get; private set; } = 0.35f;
    [field: SerializeField, Foldout("Head Bob"), Min(0f)] public float BobBlendSpeed { get; private set; } = 6f;
    [field: SerializeField, Foldout("Head Bob"), Range(0f, 1f)] public float MoveThreshold { get; private set; } = 0.1f;
    [field: SerializeField, Foldout("Head Bob"), Min(0f)] public float BreathHeight { get; private set; } = 0.004f;
    [field: SerializeField, Foldout("Head Bob"), Min(0f)] public float BreathRate { get; private set; } = 0.25f;

    [field: SerializeField, Foldout("Landing"), Min(0f)] public float LandKick { get; private set; } = 0.9f;
    [field: SerializeField, Foldout("Landing"), Min(0f)] public float LandPitch { get; private set; } = 12f;
    [field: SerializeField, Foldout("Landing"), Min(0.01f)] public float LandFrequency { get; private set; } = 4f;
    [field: SerializeField, Foldout("Landing"), Range(0f, 2f)] public float LandDamping { get; private set; } = 0.45f;

    [field: SerializeField, Foldout("Impact Shake")] public Vector3 KickAngles { get; private set; } = new(60f, 25f, 40f);
    [field: SerializeField, Foldout("Impact Shake"), Min(0.01f)] public float KickFrequency { get; private set; } = 7f;
    [field: SerializeField, Foldout("Impact Shake"), Range(0f, 2f)] public float KickDamping { get; private set; } = 0.5f;
    [field: SerializeField, Foldout("Impact Shake")] public Vector3 NoiseAngles { get; private set; } = new(0.6f, 0.6f, 0.8f);
    [field: SerializeField, Foldout("Impact Shake"), Min(0f)] public float NoiseFrequency { get; private set; } = 18f;
    [field: SerializeField, Foldout("Impact Shake"), Range(0f, 1f)] public float TraumaPerImpact { get; private set; } = 0.45f;
    [field: SerializeField, Foldout("Impact Shake"), Min(0f)] public float TraumaDecay { get; private set; } = 1.8f;

    [field: SerializeField, Foldout("Arms Sway")] public ArmsSwaySettings Arms { get; private set; } = new();

    [field: SerializeField, BoxGroup("Crouch"), HorizontalLine(2f, EColor.Green)] public float CrouchCameraOffset { get; private set; } = -0.6f;
    [field: SerializeField, BoxGroup("Crouch"), Min(0f)] public float CrouchTransitionSpeed { get; private set; } = 8f;
}
