using R3;
using UnityEngine;

public sealed class HotbarViewModel : ViewModel
{
    public InventoryGridViewModel Grid { get; }
    public ReadOnlyReactiveProperty<int> SelectedSlot { get; }
    public ReactiveCommand<int> SelectSlot { get; } = new();

    public HotbarViewModel(IInventoryService inventory, PlayerInventoryProxy player, PlayerInventoryService players,
        ItemCatalog catalog, InventoryDragDropViewModel dragDrop = null)
    {
        Grid = new InventoryGridViewModel(inventory, player.Hotbar.OwnerId, catalog, dragDrop);
        SelectedSlot = player.SelectedHotbarSlot;
        Disposables.Add(SelectSlot.Executed.Subscribe(index => players.SelectHotbarSlot(player.OwnerId, index)));
        Disposables.Add(Grid.Activated.Subscribe(coords => SelectSlot.Execute(coords.x)));
        Disposables.Add(SelectedSlot.Subscribe(index => Grid.SelectSlot(new Vector2Int(index, 0))));
    }

    protected override void OnDisposed()
    {
        Grid.Dispose();
        SelectSlot.Dispose();
        base.OnDisposed();
    }
}
