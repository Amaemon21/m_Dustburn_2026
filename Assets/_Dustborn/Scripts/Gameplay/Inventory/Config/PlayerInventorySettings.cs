using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "PlayerInventorySettings", menuName = "Dustborn/Inventory/Settings/Player Inventory Settings")]
public class PlayerInventorySettings : InventorySetting
{
    [field: SerializeField, BoxGroup("Hotbar"), Label("Slot Count"), HorizontalLine(2f, EColor.Blue), Min(1)]
    [field: FormerlySerializedAs("HotbarSize")]
    public int HotbarSize { get; private set; } = 7;

    [field: SerializeField, BoxGroup("Drop"), HorizontalLine(2f, EColor.Orange), Min(0f)]
    public float DropDistance { get; private set; } = 1.2f;
    [field: SerializeField, BoxGroup("Drop")] public LayerMask DropMask { get; private set; } = ~0;
}
