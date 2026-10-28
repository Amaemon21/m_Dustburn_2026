using System.Collections.Generic;
using UnityEngine;

public readonly struct DamageHit
{
    public int Damage { get; }
    public IReadOnlyList<MaterialBonus> Bonuses { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }

    public DamageHit(int damage, IReadOnlyList<MaterialBonus> bonuses, Vector3 point, Vector3 normal)
    {
        Damage = damage;
        Bonuses = bonuses;
        Point = point;
        Normal = normal;
    }
}
