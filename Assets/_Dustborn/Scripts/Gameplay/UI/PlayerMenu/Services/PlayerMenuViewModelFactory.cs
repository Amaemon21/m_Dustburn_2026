public sealed class PlayerMenuViewModelFactory
{
    private readonly IInventoryService _inventory;
    private readonly PlayerInventoryService _players;
    private readonly ItemCatalog _catalog;
    private readonly IItemDropService _drops;

    public PlayerMenuViewModelFactory(IInventoryService inventory, PlayerInventoryService players, ItemCatalog catalog,
        IItemDropService drops)
    {
        _inventory = inventory;
        _players = players;
        _catalog = catalog;
        _drops = drops;
    }

    public PlayerMenuScreenViewModel Create(string ownerId, PlayerMenuTab tab)
    {
        PlayerMenuScreenViewModel viewModel = new(_inventory, _players.GetPlayerInventory(ownerId), _players, _catalog, _drops);
        viewModel.SetTab(tab);
        return viewModel;
    }
}
