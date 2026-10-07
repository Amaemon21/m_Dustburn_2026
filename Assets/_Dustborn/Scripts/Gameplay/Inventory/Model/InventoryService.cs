using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class InventoryService : IInventoryService, IDisposable
{
    private readonly Dictionary<string, InventoryGrid> _inventories = new();
    private readonly ItemCatalog _catalog;
    private readonly InventoryRepository _repository;

    public InventoryService(InventoryRepository repository, ItemCatalog catalog)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        try
        {
            foreach (InventoryGridData data in repository.Origin.Grids.ToArray())
                RegisterInventory(data);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyInventoryGrid RegisterInventory(InventoryGridData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.OwnerId))
            throw new ArgumentException("Inventory owner is required", nameof(data));
        if (_inventories.ContainsKey(data.OwnerId))
            throw new InvalidOperationException($"Inventory '{data.OwnerId}' already exists");
        InventoryGrid inventory = new(data, _catalog);
        _inventories.Add(inventory.OwnerId, inventory);
        _repository.Track(inventory.Proxy);
        return inventory;
    }

    public bool TryGetInventory(string ownerId, out IReadOnlyInventoryGrid inventory)
    {
        bool found = _inventories.TryGetValue(ownerId, out InventoryGrid grid);
        inventory = grid;
        return found;
    }

    public IReadOnlyInventoryGrid GetInventory(string ownerId) => GetGrid(ownerId);
    public AddItemsToInventoryGridResult AddItemsToInventory(string ownerId, string itemId, int amount = 1)
        => GetGrid(ownerId).AddItems(itemId, amount);
    public AddItemsToInventoryGridResult AddItemsToExistingStacks(string ownerId, string itemId, int amount)
        => GetGrid(ownerId).AddItemsToExistingStacks(itemId, amount);
    public AddItemsToInventoryGridResult AddItemsToInventory(string ownerId, Vector2Int coords, string itemId, int amount = 1)
        => GetGrid(ownerId).AddItems(coords, itemId, amount);
    public RemoveItemsFromInventoryGridResult RemoveItemsFromInventory(string ownerId, string itemId, int amount = 1)
        => GetGrid(ownerId).RemoveItems(itemId, amount);
    public RemoveItemsFromInventoryGridResult RemoveItemsFromInventory(string ownerId, Vector2Int coords, string itemId, int amount = 1)
        => GetGrid(ownerId).RemoveItems(coords, itemId, amount);
    public bool Has(string ownerId, string itemId, int amount = 1) => GetGrid(ownerId).Has(itemId, amount);
    public void SwitchSlots(string ownerId, Vector2Int first, Vector2Int second) => GetGrid(ownerId).SwitchSlots(first, second);
    public void SetSize(string ownerId, Vector2Int size) => GetGrid(ownerId).SetSize(size);
    public bool MoveItems(string sourceId, Vector2Int source, string targetId, Vector2Int target, int amount = 1)
        => GetGrid(sourceId).MoveTo(source, GetGrid(targetId), target, amount);
    public bool SwapSlots(string firstId, Vector2Int first, string secondId, Vector2Int second)
        => GetGrid(firstId).SwapWith(first, GetGrid(secondId), second);
    public bool AddWear(string ownerId, Vector2Int coords, string itemId, int amount)
        => GetGrid(ownerId).AddWear(coords, itemId, amount);

    public InventoryGridProxy GetProxy(string ownerId) => GetGrid(ownerId).Proxy;

    private InventoryGrid GetGrid(string ownerId)
    {
        if (_inventories.TryGetValue(ownerId, out InventoryGrid grid))
            return grid;

        throw new KeyNotFoundException($"Inventory '{ownerId}' is not registered");
    }

    public void Dispose()
    {
        foreach (InventoryGrid grid in _inventories.Values)
            grid.Dispose();
        _inventories.Clear();
    }
}
