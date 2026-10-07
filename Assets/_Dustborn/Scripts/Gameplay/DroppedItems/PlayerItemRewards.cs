using UnityEngine;

public sealed class PlayerItemRewards : IItemRewards
{
    private readonly PlayerInventoryService _players;
    private readonly LocalPlayerInventory _player;
    private readonly ItemCatalog _catalog;
    private readonly DroppedItemService _droppedItems;

    public PlayerItemRewards(PlayerInventoryService players, LocalPlayerInventory player, ItemCatalog catalog, DroppedItemService droppedItems)
    {
        _players = players;
        _player = player;
        _catalog = catalog;
        _droppedItems = droppedItems;
    }

    public void Give(InventorySlotState stack, Vector3 dropOrigin)
    {
        if (stack.IsEmpty || string.IsNullOrWhiteSpace(stack.ItemId))
            return;

        int left = stack.Amount - _players.PickUp(_player.OwnerId, stack);
        if (left <= 0)
            return;

        InventoryItem item = _catalog.GetItem(stack.ItemId);
        if (item == null || item.PickUpPrefab == null)
        {
            Debug.LogWarning($"{nameof(PlayerItemRewards)}: the inventory is full and '{stack.ItemId}' has no {nameof(InventoryItem.PickUpPrefab)}, {left} of it are lost");
            return;
        }

        _droppedItems.Drop(item, stack.WithAmount(left), dropOrigin, Quaternion.identity);
    }
}
