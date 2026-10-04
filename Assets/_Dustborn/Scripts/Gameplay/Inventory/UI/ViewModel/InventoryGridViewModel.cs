using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class InventoryGridViewModel : ViewModel
{
    private readonly IInventoryService _inventory;
    private readonly IReadOnlyInventoryGrid _grid;
    private readonly ReactiveProperty<IReadOnlyList<InventorySlotViewModel>> _slots = new(Array.Empty<InventorySlotViewModel>());
    private readonly ReactiveProperty<Vector2Int?> _selectedSlot = new(null);
    private readonly Subject<Vector2Int> _activated = new();
    private readonly Subject<Unit> _selectionCancelled = new();
    private readonly Subject<Vector2Int> _quickTransferRequested = new();
    private readonly ItemCatalog _catalog;
    private readonly InventoryDragDropViewModel _dragDrop;
    public string OwnerId => _grid.OwnerId;
    public Vector2Int Size => _grid.Size;
    public ReadOnlyReactiveProperty<IReadOnlyList<InventorySlotViewModel>> Slots => _slots;
    public ReadOnlyReactiveProperty<Vector2Int?> SelectedSlot => _selectedSlot;
    public Observable<Vector2Int> Activated => _activated;
    public Observable<Unit> SelectionCancelled => _selectionCancelled;
    public Observable<Vector2Int> QuickTransferRequested => _quickTransferRequested;
    public ReactiveCommand<(Vector2Int First, Vector2Int Second)> SwitchSlots { get; } = new();
    public InventoryOccupancyViewModel Occupancy { get; }
    public InventoryWeightViewModel Weight { get; }

    public InventoryGridViewModel(IInventoryService inventory, string ownerId, ItemCatalog catalog,
        InventoryDragDropViewModel dragDrop = null)
    {
        _inventory = inventory;
        _grid = inventory.GetInventory(ownerId);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _dragDrop = dragDrop;
        Occupancy = new InventoryOccupancyViewModel(_grid);
        Weight = new InventoryWeightViewModel(_catalog, _grid);
        
        Disposables.Add(SwitchSlots.Executed.Subscribe(pair => _inventory.SwitchSlots(OwnerId, pair.First, pair.Second)));
        RebuildSlots();
        Disposables.Add(_grid.SizeChanged.Subscribe(_ => RebuildSlots()));
    }

    private void RebuildSlots()
    {
        _dragDrop?.Cancel();
        IReadOnlyList<InventorySlotViewModel> previous = _slots.Value;
        List<InventorySlotViewModel> next = new();
        IReadOnlyInventorySlot[,] slots = _grid.GetSlots();
        
        for (int x = 0; x < Size.x; x++)
        {
            for (int y = 0; y < Size.y; y++)
            {
                next.Add(new InventorySlotViewModel(slots[x, y], new Vector2Int(x, y), _catalog, Activate, OwnerId,
                    _dragDrop, coordinates => _quickTransferRequested.OnNext(coordinates)));
            }
        }

        _slots.Value = next.AsReadOnly();
        SelectSlot(null);
        
        foreach (InventorySlotViewModel slot in previous)
        {
            slot.Dispose();
        }
    }

    public void SelectSlot(Vector2Int? coordinates)
    {
        if (coordinates.HasValue && (coordinates.Value.x < 0 || coordinates.Value.y < 0 ||
            coordinates.Value.x >= Size.x || coordinates.Value.y >= Size.y))
            throw new ArgumentOutOfRangeException(nameof(coordinates));
        foreach (InventorySlotViewModel slot in _slots.Value)
            slot.SetSelected(coordinates.HasValue && slot.Coordinates == coordinates.Value);
        _selectedSlot.Value = coordinates;
    }

    private void Activate(Vector2Int coordinates)
    {
        if (_slots.Value[InventoryGridLayout.IndexOf(coordinates, Size)].State.CurrentValue.IsEmpty)
        {
            SelectSlot(null);
            _selectionCancelled.OnNext(Unit.Default);
            return;
        }
        SelectSlot(coordinates);
        _activated.OnNext(coordinates);
    }

    protected override void OnDisposed()
    {
        foreach (InventorySlotViewModel slot in _slots.CurrentValue)
        {
            slot.Dispose();
        }
        _slots.Dispose();
        _selectedSlot.Dispose();
        _activated.Dispose();
        _selectionCancelled.Dispose();
        _quickTransferRequested.Dispose();
        SwitchSlots.Dispose();
        Occupancy.Dispose();
        Weight.Dispose();
        base.OnDisposed();
    }
}
