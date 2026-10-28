using R3;

public sealed class ContainerScreenViewModel : WindowViewModel
{
    private readonly ContainerInventoryService _containers;
    private readonly PlayerInventoryProxy _player;
    private readonly IReadOnlyInventoryGrid _grid;

    public string Title { get; }
    public InventoryGridViewModel Container { get; }
    public InventoryGridViewModel Backpack { get; }
    public HotbarViewModel Hotbar { get; }
    public InventoryDragDropViewModel DragDrop { get; }
    public InventoryItemInfoViewModel Selection { get; }
    public ItemNameHoverViewModel ItemNameHover { get; }
    public ReactiveCommand<Unit> TakeAll { get; } = new();

    public ContainerScreenViewModel(IInventoryService inventory, ContainerInventoryService containers,
        PlayerInventoryProxy player, IReadOnlyInventoryWeight playerWeight, PlayerInventoryService players,
        ItemCatalog catalog, IItemDropService drops, string containerOwnerId, string title)
    {
        _containers = containers;
        _player = player;
        _grid = inventory.GetInventory(containerOwnerId);
        Title = title;
        DragDrop = new InventoryDragDropViewModel(inventory, drops);
        Container = new InventoryGridViewModel(inventory, containerOwnerId, catalog, DragDrop);
        Backpack = new InventoryGridViewModel(inventory, player.Backpack.OwnerId, catalog, DragDrop, playerWeight);
        Hotbar = new HotbarViewModel(inventory, player, catalog, DragDrop);
        Selection = new InventoryItemInfoViewModel(catalog, Container, Backpack, Hotbar.Grid);
        ItemNameHover = new ItemNameHoverViewModel(catalog, DragDrop, Container, Backpack, Hotbar.Grid);

        Disposables.Add(Container.QuickTransferRequested.Subscribe(coordinates =>
        {
            DragDrop.Cancel();
            _containers.MoveToPlayer(_player, _grid.OwnerId, coordinates);
        }));
        foreach (InventoryGridViewModel source in new[] { Backpack, Hotbar.Grid })
            Disposables.Add(source.QuickTransferRequested.Subscribe(coordinates =>
            {
                DragDrop.Cancel();
                _containers.MoveToGrid(source.OwnerId, coordinates, _grid);
            }));
        Disposables.Add(TakeAll.Executed.Subscribe(_ =>
        {
            DragDrop.Cancel();
            _containers.TakeAll(_player, _grid.OwnerId);
        }));
    }

    protected override void OnDisposed()
    {
        ItemNameHover.Dispose();
        Selection.Dispose();
        DragDrop.Dispose();
        Container.Dispose();
        Backpack.Dispose();
        Hotbar.Dispose();
        TakeAll.Dispose();
        base.OnDisposed();
    }
}
