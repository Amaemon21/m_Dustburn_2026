using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "PlayerInventorySettings", menuName = "Dustborn/Inventory/Settings/Player Inventory Settings")]
public class PlayerInventorySettings : InventorySetting
{
    [field: SerializeField, BoxGroup("Hotbar"), Label("Slot Count"), HorizontalLine(2f, EColor.Blue), Min(1)]
    [field: FormerlySerializedAs("HotbarSize")]
    public int HotbarSize { get; private set; } = 7;
}
