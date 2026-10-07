using System;
using UnityEngine;

[Serializable]
public sealed class MeleeActionConfig : ItemActionConfig
{
    [field: SerializeField] public HandsMotion Motion { get; private set; } = HandsMotion.Attack;
    [field: SerializeField, Min(0.05f)] public float Duration { get; private set; } = 1.1f;
    [field: SerializeField, Range(0f, 1f)] public float HitTime { get; private set; } = 0.45f;
    [field: SerializeField, Min(0.1f)] public float Reach { get; private set; } = 2.4f;
    [field: SerializeField, Min(0.01f)] public float Radius { get; private set; } = 0.2f;
    [field: SerializeField, Min(0)] public int Damage { get; private set; } = 25;
    [field: SerializeField, Min(0)] public int WearPerHit { get; private set; } = 1;
    [field: SerializeField] public LayerMask HitMask { get; private set; } = Physics.DefaultRaycastLayers;
    [field: SerializeField] public MaterialBonus[] Bonuses { get; private set; } = Array.Empty<MaterialBonus>();

    public override void Validate()
    {
        Bonuses ??= Array.Empty<MaterialBonus>();
        foreach (MaterialBonus bonus in Bonuses)
            if (bonus != null && bonus.IsUnset)
                bonus.ApplyDefaults();
    }
}
