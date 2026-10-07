public sealed class ContainerScreenService
{
    public const string CONTAINER_WINDOW = "container";

    private readonly WindowService _windows;
    private readonly ContainerInventoryService _containers;
    private readonly IInventoryService _inventory;
    private readonly PlayerInventoryService _players;
    private readonly LocalPlayerInventory _player;
    private readonly ItemCatalog _catalog;
    private readonly IItemDropService _drops;

    public ContainerScreenService(WindowService windows, ContainerInventoryService containers, IInventoryService inventory,
        PlayerInventoryService players, LocalPlayerInventory player, ItemCatalog catalog, IItemDropService drops)
    {
        _windows = windows;
        _containers = containers;
        _inventory = inventory;
        _players = players;
        _player = player;
        _catalog = catalog;
        _drops = drops;
    }

    public void Open(string containerId, ContainerConfig config)
    {
        IReadOnlyInventoryGrid grid = _containers.GetOrCreate(containerId, config);
        ContainerScreenViewModel viewModel = new(_inventory, _containers, _player.Proxy, _players, _catalog, _drops,
            grid.OwnerId, config.InteractableName);
        try
        {
            _windows.Open(CONTAINER_WINDOW, viewModel);
        }
        catch
        {
            viewModel.Dispose();
            throw;
        }
    }

    public void Close() => _windows.Close(CONTAINER_WINDOW);
}
