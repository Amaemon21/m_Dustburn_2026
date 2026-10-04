using System;
using R3;
using UnityEngine;

public abstract class ItemSlotViewModel : ViewModel
{
    private readonly ReactiveProperty<InventorySlotState> _state;
    private readonly ReactiveProperty<Sprite> _icon = new(null);
    private readonly ReactiveProperty<bool> _selected = new(false);
    private readonly ReactiveProperty<bool> _dragging = new(false);
    private readonly ReactiveProperty<int> _draggedAmount = new(0);

    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public ReadOnlyReactiveProperty<Sprite> Icon => _icon;
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
            _icon.Value = catalog.GetIcon(state.ItemId);
        }));
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
        _selected.Dispose();
        _dragging.Dispose();
        _draggedAmount.Dispose();
        base.OnDisposed();
    }
}
