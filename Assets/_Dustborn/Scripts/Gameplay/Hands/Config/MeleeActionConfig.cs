using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MeleeActionConfig : ItemActionConfig
{
    [field: SerializeField, Min(0f)] public float Windup { get; private set; } = 0.37f;
    [field: SerializeField, Min(0f)] public float SwingDuration { get; private set; } = 0.86666673f;
    [field: SerializeField, Min(0f)] public float Recovery { get; private set; } = 0.2f;
    [field: SerializeField, Min(0.1f)] public float Reach { get; private set; } = 2.4f;
    [field: SerializeField, Min(0.01f)] public float Radius { get; private set; } = 0.2f;
    [field: SerializeField, Min(0)] public int Damage { get; private set; } = 25;
    [field: SerializeField, Min(0)] public int WearPerHit { get; private set; } = 1;
    [field: SerializeField, Min(0f)] public float StaminaCost { get; private set; } = 8f;
    [field: SerializeField] public LayerMask HitMask { get; private set; } = Physics.DefaultRaycastLayers;
    [field: SerializeField] public MaterialBonus[] Bonuses { get; private set; } = Array.Empty<MaterialBonus>();

    public float CycleTime => SwingDuration + Recovery;

    public override void CollectStats(ICollection<ItemStat> stats)
    {
        stats.Add(new ItemStat(ItemStatType.Damage, Damage));
        stats.Add(new ItemStat(ItemStatType.SwingTime, CycleTime));

        if (StaminaCost > 0f)
            stats.Add(new ItemStat(ItemStatType.StaminaCost, StaminaCost));

        stats.Add(new ItemStat(ItemStatType.Reach, Reach));
    }

    public override void Validate(UnityEngine.Object owner)
    {
        Windup = Mathf.Clamp(Windup, 0f, SwingDuration);
        Bonuses ??= Array.Empty<MaterialBonus>();

        foreach (MaterialBonus bonus in Bonuses)
        {
            if (bonus != null && bonus.IsUnset)
                bonus.ApplyDefaults();
        }

        ListDuplicates.Clear(Bonuses, bonus => bonus.Material, index => Bonuses[index].ClearMaterial(), owner, "Bonuses");
    }
}
