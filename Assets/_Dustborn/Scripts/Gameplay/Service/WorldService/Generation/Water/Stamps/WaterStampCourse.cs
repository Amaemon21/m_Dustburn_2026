using System.Collections.Generic;
using UnityEngine;

public sealed class WaterStampCourse
{
    public List<Vector2> Points;
    public List<float> Areas;
    public float[] WidthScale;
    public float[] DepthScale;
    public WaterStampTrack Track;
    public int Parent = -1;
    public bool HasJoinPoint;
    public Vector2 JoinPoint;
}
