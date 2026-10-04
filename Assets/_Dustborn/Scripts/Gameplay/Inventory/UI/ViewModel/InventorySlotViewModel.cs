using System;
using R3;
using UnityEngine;

public class InventorySlotViewModel : ItemSlotViewModel
{
    public Vector2Int Coordinates { get; }
    public string OwnerId { get; }
    public InventoryDragDropViewModel DragDrop { get; }
    public ReactiveCommand<Unit> QuickTransfer { get; } = new();

    public InventorySlotViewModel(IReadOnlyInventorySlot slot, Vector2Int coordinates,
        ItemCatalog catalog, Action<Vector2Int> onSelected = null,
        string ownerId = null, InventoryDragDropViewModel dragDrop = null,
        Action<Vector2Int> onQuickTransfer = null) : base(slot, catalog)
    {
        Coordinates = coordinates;
        OwnerId = ownerId;
        DragDrop = dragDrop;
        Disposables.Add(Select.Executed.Subscribe(_ => onSelected?.Invoke(Coordinates)));
        Disposables.Add(QuickTransfer.Executed.Subscribe(_ => onQuickTransfer?.Invoke(Coordinates)));
    }

    protected override void OnDisposed()
    {
        DragDrop?.Cancel(this);
        QuickTransfer.Dispose();
        base.OnDisposed();
    }

}
