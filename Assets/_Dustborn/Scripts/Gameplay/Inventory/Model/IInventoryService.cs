using UnityEngine;

public interface IInventoryService
{
    IReadOnlyInventoryGrid RegisterInventory(InventoryGridData data);
    IReadOnlyInventoryGrid GetInventory(string ownerId);
    bool TryGetInventory(string ownerId, out IReadOnlyInventoryGrid inventory);
    AddItemsToInventoryGridResult AddItemsToInventory(string ownerId, string itemId, int amount = 1);
    AddItemsToInventoryGridResult AddItemsToExistingStacks(string ownerId, string itemId, int amount);
    AddItemsToInventoryGridResult AddItemsToInventory(string ownerId, Vector2Int coords, string itemId, int amount = 1);
    RemoveItemsFromInventoryGridResult RemoveItemsFromInventory(string ownerId, string itemId, int amount = 1);
    RemoveItemsFromInventoryGridResult RemoveItemsFromInventory(string ownerId, Vector2Int coords, string itemId, int amount = 1);
    bool Has(string ownerId, string itemId, int amount = 1);
    void SwitchSlots(string ownerId, Vector2Int first, Vector2Int second);
    void SetSize(string ownerId, Vector2Int size);
    bool MoveItems(string sourceId, Vector2Int source, string targetId, Vector2Int target, int amount = 1);
    bool SwapSlots(string firstId, Vector2Int first, string secondId, Vector2Int second);
    bool AddWear(string ownerId, Vector2Int coords, string itemId, int amount);
}
