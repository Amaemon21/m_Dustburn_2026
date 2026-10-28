using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class InventoryWeight : IReadOnlyInventoryWeight, IDisposable
{
    private const float OVERLOAD_TOLERANCE = 0.001f;

    private readonly ItemCatalog _catalog;
    private readonly IReadOnlyInventoryGrid[] _grids;
    private readonly IReadOnlyList<CarryZone> _zones;
    private readonly ReactiveProperty<float> _current = new(0f);
    private readonly ReactiveProperty<bool> _overloaded = new(false);
    private readonly ReactiveProperty<float> _load = new(0f);
    private readonly ReactiveProperty<CarryZone> _zone = new(null);
    private readonly CompositeDisposable _subscriptions = new();

    public ReadOnlyReactiveProperty<float> Current => _current;
    public IReadOnlyStat Capacity { get; }
    public bool HasLimit => Capacity != null;
    public ReadOnlyReactiveProperty<bool> IsOverloaded => _overloaded;
    public ReadOnlyReactiveProperty<float> Load => _load;
    public ReadOnlyReactiveProperty<CarryZone> Zone => _zone;

    public InventoryWeight(ItemCatalog catalog, IReadOnlyStat capacity, params IReadOnlyInventoryGrid[] grids)
        : this(catalog, capacity, null, grids)
    {
    }

    public InventoryWeight(ItemCatalog catalog, IReadOnlyStat capacity, IReadOnlyList<CarryZone> zones, params IReadOnlyInventoryGrid[] grids)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (grids == null || grids.Length == 0)
            throw new ArgumentException("An inventory weight needs at least one grid", nameof(grids));

        _grids = grids;
        _zones = zones;
        Capacity = capacity;
        foreach (IReadOnlyInventoryGrid grid in grids)
            _subscriptions.Add(grid.Changed.Subscribe(_ => Refresh()));
        if (capacity != null)
            _subscriptions.Add(capacity.Value.Subscribe(_ => RefreshOverload()));
        Refresh();
    }

    private void Refresh()
    {
        float total = 0f;
        foreach (IReadOnlyInventoryGrid grid in _grids)
            for (int x = 0; x < grid.Size.x; x++)
                for (int y = 0; y < grid.Size.y; y++)
                {
                    IReadOnlyInventorySlot slot = grid.GetSlot(new Vector2Int(x, y));
                    if (!slot.IsEmpty)
                        total += _catalog.WeightOf(slot.ItemId) * slot.Amount;
                }

        _current.Value = total;
        RefreshOverload();
    }

    private void RefreshOverload()
    {
        float capacity = HasLimit ? Capacity.Value.CurrentValue : 0f;
        _overloaded.Value = HasLimit && _current.Value > capacity + OVERLOAD_TOLERANCE;
        _load.Value = capacity > 0f ? _current.Value / capacity : 0f;
        _zone.Value = HasLimit ? CarryPenalty.ZoneAt(_zones, _load.Value) : null;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _current.Dispose();
        _overloaded.Dispose();
        _load.Dispose();
        _zone.Dispose();
    }
}
