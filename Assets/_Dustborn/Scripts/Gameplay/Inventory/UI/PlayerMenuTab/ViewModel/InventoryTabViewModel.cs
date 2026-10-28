using R3;
using UnityEngine;

public sealed class InventoryTabViewModel : ViewModel
{
    private readonly PlayerInventoryService _players;
    private readonly IItemDropService _drops;
    private readonly IItemUseService _uses;
    public PlayerInventoryProxy Player { get; }
    public PlayerStatusViewModel Status { get; }
    public InventoryGridViewModel Backpack { get; }
    public HotbarViewModel Hotbar { get; }
    public InventoryGridViewModel Equipment { get; }
    public InventoryItemInfoViewModel ItemInfo { get; }
    public InventoryDragDropViewModel DragDrop { get; }
    public ReactiveCommand<Unit> Unequip { get; } = new();
    public ReactiveCommand<Unit> TakeFromHotbar { get; } = new();
    public ReadOnlyReactiveProperty<bool> LastTransferSucceeded => _lastTransferSucceeded;
    private readonly ReactiveProperty<bool> _lastTransferSucceeded = new(true);

    public InventoryTabViewModel(IInventoryService inventory, PlayerInventoryProxy player, PlayerInventoryService players,
        ItemCatalog catalog, IItemDropService drops, IReadOnlyInventoryWeight weight, IItemUseService uses,
        PlayerStatusViewModel status)
    {
        _players = players;
        _drops = drops;
        _uses = uses;
        Player = player;
        Status = status;
        DragDrop = new InventoryDragDropViewModel(inventory, drops);
        Backpack = new InventoryGridViewModel(inventory, player.Backpack.OwnerId, catalog, DragDrop, weight);
        Hotbar = new HotbarViewModel(inventory, player, catalog, DragDrop);
        Equipment = new InventoryGridViewModel(inventory, player.Equipment.OwnerId, catalog, DragDrop);
        Disposables.Add(Unequip.Executed.Subscribe(_ => UnequipSelected()));
        Disposables.Add(TakeFromHotbar.Executed.Subscribe(_ => TakeSelectedHotbar()));
        ItemInfo = new InventoryItemInfoViewModel(catalog, Backpack, Hotbar.Grid, Equipment);
        Disposables.Add(ItemInfo.Drop.Executed.Subscribe(_ => DropSelected()));
        Disposables.Add(ItemInfo.Use.Executed.Subscribe(_ => UseSelected()));
        foreach (InventoryGridViewModel grid in new[] { Backpack, Hotbar.Grid, Equipment })
            Disposables.Add(grid.QuickTransferRequested.Subscribe(coordinates =>
            {
                DragDrop.Cancel();
                ItemInfo.Clear();
                _lastTransferSucceeded.Value = players.QuickTransfer(player.OwnerId, grid.OwnerId, coordinates) > 0;
            }));
    }

    private void UnequipSelected()
    {
        if (Equipment.SelectedSlot.CurrentValue.HasValue)
            MoveToBackpack(Player.Equipment.OwnerId, Equipment.SelectedSlot.CurrentValue.Value);
    }

    private void TakeSelectedHotbar()
    {
        Vector2Int source = new(Player.SelectedHotbarSlot.CurrentValue, 0);
        if (!Player.Hotbar.GetSlot(source).IsEmpty)
            MoveToBackpack(Player.Hotbar.OwnerId, source);
    }

    private void MoveToBackpack(string sourceId, Vector2Int source)
    {
        _lastTransferSucceeded.Value = _players.MoveToBackpack(Player.OwnerId, sourceId, source,
            Backpack.SelectedSlot.CurrentValue, out Vector2Int target);
        SelectTransferredItem(target);
    }

    private void DropSelected()
    {
        string ownerId = ItemInfo.SelectedOwnerId;
        InventorySlotState state = ItemInfo.State.CurrentValue;
        if (_drops == null || ownerId == null || state.IsEmpty)
            return;

        DragDrop.Cancel();
        _drops.Drop(ownerId, ItemInfo.SelectedCoordinates, state.Amount);
    }

    private void UseSelected()
    {
        string ownerId = ItemInfo.SelectedOwnerId;
        if (_uses == null || ownerId == null || !ItemInfo.HasItem)
            return;

        DragDrop.Cancel();
        _uses.Use(ownerId, ItemInfo.SelectedCoordinates);
    }

    private void SelectTransferredItem(Vector2Int target)
    {
        if (!_lastTransferSucceeded.Value)
            return;

        Backpack.SelectSlot(target);
        if (!ItemInfo.IsSelected(Backpack, target))
            ItemInfo.Select(Backpack, target);
    }

    protected override void OnDisposed()
    {
        ItemInfo.Dispose();
        DragDrop.Dispose();
        Backpack.Dispose();
        Hotbar.Dispose();
        Equipment.Dispose();
        Unequip.Dispose();
        TakeFromHotbar.Dispose();
        _lastTransferSucceeded.Dispose();
        Status?.Dispose();
        base.OnDisposed();
    }
}
