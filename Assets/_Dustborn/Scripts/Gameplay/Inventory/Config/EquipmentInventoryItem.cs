using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "EquipmentInventoryItem", menuName = "Dustborn/Inventory/Items/Equipment Inventory Item")]
public class EquipmentInventoryItem : InventoryItem
{
    [field: SerializeField, BoxGroup("Equipment"), Label("Equipment Slot"), HorizontalLine(2f, EColor.Blue)]
    [field: FormerlySerializedAs("<EquipmentSlots>k__BackingField")]
    public EquipmentSlot EquipmentSlot { get; private set; }

    protected override ItemCategory DefaultCategory => ItemCategory.Clothing;
}
