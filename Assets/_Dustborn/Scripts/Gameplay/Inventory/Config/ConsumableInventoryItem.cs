using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public abstract class ConsumableInventoryItem : InventoryItem
{
    [field: SerializeField, BoxGroup("Effect"), HorizontalLine(2f, EColor.Red)] public float Health { get; private set; }
    [field: SerializeField, BoxGroup("Effect")] public float Bleeding { get; private set; }
    [field: SerializeField, BoxGroup("Effect"), Min(0f)] public float Duration { get; private set; }

    public override bool IsUsable => true;

    public override void CollectStats(ICollection<ItemStat> stats, InventorySlotState state)
    {
        AddIfSet(stats, ItemStatType.Health, Health);
        AddIfSet(stats, ItemStatType.Bleeding, Bleeding);
        AddIfSet(stats, ItemStatType.Duration, Duration);
        base.CollectStats(stats, state);
    }
}
