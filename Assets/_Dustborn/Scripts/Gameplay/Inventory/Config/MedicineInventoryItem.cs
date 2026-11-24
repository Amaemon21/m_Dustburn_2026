using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "MedicineInventoryItem", menuName = "Dustborn/Inventory/Items/Medicine Inventory Item")]
public class MedicineInventoryItem : ConsumableInventoryItem
{
    [field: SerializeField, BoxGroup("Effect"), Range(0f, 100f)] public float Protection { get; private set; }
    [field: SerializeField, BoxGroup("Effect"), Min(0f)] public float ProtectionDuration { get; private set; }

    protected override ItemCategory DefaultCategory => ItemCategory.Medicine;

    public override void CollectStats(ICollection<ItemStat> stats, InventorySlotState state)
    {
        base.CollectStats(stats, state);

        if (Protection <= 0f || ProtectionDuration <= 0f)
            return;

        AddIfSet(stats, ItemStatType.Protection, Protection);
        AddIfSet(stats, ItemStatType.ProtectionDuration, ProtectionDuration);
    }
}
