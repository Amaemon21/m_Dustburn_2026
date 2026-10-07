using System.Collections.Generic;
using UnityEngine;

public readonly struct StrikeInfo
{
    public int Damage { get; }
    public int Wear { get; }
    public IReadOnlyList<MaterialBonus> Bonuses { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }

    public StrikeInfo(int damage, int wear, IReadOnlyList<MaterialBonus> bonuses, Vector3 point, Vector3 normal)
    {
        Damage = damage;
        Wear = wear;
        Bonuses = bonuses;
        Point = point;
        Normal = normal;
    }
}
