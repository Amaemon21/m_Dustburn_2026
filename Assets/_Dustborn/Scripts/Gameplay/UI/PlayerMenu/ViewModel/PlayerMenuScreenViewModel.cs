using R3;

public sealed class PlayerMenuScreenViewModel : WindowViewModel
{
    private readonly ReactiveProperty<PlayerMenuTab> _selectedTab = new(PlayerMenuTab.Inventory);
    public string OwnerId => Inventory.Player.OwnerId;
    public InventoryTabViewModel Inventory { get; }
    public ReadOnlyReactiveProperty<PlayerMenuTab> SelectedTab => _selectedTab;
    public ReactiveCommand<PlayerMenuTab> SelectTab { get; } = new();

    public PlayerMenuScreenViewModel(IInventoryService inventory, PlayerInventoryProxy player, PlayerInventoryService players,
        ItemCatalog catalog)
    {
        Inventory = new InventoryTabViewModel(inventory, player, players, catalog);
        Disposables.Add(SelectTab.Executed.Subscribe(SetTab));
    }

    public void SetTab(PlayerMenuTab tab)
    {
        _selectedTab.Value = tab;
    }

    protected override void OnDisposed()
    {
        Inventory.Dispose();
        _selectedTab.Dispose();
        SelectTab.Dispose();
        base.OnDisposed();
    }
}
