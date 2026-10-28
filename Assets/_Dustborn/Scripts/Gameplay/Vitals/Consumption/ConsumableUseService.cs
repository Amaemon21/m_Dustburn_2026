using System;
using UnityEngine;

public sealed class ConsumableUseService : IItemUseService
{
    private readonly IInventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly PlayerVitals _vitals;

    public ConsumableUseService(IInventoryService inventory, ItemCatalog catalog, PlayerVitals vitals)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
    }

    public bool Use(string ownerId, Vector2Int coordinates)
    {
        if (!_inventory.TryGetInventory(ownerId, out IReadOnlyInventoryGrid grid))
            return false;

        InventorySlotState state = grid.GetSlot(coordinates).State.CurrentValue;
        if (state.IsEmpty || !(_catalog.GetItem(state.ItemId) is ConsumableInventoryItem consumable))
            return false;
        if (!_inventory.RemoveItemsFromInventory(ownerId, coordinates, state.ItemId).Success)
            return false;

        _vitals.Apply(EffectOf(consumable));
        return true;
    }

    private static VitalsEffect EffectOf(ConsumableInventoryItem item)
        => new(item.Health, item.Stamina, item.Food, item.Water, item.Infection);
}
