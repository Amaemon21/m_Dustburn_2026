using System;
using R3;

public sealed class PlayerMenuService : IDisposable
{
    public const string PLAYER_MENU_WINDOW = "player-menu";
    private readonly PlayerMenuViewModelFactory _factory;
    private readonly WindowService _windows;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ReactiveProperty<bool> _menuOpen = new(false);
    public ReadOnlyReactiveProperty<bool> IsPlayerMenuOpen => _menuOpen;

    public PlayerMenuService(PlayerMenuViewModelFactory factory, WindowService windows)
    {
        _factory = factory;
        _windows = windows;
        _subscriptions.Add(windows.Opened.Subscribe(_ => UpdateState()));
        _subscriptions.Add(windows.Closed.Subscribe(_ => UpdateState()));
        UpdateState();
    }

    private void UpdateState() => _menuOpen.Value = _windows.TryGet(PLAYER_MENU_WINDOW, out _);

    public void OpenPlayerMenu(string ownerId, PlayerMenuTab tab = PlayerMenuTab.Inventory)
    {
        if (_windows.TryGet(PLAYER_MENU_WINDOW, out WindowViewModel current) &&
            current is PlayerMenuScreenViewModel menu && menu.OwnerId == ownerId)
        {
            menu.SetTab(tab);
            _windows.Focus(PLAYER_MENU_WINDOW);
            return;
        }
        PlayerMenuScreenViewModel next = _factory.Create(ownerId, tab);
        try
        {
            _windows.Open(PLAYER_MENU_WINDOW, next);
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    public void ClosePlayerMenu() => _windows.Close(PLAYER_MENU_WINDOW);
    public void TogglePlayerMenu(string ownerId)
    {
        if (_menuOpen.Value)
            ClosePlayerMenu();
        else
            OpenPlayerMenu(ownerId);
    }

    public void OpenInventory(string ownerId) => OpenPlayerMenu(ownerId);
    public void CloseInventory() => ClosePlayerMenu();
    public void Dispose()
    {
        _subscriptions.Dispose();
        _menuOpen.Dispose();
    }
}
