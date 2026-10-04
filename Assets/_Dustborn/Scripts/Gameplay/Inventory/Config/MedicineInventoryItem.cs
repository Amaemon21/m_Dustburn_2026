using UnityEngine;

[CreateAssetMenu(fileName = "MedicineInventoryItem", menuName = "Dustborn/Inventory/Items/Medicine Inventory Item")]
public class MedicineInventoryItem : ConsumableInventoryItem
{
    protected override ItemCategory DefaultCategory => ItemCategory.Medicine;
}
