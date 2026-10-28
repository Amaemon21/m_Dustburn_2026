using System;
using System.Collections.Generic;

public sealed class InventoryWeightService : IDisposable
{
    private readonly ItemCatalog _catalog;
    private readonly Dictionary<string, InventoryWeight> _weights = new();

    public InventoryWeightService(ItemCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public IReadOnlyInventoryWeight Register(string ownerId, IReadOnlyStat capacity, params IReadOnlyInventoryGrid[] grids)
        => Register(ownerId, capacity, null, grids);

    public IReadOnlyInventoryWeight Register(string ownerId, IReadOnlyStat capacity, IReadOnlyList<CarryZone> zones,
        params IReadOnlyInventoryGrid[] grids)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Weight owner is required", nameof(ownerId));
        if (_weights.ContainsKey(ownerId))
            throw new InvalidOperationException($"Inventory weight for '{ownerId}' is already registered");

        InventoryWeight weight = new(_catalog, capacity, zones, grids);
        _weights.Add(ownerId, weight);
        return weight;
    }

    public bool TryGet(string ownerId, out IReadOnlyInventoryWeight weight)
    {
        bool found = _weights.TryGetValue(ownerId, out InventoryWeight value);
        weight = value;
        return found;
    }

    public void Dispose()
    {
        foreach (InventoryWeight weight in _weights.Values)
            weight.Dispose();
        _weights.Clear();
    }
}
