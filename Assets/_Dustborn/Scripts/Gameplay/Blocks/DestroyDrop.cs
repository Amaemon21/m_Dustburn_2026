using System;
using UnityEngine;

[Serializable]
public sealed class DestroyDrop
{
    [field: SerializeField] public InventoryItem Item { get; private set; }
    [field: SerializeField, Min(0)] public int MinCount { get; private set; } = 1;
    [field: SerializeField, Min(0)] public int MaxCount { get; private set; } = 1;
    [field: SerializeField, Range(0f, 1f)] public float Chance { get; private set; } = 1f;

    public bool IsUnset => Item == null && MinCount == 0 && MaxCount == 0 && Chance == 0f;

    public DestroyDrop()
    {
    }

    public DestroyDrop(InventoryItem item, int minCount, int maxCount, float chance)
    {
        Item = item;
        MinCount = minCount;
        MaxCount = maxCount;
        Chance = chance;
    }

    public void ApplyDefaults()
    {
        MinCount = 1;
        MaxCount = 1;
        Chance = 1f;
    }

    public void Validate()
    {
        if (MaxCount < MinCount)
            MaxCount = MinCount;
    }
}
