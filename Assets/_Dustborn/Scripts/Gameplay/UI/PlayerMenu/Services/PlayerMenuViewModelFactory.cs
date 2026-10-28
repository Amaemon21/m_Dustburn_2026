public sealed class PlayerMenuViewModelFactory
{
    private readonly IInventoryService _inventory;
    private readonly PlayerInventoryService _players;
    private readonly InventoryWeightService _weights;
    private readonly ItemCatalog _catalog;
    private readonly IItemDropService _drops;
    private readonly IItemUseService _uses;
    private readonly PlayerVitals _vitals;
    private readonly PlayerProgression _progression;

    public PlayerMenuViewModelFactory(IInventoryService inventory, PlayerInventoryService players,
        InventoryWeightService weights, ItemCatalog catalog, IItemDropService drops, IItemUseService uses,
        PlayerVitals vitals, PlayerProgression progression)
    {
        _inventory = inventory;
        _players = players;
        _weights = weights;
        _catalog = catalog;
        _drops = drops;
        _uses = uses;
        _vitals = vitals;
        _progression = progression;
    }

    public PlayerMenuScreenViewModel Create(string ownerId, PlayerMenuTab tab)
    {
        _weights.TryGet(ownerId, out IReadOnlyInventoryWeight weight);
        PlayerMenuScreenViewModel viewModel = new(_inventory, _players.GetPlayerInventory(ownerId), _players, _catalog,
            _drops, weight, _uses, CreateStatus());
        viewModel.SetTab(tab);
        return viewModel;
    }

    private PlayerStatusViewModel CreateStatus()
        => _vitals == null || _progression == null ? null : new PlayerStatusViewModel(_vitals, _progression);
}
