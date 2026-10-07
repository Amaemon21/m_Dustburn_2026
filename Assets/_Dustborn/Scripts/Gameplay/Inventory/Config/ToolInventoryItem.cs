using UnityEngine;

[CreateAssetMenu(fileName = "ToolInventoryItem", menuName = "Dustborn/Inventory/Items/Tool Inventory Item")]
public class ToolInventoryItem : InventoryItem
{
    protected override ItemCategory DefaultCategory => ItemCategory.Tool;
}
