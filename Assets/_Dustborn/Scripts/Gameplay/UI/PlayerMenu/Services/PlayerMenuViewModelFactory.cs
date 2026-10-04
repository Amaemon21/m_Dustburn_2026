public sealed class PlayerMenuViewModelFactory
{
    private readonly IInventoryService _inventory;
    private readonly PlayerInventoryService _players;
    private readonly ItemCatalog _catalog;

    public PlayerMenuViewModelFactory(IInventoryService inventory, PlayerInventoryService players, ItemCatalog catalog)
    {
        _inventory = inventory;
        _players = players;
        _catalog = catalog;
    }

    public PlayerMenuScreenViewModel Create(string ownerId, PlayerMenuTab tab)
    {
        PlayerMenuScreenViewModel viewModel = new(_inventory, _players.GetPlayerInventory(ownerId), _players, _catalog);
        viewModel.SetTab(tab);
        return viewModel;
    }
}
