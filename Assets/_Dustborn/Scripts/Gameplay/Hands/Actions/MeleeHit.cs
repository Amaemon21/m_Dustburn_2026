using UnityEngine;

public readonly struct MeleeHit
{
    public IDamageable Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }

    public MeleeHit(IDamageable target, Vector3 point, Vector3 normal)
    {
        Target = target;
        Point = point;
        Normal = normal;
    }
}
