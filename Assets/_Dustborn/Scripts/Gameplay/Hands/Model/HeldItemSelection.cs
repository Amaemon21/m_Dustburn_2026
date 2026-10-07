using System;
using R3;
using UnityEngine;

public sealed class HeldItemSelection : IDisposable
{
    private readonly ReactiveProperty<HeldItem> _held = new();
    private readonly IDisposable _subscription;

    public ReadOnlyReactiveProperty<HeldItem> Held => _held;

    public HeldItemSelection(PlayerInventoryProxy player, ItemCatalog catalog)
    {
        _subscription = player.SelectedHotbarSlot
            .Select(index => Observe(player.Hotbar, new Vector2Int(index, 0), catalog))
            .Switch()
            .Subscribe(item => _held.Value = item);
    }

    private static Observable<HeldItem> Observe(IReadOnlyInventoryGrid hotbar, Vector2Int coordinates, ItemCatalog catalog)
        => hotbar.GetSlot(coordinates).State.Select(state => Resolve(hotbar.OwnerId, coordinates, state, catalog));

    private static HeldItem Resolve(string ownerId, Vector2Int coordinates, InventorySlotState state, ItemCatalog catalog)
    {
        if (state.IsEmpty)
            return new HeldItem(ownerId, coordinates, null, null);

        InventoryItem item = catalog.GetItem(state.ItemId);
        return new HeldItem(ownerId, coordinates, state.ItemId, item == null ? null : item.Held);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _held.Dispose();
    }
}
