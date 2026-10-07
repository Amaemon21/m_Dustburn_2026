using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MaterialBonus
{
    [field: SerializeField] public BlockMaterial Material { get; private set; }
    [field: SerializeField, Min(0f)] public float Damage { get; private set; } = 1f;
    [field: SerializeField, Min(0f)] public float Harvest { get; private set; } = 1f;

    public bool IsUnset => Material == null && Damage == 0f && Harvest == 0f;

    public MaterialBonus()
    {
    }

    public MaterialBonus(BlockMaterial material, float damage, float harvest)
    {
        Material = material;
        Damage = damage;
        Harvest = harvest;
    }

    public void ApplyDefaults()
    {
        Damage = 1f;
        Harvest = 1f;
    }

    public static MaterialBonus Find(IReadOnlyList<MaterialBonus> bonuses, BlockMaterial material)
    {
        if (bonuses == null || material == null)
            return null;

        for (int i = 0; i < bonuses.Count; i++)
            if (bonuses[i] != null && ReferenceEquals(bonuses[i].Material, material))
                return bonuses[i];

        return null;
    }
}
