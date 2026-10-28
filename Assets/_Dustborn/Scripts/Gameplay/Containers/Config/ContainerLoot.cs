using System;
using UnityEngine;
using Random = System.Random;

[Serializable]
public sealed class ContainerLoot
{
    [field: SerializeField] public InventoryItem Item { get; private set; }
    [field: SerializeField, Min(1)] public int Amount { get; private set; } = 1;
    [field: SerializeField, Min(0)] public int MaxAmount { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float Chance { get; private set; } = 1f;

    public ContainerLoot()
    {
    }

    public ContainerLoot(InventoryItem item, int amount, int maxAmount, float chance)
    {
        Item = item;
        Amount = amount;
        MaxAmount = maxAmount;
        Chance = chance;
    }

    public int Roll(Random random)
    {
        if (Chance < 1f && random.NextDouble() >= Chance)
            return 0;

        return MaxAmount > Amount ? random.Next(Amount, MaxAmount + 1) : Amount;
    }

    public void ClearItem() => Item = null;
}
