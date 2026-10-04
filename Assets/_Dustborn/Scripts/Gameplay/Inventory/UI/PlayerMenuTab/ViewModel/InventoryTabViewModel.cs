using R3;
using UnityEngine;

public sealed class InventoryTabViewModel : ViewModel
{
    private readonly IInventoryService _inventory;
    public PlayerInventoryProxy Player { get; }
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
        ItemCatalog catalog)
    {
        _inventory = inventory;
        Player = player;
        DragDrop = new InventoryDragDropViewModel(inventory);
        Backpack = new InventoryGridViewModel(inventory, player.Backpack.OwnerId, catalog, DragDrop);
        Hotbar = new HotbarViewModel(inventory, player, players, catalog, DragDrop);
        Equipment = new InventoryGridViewModel(inventory, player.Equipment.OwnerId, catalog, DragDrop);
        Disposables.Add(Hotbar.Grid.Activated.Subscribe(coords => MoveSelected(coords, false)));
        Disposables.Add(Equipment.Activated.Subscribe(coords => MoveSelected(coords, true)));
        Disposables.Add(Unequip.Executed.Subscribe(_ => UnequipSelected()));
        Disposables.Add(TakeFromHotbar.Executed.Subscribe(_ => TakeSelectedHotbar()));
        ItemInfo = new InventoryItemInfoViewModel(catalog, Backpack, Hotbar.Grid, Equipment);
        foreach (InventoryGridViewModel grid in new[] { Backpack, Hotbar.Grid, Equipment })
            Disposables.Add(grid.QuickTransferRequested.Subscribe(coordinates =>
            {
                DragDrop.Cancel();
                ItemInfo.Clear();
                _lastTransferSucceeded.Value = players.QuickTransfer(player.OwnerId, grid.OwnerId, coordinates) > 0;
            }));
    }

    private void MoveSelected(Vector2Int target, bool equipment)
    {
        if (ItemInfo.IsSelected(equipment ? Equipment : Hotbar.Grid, target))
            return;
        if (!Backpack.SelectedSlot.CurrentValue.HasValue)
            return;
        Vector2Int source = Backpack.SelectedSlot.CurrentValue.Value;
        InventorySlotState state = Player.Backpack.GetSlot(source).State.CurrentValue;
        if (state.IsEmpty)
            return;
        _lastTransferSucceeded.Value = _inventory.MoveItems(Player.Backpack.OwnerId, source,
            equipment ? Player.Equipment.OwnerId : Player.Hotbar.OwnerId, target, equipment ? 1 : state.Amount);
    }

    private void UnequipSelected()
    {
        Vector2Int? target = GetBackpackTarget();
        if (!Equipment.SelectedSlot.CurrentValue.HasValue || !target.HasValue)
            return;
        _lastTransferSucceeded.Value = _inventory.MoveItems(Player.Equipment.OwnerId,
            Equipment.SelectedSlot.CurrentValue.Value, Player.Backpack.OwnerId, target.Value);
        SelectTransferredItem(target.Value);
    }

    private void TakeSelectedHotbar()
    {
        Vector2Int? target = GetBackpackTarget();
        if (!target.HasValue)
            return;
        Vector2Int source = new(Player.SelectedHotbarSlot.CurrentValue, 0);
        InventorySlotState state = Player.Hotbar.GetSlot(source).State.CurrentValue;
        if (state.IsEmpty)
            return;
        _lastTransferSucceeded.Value = _inventory.MoveItems(Player.Hotbar.OwnerId, source,
            Player.Backpack.OwnerId, target.Value, state.Amount);
        SelectTransferredItem(target.Value);
    }

    private Vector2Int? GetBackpackTarget()
    {
        if (Backpack.SelectedSlot.CurrentValue.HasValue)
            return Backpack.SelectedSlot.CurrentValue;

        foreach (InventorySlotViewModel slot in Backpack.Slots.CurrentValue)
            if (slot.State.CurrentValue.IsEmpty)
                return slot.Coordinates;
        return null;
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
        base.OnDisposed();
    }
}
