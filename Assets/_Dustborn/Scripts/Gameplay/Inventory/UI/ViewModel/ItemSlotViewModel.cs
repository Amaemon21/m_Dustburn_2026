using System;
using R3;
using UnityEngine;

public abstract class ItemSlotViewModel : ViewModel
{
    private readonly ReactiveProperty<InventorySlotState> _state;
    private readonly ReactiveProperty<Sprite> _icon = new(null);
    private readonly ReactiveProperty<ItemRarity?> _rarity = new(null);
    private readonly ReactiveProperty<float?> _durability = new(null);
    private readonly ReactiveProperty<bool> _selected = new(false);
    private readonly ReactiveProperty<bool> _dragging = new(false);
    private readonly ReactiveProperty<int> _draggedAmount = new(0);

    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public ReadOnlyReactiveProperty<Sprite> Icon => _icon;
    public ReadOnlyReactiveProperty<ItemRarity?> Rarity => _rarity;
    public ReadOnlyReactiveProperty<float?> Durability => _durability;
    public ReadOnlyReactiveProperty<bool> IsSelected => _selected;
    public ReadOnlyReactiveProperty<bool> IsDragging => _dragging;
    public ReadOnlyReactiveProperty<int> DraggedAmount => _draggedAmount;
    public int DisplayAmount => Math.Max(0, _state.CurrentValue.Amount - _draggedAmount.CurrentValue);
    public ReactiveCommand<Unit> Select { get; } = new();

    protected ItemSlotViewModel(IReadOnlyInventorySlot slot, ItemCatalog catalog)
    {
        _state = new ReactiveProperty<InventorySlotState>(slot.State.CurrentValue);
        
        Disposables.Add(slot.State.Subscribe(state =>
        {
            _state.Value = state;
            InventoryItem item = catalog.GetItem(state.ItemId);
            _icon.Value = item == null ? null : item.Icon;
            _rarity.Value = item == null ? null : (ItemRarity?)item.Rarity;
            _durability.Value = DurabilityOf(catalog, state);
        }));
    }

    private static float? DurabilityOf(ItemCatalog catalog, InventorySlotState state)
    {
        int durability = catalog.DurabilityOf(state.ItemId);
        if (state.IsEmpty || durability == 0)
            return null;
        return Mathf.Clamp01((durability - state.Wear) / (float)durability);
    }

    protected internal void SetSelected(bool selected) => _selected.Value = selected;
    internal void SetDragging(int amount)
    {
        _draggedAmount.Value = amount;
        _dragging.Value = amount > 0;
    }

    protected override void OnDisposed()
    {
        Select.Dispose();
        _state.Dispose();
        _icon.Dispose();
        _rarity.Dispose();
        _durability.Dispose();
        _selected.Dispose();
        _dragging.Dispose();
        _draggedAmount.Dispose();
        base.OnDisposed();
    }
}
