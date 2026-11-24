using System;
using UnityEngine;

public sealed class ConsumableUseService : IItemUseService
{
    private readonly IInventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly PlayerVitals _vitals;
    private readonly PlayerEffects _effects;

    public ConsumableUseService(IInventoryService inventory, ItemCatalog catalog, PlayerVitals vitals, PlayerEffects effects)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public bool Use(string ownerId, Vector2Int coordinates)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || !_inventory.TryGetInventory(ownerId, out IReadOnlyInventoryGrid grid))
            return false;

        if (!InventoryGridLayout.Contains(coordinates, grid.Size))
            return false;

        InventorySlotState state = grid.GetSlot(coordinates).State.CurrentValue;

        if (state.IsEmpty || _catalog.GetItem(state.ItemId) is not ConsumableInventoryItem consumable)
            return false;

        if (!_inventory.RemoveItemsFromInventory(ownerId, coordinates, state.ItemId).Success)
            return false;

        Apply(consumable);
        return true;
    }

    private void Apply(ConsumableInventoryItem item)
    {
        bool healsOverTime = item.Health > 0f && float.IsFinite(item.Duration) && item.Duration > 0f;
        _vitals.Apply(new VitalsEffect(healsOverTime ? 0f : item.Health, item.Stamina, item.Food, item.Water, item.Infection));

        if (item.Bleeding < 0f)
            _effects.ReduceBleeding(-item.Bleeding);
        else if (item.Bleeding > 0f)
            _effects.ApplyBleeding(item.Bleeding, item.Duration);

        if (healsOverTime)
            _effects.ApplyHealing(item.Health, item.Duration);

        if (item is MedicineInventoryItem medicine)
            _effects.ApplyProtection(medicine.Protection / 100f, medicine.ProtectionDuration);
    }
}
