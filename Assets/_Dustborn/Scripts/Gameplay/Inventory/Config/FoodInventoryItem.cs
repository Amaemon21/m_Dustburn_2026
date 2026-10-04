using UnityEngine;

[CreateAssetMenu(fileName = "FoodInventoryItem", menuName = "Dustborn/Inventory/Items/Food Inventory Item")]
public class FoodInventoryItem : ConsumableInventoryItem
{
    protected override ItemCategory DefaultCategory => ItemCategory.Food;
}
