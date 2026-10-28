using System;
using UnityEngine;

[Serializable]
public sealed class HeadBobProfile
{
    [field: SerializeField, Min(0f)] public float StepRate { get; private set; }
    [field: SerializeField, Min(0f)] public float Height { get; private set; }
    [field: SerializeField, Min(0f)] public float Sway { get; private set; }
    [field: SerializeField, Min(0f)] public float Roll { get; private set; }
    [field: SerializeField, Min(0f)] public float Nod { get; private set; }

    public HeadBobProfile()
    {
    }

    public HeadBobProfile(float stepRate, float height, float sway, float roll, float nod)
    {
        StepRate = stepRate;
        Height = height;
        Sway = sway;
        Roll = roll;
        Nod = nod;
    }
}
