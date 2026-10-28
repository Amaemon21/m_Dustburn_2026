using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "BackpackInventoryItem", menuName = "Dustborn/Inventory/Items/Backpack Inventory Item")]
public class BackpackInventoryItem : InventoryItem
{
    [field: SerializeField, BoxGroup("Backpack"), HorizontalLine(2f, EColor.Blue), Min(0)]
    public int ExtraRows { get; private set; } = 1;
    [field: SerializeField, BoxGroup("Backpack"), Min(0f)]
    public float CarryBonus { get; private set; } = 10f;

    protected override ItemCategory DefaultCategory => ItemCategory.Clothing;

    public override void CollectStats(ICollection<ItemStat> stats, InventorySlotState state)
    {
        AddIfSet(stats, ItemStatType.ExtraRows, ExtraRows);
        AddIfSet(stats, ItemStatType.CarryBonus, CarryBonus);
        base.CollectStats(stats, state);
    }
}
