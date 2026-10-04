using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerInventoryService : IDisposable
{
    private readonly InventoryRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly Dictionary<string, PlayerInventoryProxy> _players = new();

    public PlayerInventoryService(InventoryRepository repository, IInventoryService inventory)
    {
        _repository = repository;
        _inventory = inventory;
        try
        {
            foreach (PlayerInventoryData data in repository.Origin.Players.ToArray())
                TrackPlayer(data);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public PlayerInventoryProxy GetPlayerInventory(string ownerId)
    {
        return _players[ownerId];
    }

    public PlayerInventoryProxy GetOrCreatePlayerInventory(PlayerInventorySettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));
        return GetOrCreatePlayerInventory(settings.OwnerId, settings.Size, settings.HotbarSize);
    }

    public PlayerInventoryProxy GetOrCreatePlayerInventory(string ownerId, Vector2Int size, int hotbarSize)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Player owner is required", nameof(ownerId));
        
        if (_players.TryGetValue(ownerId, out PlayerInventoryProxy existing))
            return existing;
        
        if (size.x <= 0 || size.y <= 0 || hotbarSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        PlayerInventoryData data = new()
        {
            OwnerId = ownerId,
            BackpackId = ownerId,
            HotbarId = ownerId + "/hotbar",
            EquipmentId = ownerId + "/equipment"
        };
        
        InventoryGridData[] grids =
        {
            new() { OwnerId = data.BackpackId, Size = size, Kind = InventoryGridKind.Backpack },
            new() { OwnerId = data.HotbarId, Size = new Vector2Int(hotbarSize, 1), Kind = InventoryGridKind.Hotbar },
            new() { OwnerId = data.EquipmentId, Size = new Vector2Int(Enum.GetValues(typeof(EquipmentSlot)).Length, 1), Kind = InventoryGridKind.Equipment }
        };
        
        foreach (InventoryGridData grid in grids)
        {
            if (_inventory.TryGetInventory(grid.OwnerId, out IReadOnlyInventoryGrid current) &&
                (current.Kind != grid.Kind || (grid.Kind != InventoryGridKind.Backpack && current.Size != grid.Size)))
            {
                throw new InvalidOperationException($"Inventory '{grid.OwnerId}' has an incompatible layout");
            }
        }

        foreach (InventoryGridData grid in grids)
        {
            if (!_inventory.TryGetInventory(grid.OwnerId, out _))
            {
                _inventory.RegisterInventory(grid);
            }
        }

        TrackPlayer(data);
        
        return _players[data.OwnerId];
    }

    public void SelectHotbarSlot(string ownerId, int index)
    {
        _players[ownerId].SelectHotbarSlot(index);
    }

    public void CycleHotbarSlot(string ownerId, int direction)
    {
        PlayerInventoryProxy player = GetPlayerInventory(ownerId);
        int count = player.Hotbar.Size.x;
        int index = (player.SelectedHotbarSlot.CurrentValue + direction % count + count) % count;
        player.SelectHotbarSlot(index);
    }

    public int PickUp(string ownerId, string itemId, int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        PlayerInventoryProxy player = GetPlayerInventory(ownerId);
        int remaining = amount;
        bool useHotbar = player.Hotbar.GetAmount(itemId) > 0;
        remaining -= _inventory.AddItemsToExistingStacks(player.Hotbar.OwnerId, itemId, remaining).ItemsAddedAmount;
        if (remaining > 0)
            remaining -= _inventory.AddItemsToExistingStacks(player.Backpack.OwnerId, itemId, remaining).ItemsAddedAmount;
        if (remaining > 0 && useHotbar)
            remaining -= _inventory.AddItemsToInventory(player.Hotbar.OwnerId, itemId, remaining).ItemsAddedAmount;
        if (remaining > 0)
            remaining -= _inventory.AddItemsToInventory(player.Backpack.OwnerId, itemId, remaining).ItemsAddedAmount;
        return amount - remaining;
    }

    public int QuickTransfer(string ownerId, string sourceId, Vector2Int coordinates)
    {
        PlayerInventoryProxy player = GetPlayerInventory(ownerId);
        if (sourceId != player.Backpack.OwnerId && sourceId != player.Hotbar.OwnerId && sourceId != player.Equipment.OwnerId)
            throw new ArgumentException("Quick transfer requires a player inventory", nameof(sourceId));
        IReadOnlyInventorySlot source = _inventory.GetInventory(sourceId).GetSlot(coordinates);
        InventorySlotState state = source.State.CurrentValue;
        if (state.IsEmpty)
            return 0;

        if (sourceId != player.Equipment.OwnerId)
        {
            for (int index = 0; index < player.Equipment.Size.x; index++)
            {
                Vector2Int equipmentSlot = new(index, 0);
                if (player.Equipment.GetSlot(equipmentSlot).IsEmpty &&
                    _inventory.MoveItems(sourceId, coordinates, player.Equipment.OwnerId, equipmentSlot))
                    return 1;
            }
        }

        IReadOnlyInventoryGrid target = sourceId == player.Backpack.OwnerId ? player.Hotbar : player.Backpack;
        int capacity = target.CapacityFor(state.ItemId);
        for (int pass = 0; pass < 2; pass++)
            for (int x = 0; x < target.Size.x; x++)
                for (int y = 0; y < target.Size.y; y++)
                {
                    InventorySlotState destination = target.GetSlot(new Vector2Int(x, y)).State.CurrentValue;
                    if ((pass == 0 && destination.ItemId != state.ItemId) || (pass == 1 && !destination.IsEmpty))
                        continue;
                    int amount = Math.Min(source.State.CurrentValue.Amount, capacity - destination.Amount);
                    if (amount > 0)
                        _inventory.MoveItems(sourceId, coordinates, target.OwnerId, new Vector2Int(x, y), amount);
                }
        return state.Amount - source.State.CurrentValue.Amount;
    }

    public bool Equip(string ownerId, Vector2Int backpackSlot, EquipmentSlot equipmentSlot)
    {
        PlayerInventoryProxy player = GetPlayerInventory(ownerId);
        
        return _inventory.MoveItems(player.Backpack.OwnerId, backpackSlot, player.Equipment.OwnerId, new Vector2Int((int)equipmentSlot, 0));
    }

    public bool Unequip(string ownerId, EquipmentSlot equipmentSlot, Vector2Int backpackSlot)
    {
        PlayerInventoryProxy player = GetPlayerInventory(ownerId);
        
        return _inventory.MoveItems(player.Equipment.OwnerId, new Vector2Int((int)equipmentSlot, 0), player.Backpack.OwnerId, backpackSlot);
    }

    private void TrackPlayer(PlayerInventoryData data)
    {
        int previousSelection = data.SelectedHotbarSlot;
        PlayerInventoryProxy player = new(data, _inventory.GetInventory(data.BackpackId),
            _inventory.GetInventory(data.HotbarId), _inventory.GetInventory(data.EquipmentId));
        _players.Add(data.OwnerId, player);
        
        _repository.Track(player);
        if (previousSelection != data.SelectedHotbarSlot)
            _repository.MarkDirty();
    }

    public void Dispose()
    {
        foreach (PlayerInventoryProxy player in _players.Values)
            player.Dispose();
        _players.Clear();
    }
}
