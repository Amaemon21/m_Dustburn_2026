using System;
using UnityEngine;

[Serializable]
public sealed class ArmsSwaySettings
{
    [field: SerializeField, Range(0f, 0.05f)] public float LookLag { get; private set; } = 0.005f;
    [field: SerializeField, Min(0f)] public float MaxLookAngle { get; private set; } = 6f;
    [field: SerializeField, Min(0f)] public float LookRoll { get; private set; } = 0.6f;
    [field: SerializeField, Min(0f)] public float LookShift { get; private set; } = 0.004f;
    [field: SerializeField, Min(0.01f)] public float LookFrequency { get; private set; } = 5f;
    [field: SerializeField, Range(0f, 2f)] public float LookDamping { get; private set; } = 0.7f;

    [field: SerializeField, Min(0f)] public float BobSway { get; private set; } = 0.012f;
    [field: SerializeField, Min(0f)] public float BobHeight { get; private set; } = 0.008f;
    [field: SerializeField, Min(0f)] public float BobRoll { get; private set; } = 0.8f;
    [field: SerializeField, Range(0f, 3.14f)] public float BobLag { get; private set; } = 0.35f;

    [field: SerializeField, Min(0f)] public float StrafeRoll { get; private set; } = 2.5f;
    [field: SerializeField, Min(0f)] public float ForwardPull { get; private set; } = 0.012f;
    [field: SerializeField, Min(0.01f)] public float MoveFrequency { get; private set; } = 4f;
    [field: SerializeField, Range(0f, 2f)] public float MoveDamping { get; private set; } = 0.8f;

    [field: SerializeField, Min(0f)] public float LandKick { get; private set; } = 1.2f;
    [field: SerializeField, Min(0f)] public float LandPitch { get; private set; } = 25f;
    [field: SerializeField, Min(0.01f)] public float LandFrequency { get; private set; } = 4.5f;
    [field: SerializeField, Range(0f, 2f)] public float LandDamping { get; private set; } = 0.45f;

    [field: SerializeField] public Vector3 RecoilAngles { get; private set; } = new(-90f, 20f, 30f);
    [field: SerializeField] public float RecoilPush { get; private set; } = -0.25f;
    [field: SerializeField, Min(0.01f)] public float RecoilFrequency { get; private set; } = 9f;
    [field: SerializeField, Range(0f, 2f)] public float RecoilDamping { get; private set; } = 0.5f;
}
