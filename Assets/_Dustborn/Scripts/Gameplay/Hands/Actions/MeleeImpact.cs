using UnityEngine;

public readonly struct MeleeImpact
{
    public Vector3 Point { get; }
    public Vector3 Normal { get; }

    public MeleeImpact(Vector3 point, Vector3 normal)
    {
        Point = point;
        Normal = normal;
    }
}
