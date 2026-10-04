using System;
using UnityEngine;

public sealed class ContainerInventoryService
{
    private const string OWNER_PREFIX = "container/";

    private readonly IInventoryService _inventory;

    public ContainerInventoryService(IInventoryService inventory)
    {
        _inventory = inventory;
    }

    public static string OwnerIdOf(string containerId) => OWNER_PREFIX + containerId;

    public IReadOnlyInventoryGrid GetOrCreate(string containerId, ContainerConfig config)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            throw new ArgumentException("Container id is required", nameof(containerId));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        string ownerId = OwnerIdOf(containerId);
        if (_inventory.TryGetInventory(ownerId, out IReadOnlyInventoryGrid existing))
        {
            if (existing.Kind != InventoryGridKind.Container)
                throw new InvalidOperationException($"Inventory '{ownerId}' exists but is not a container");
            return existing;
        }

        IReadOnlyInventoryGrid grid = _inventory.RegisterInventory(new InventoryGridData
        {
            OwnerId = ownerId,
            Size = config.Size,
            Kind = InventoryGridKind.Container
        });
        FillLoot(ownerId, config);
        return grid;
    }

    public int MoveToPlayer(PlayerInventoryProxy player, string sourceId, Vector2Int coordinates)
    {
        InventorySlotState state = _inventory.GetInventory(sourceId).GetSlot(coordinates).State.CurrentValue;
        if (state.IsEmpty)
            return 0;

        bool hotbarHoldsItem = player.Hotbar.GetAmount(state.ItemId) > 0;
        int moved = Fill(sourceId, coordinates, player.Hotbar, false);
        moved += Fill(sourceId, coordinates, player.Backpack, false);
        if (hotbarHoldsItem)
            moved += Fill(sourceId, coordinates, player.Hotbar, true);
        return moved + Fill(sourceId, coordinates, player.Backpack, true);
    }

    public int MoveToGrid(string sourceId, Vector2Int coordinates, IReadOnlyInventoryGrid target)
    {
        int moved = Fill(sourceId, coordinates, target, false);
        return moved + Fill(sourceId, coordinates, target, true);
    }

    private int Fill(string sourceId, Vector2Int coordinates, IReadOnlyInventoryGrid target, bool emptySlots)
    {
        IReadOnlyInventorySlot source = _inventory.GetInventory(sourceId).GetSlot(coordinates);
        InventorySlotState state = source.State.CurrentValue;
        if (state.IsEmpty || sourceId == target.OwnerId)
            return 0;

        int capacity = target.CapacityFor(state.ItemId);
        for (int x = 0; x < target.Size.x; x++)
            for (int y = 0; y < target.Size.y; y++)
            {
                if (source.IsEmpty)
                    return state.Amount;

                Vector2Int destination = new(x, y);
                InventorySlotState slot = target.GetSlot(destination).State.CurrentValue;
                if (emptySlots ? !slot.IsEmpty : slot.ItemId != state.ItemId)
                    continue;

                int amount = Math.Min(source.Amount, capacity - slot.Amount);
                if (amount > 0)
                    _inventory.MoveItems(sourceId, coordinates, target.OwnerId, destination, amount);
            }
        return state.Amount - source.Amount;
    }

    public int TakeAll(PlayerInventoryProxy player, string containerOwnerId)
    {
        IReadOnlyInventoryGrid container = _inventory.GetInventory(containerOwnerId);
        int moved = 0;
        for (int x = 0; x < container.Size.x; x++)
            for (int y = 0; y < container.Size.y; y++)
                moved += MoveToPlayer(player, containerOwnerId, new Vector2Int(x, y));
        return moved;
    }

    private void FillLoot(string ownerId, ContainerConfig config)
    {
        foreach (ContainerLoot loot in config.Loot)
        {
            if (loot == null || loot.Item == null)
            {
                Debug.LogError($"ContainerInventoryService: container config '{config.name}' has an empty loot entry");
                continue;
            }

            AddItemsToInventoryGridResult result = _inventory.AddItemsToInventory(ownerId, loot.Item.ItemId, loot.Amount);
            if (result.ItemsNotAddedAmount > 0)
                Debug.LogWarning($"ContainerInventoryService: '{config.name}' has no room for {result.ItemsNotAddedAmount} of '{loot.Item.ItemId}'");
        }
    }
}
