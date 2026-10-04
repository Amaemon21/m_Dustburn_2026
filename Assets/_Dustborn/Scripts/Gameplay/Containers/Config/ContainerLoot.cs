using System;
using UnityEngine;

[Serializable]
public sealed class ContainerLoot
{
    [field: SerializeField] public InventoryItem Item { get; private set; }
    [field: SerializeField, Min(1)] public int Amount { get; private set; } = 1;
}
