using System;
using UnityEngine;

[Serializable]
public class ProgressData
{
    public string SlotName = string.Empty;
    public long CreatedUnixTime;
    public long UpdatedUnixTime;
    public float PlayTimeSeconds;
    public Vector3 PlayerPosition;
    public Quaternion PlayerRotation = Quaternion.identity;
}
