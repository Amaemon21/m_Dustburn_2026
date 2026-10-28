public sealed class HotbarViewModel : ViewModel
{
    public InventoryGridViewModel Grid { get; }

    public HotbarViewModel(IInventoryService inventory, PlayerInventoryProxy player, ItemCatalog catalog,
        InventoryDragDropViewModel dragDrop = null)
    {
        Grid = new InventoryGridViewModel(inventory, player.Hotbar.OwnerId, catalog, dragDrop);
    }

    protected override void OnDisposed()
    {
        Grid.Dispose();
        base.OnDisposed();
    }
}
