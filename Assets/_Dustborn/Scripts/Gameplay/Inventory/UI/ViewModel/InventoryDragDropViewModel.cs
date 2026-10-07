using System;
using R3;
using UnityEngine;

public sealed class InventoryDragDropViewModel : ViewModel
{
    private readonly IInventoryService _inventory;
    private readonly IItemDropService _drops;
    private readonly CompositeDisposable _sourceBindings = new();
    private readonly ReactiveProperty<InventorySlotState> _state = new(new InventorySlotState(null, 0));
    private readonly ReactiveProperty<Sprite> _icon = new(null);
    private readonly ReactiveProperty<Vector2> _position = new();
    private InventorySlotViewModel _source;
    private InventorySlotState _sourceState;

    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public ReadOnlyReactiveProperty<Sprite> Icon => _icon;
    public ReadOnlyReactiveProperty<Vector2> Position => _position;
    public bool IsDragging => _source != null;

    public InventoryDragDropViewModel(IInventoryService inventory, IItemDropService drops = null)
    {
        _inventory = inventory;
        _drops = drops;
    }

    public bool Begin(InventorySlotViewModel source, Vector2 position, bool takeHalf = false)
    {
        Cancel();
        if (source.IsDisposed || source.State.CurrentValue.IsEmpty || !ReferenceEquals(source.DragDrop, this))
            return false;

        _source = source;
        _sourceState = source.State.CurrentValue;
        _icon.Value = source.Icon.CurrentValue;
        _position.Value = position;
        int amount = takeHalf ? (_sourceState.Amount + 1) / 2 : _sourceState.Amount;
        source.SetDragging(amount);
        _state.Value = _sourceState.WithAmount(amount);
        _sourceBindings.Add(source.State.Skip(1).Subscribe(_ => Cancel()));
        return true;
    }

    public void Move(Vector2 position)
    {
        if (IsDragging)
            _position.Value = position;
    }

    public void SetAmount(int amount)
    {
        if (IsDragging)
        {
            _state.Value = _sourceState.WithAmount(Math.Clamp(amount, 1, _sourceState.Amount));
            _source.SetDragging(_state.CurrentValue.Amount);
        }
    }

    public void ChangeAmount(int delta)
    {
        SetAmount(_state.CurrentValue.Amount + delta);
    }

    public bool Drop(InventorySlotViewModel target)
    {
        InventorySlotViewModel source = _source;
        InventorySlotState state = _state.CurrentValue;
        try
        {
            if (source == null || source.IsDisposed || target.IsDisposed ||
                !ReferenceEquals(target.DragDrop, this) ||
                (source.OwnerId == target.OwnerId && source.Coordinates == target.Coordinates) ||
                !source.State.CurrentValue.Equals(_sourceState))
                return false;

            InventorySlotState destination = target.State.CurrentValue;
            bool stacks = destination.ItemId == state.ItemId && destination.Wear == state.Wear;
            if (!destination.IsEmpty && !stacks)
                return state.Amount == _sourceState.Amount &&
                    _inventory.SwapSlots(source.OwnerId, source.Coordinates, target.OwnerId, target.Coordinates);

            IReadOnlyInventoryGrid grid = _inventory.GetInventory(target.OwnerId);
            int amount = Math.Min(state.Amount, grid.CapacityFor(state.ItemId) - destination.Amount);
            return amount > 0 && _inventory.MoveItems(source.OwnerId, source.Coordinates,
                target.OwnerId, target.Coordinates, amount);
        }
        finally
        {
            Cancel();
        }
    }

    public bool DropToWorld(InventorySlotViewModel source)
    {
        InventorySlotState state = _state.CurrentValue;
        try
        {
            return _drops != null && ReferenceEquals(_source, source) && !source.IsDisposed &&
                source.State.CurrentValue.Equals(_sourceState) &&
                _drops.Drop(source.OwnerId, source.Coordinates, state.Amount);
        }
        finally
        {
            Cancel(source);
        }
    }

    public void Cancel(InventorySlotViewModel source)
    {
        if (ReferenceEquals(_source, source))
            Cancel();
    }

    public void Cancel()
    {
        _sourceBindings.Clear();
        _source?.SetDragging(0);
        _source = null;
        _state.Value = new InventorySlotState(null, 0);
        _icon.Value = null;
    }

    protected override void OnDisposed()
    {
        Cancel();
        _sourceBindings.Dispose();
        _state.Dispose();
        _icon.Dispose();
        _position.Dispose();
        base.OnDisposed();
    }
}
