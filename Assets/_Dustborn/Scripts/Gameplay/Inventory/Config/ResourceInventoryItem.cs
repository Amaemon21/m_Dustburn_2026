using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "ResourceInventoryItem", menuName = "Dustborn/Inventory/Items/Resource Inventory Item")]
public class ResourceInventoryItem : InventoryItem
{
    [field: SerializeField, BoxGroup("Resource"), HorizontalLine(2f, EColor.Orange)]
    public ResourceCategory ResourceCategory { get; private set; } = ResourceCategory.Raw;

    protected override ItemCategory DefaultCategory => ItemCategory.Resource;
}
