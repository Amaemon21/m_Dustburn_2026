using System;
using UnityEngine;

[Serializable]
public sealed class HarvestYield
{
    [field: SerializeField] public InventoryItem Item { get; private set; }
    [field: SerializeField, Min(0)] public int Count { get; private set; } = 1;

    public bool IsUnset => Item == null && Count == 0;

    public HarvestYield()
    {
    }

    public HarvestYield(InventoryItem item, int count)
    {
        Item = item;
        Count = count;
    }

    public void ApplyDefaults() => Count = 1;
}
