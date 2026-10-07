using System;
using R3;
using UnityEngine;

public sealed class InventoryItemInfoViewModel : ViewModel
{
    private readonly ItemCatalog _catalog;
    private readonly ReactiveProperty<InventorySlotState> _state = new(new InventorySlotState(null, 0));
    private readonly CompositeDisposable _slotBindings = new();
    private readonly InventoryGridViewModel[] _grids;
    private InventoryGridViewModel _selectedGrid;
    private Vector2Int _selectedCoordinates;

    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public InventoryItem Item => _catalog.GetItem(_state.CurrentValue.ItemId);
    public bool HasItem => !_state.CurrentValue.IsEmpty;
    public string SelectedOwnerId => _selectedGrid?.OwnerId;
    public Vector2Int SelectedCoordinates => _selectedCoordinates;
    public ReactiveCommand<Unit> Use { get; } = new();
    public ReactiveCommand<Unit> Drop { get; } = new();

    public InventoryItemInfoViewModel(ItemCatalog catalog, params InventoryGridViewModel[] grids)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _grids = grids;
        Disposables.Add(_state.Subscribe(_ => RefreshCommands()));
        foreach (InventoryGridViewModel grid in grids)
        {
            Disposables.Add(grid.Activated.Subscribe(coordinates => Select(grid, coordinates)));
            Disposables.Add(grid.SelectionCancelled.Subscribe(_ => Clear()));
            Disposables.Add(grid.Slots.Skip(1).Subscribe(_ => Clear()));
            Disposables.Add(grid.SelectedSlot.Skip(1).Subscribe(coordinates =>
            {
                if (ReferenceEquals(_selectedGrid, grid) && !coordinates.HasValue)
                    Clear();
            }));
        }
    }

    public void Select(InventoryGridViewModel grid, Vector2Int coordinates)
    {
        _slotBindings.Clear();
        InventorySlotViewModel slot = grid.Slots.CurrentValue[InventoryGridLayout.IndexOf(coordinates, grid.Size)];
        if (IsSelected(grid, coordinates) || slot.State.CurrentValue.IsEmpty)
        {
            Clear();
            return;
        }
        _selectedGrid = grid;
        _selectedCoordinates = coordinates;
        grid.SelectSlot(coordinates);
        _slotBindings.Add(slot.State.Subscribe(state =>
        {
            if (state.IsEmpty)
            {
                Clear();
                return;
            }
            _state.Value = state;
        }));
    }

    private void RefreshCommands()
    {
        InventoryItem item = Item;
        Use.SetCanExecute(item != null && item.IsUsable);
        Drop.SetCanExecute(item != null && item.PickUpPrefab != null);
    }

    public bool IsSelected(InventoryGridViewModel grid, Vector2Int coordinates)
        => ReferenceEquals(_selectedGrid, grid) && _selectedCoordinates == coordinates;

    public void Clear()
    {
        _slotBindings.Clear();
        _selectedGrid = null;
        foreach (InventoryGridViewModel grid in _grids)
            grid.SelectSlot(null);
        _state.Value = new InventorySlotState(null, 0);
    }

    protected override void OnDisposed()
    {
        _slotBindings.Dispose();
        _state.Dispose();
        Use.Dispose();
        Drop.Dispose();
        base.OnDisposed();
    }
}
