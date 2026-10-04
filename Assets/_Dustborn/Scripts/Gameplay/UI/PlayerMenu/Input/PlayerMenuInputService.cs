using System;
using R3;

public sealed class PlayerMenuInputService : IGameplayActivatable, IDisposable
{
    private readonly UIInputService _input;
    private readonly PlayerMenuService _screen;
    private readonly PlayerInventoryService _inventory;
    private readonly IScreenService _screenService;
    private readonly LocalPlayerInventory _player;
    private readonly CompositeDisposable _subscriptions = new();

    public PlayerMenuInputService(UIInputService input, PlayerMenuService screen, PlayerInventoryService inventory, IScreenService screenService, LocalPlayerInventory player)
    {
        _input = input;
        _screen = screen;
        _inventory = inventory;
        _screenService = screenService;
        _player = player;
    }

    public void Activate()
    {
        _subscriptions.Clear();
        PlayerInventoryProxy player = _player.Proxy;

        _subscriptions.Add(_input.PlayerMenuPressed.Subscribe(_ => _screen.TogglePlayerMenu(player.OwnerId)));
        _subscriptions.Add(_input.ClosePressed.Subscribe(_ => _screenService.CloseTop()));
        
        _subscriptions.Add(_input.HotbarSlotPressed.Subscribe(index =>
        {
            if (!_screenService.HasAnyWindowOpen() && index < player.Hotbar.Size.x)
                _inventory.SelectHotbarSlot(player.OwnerId, index);
        }));
        
        _subscriptions.Add(_input.Scrolled.Subscribe(direction =>
        {
            if (!_screenService.HasAnyWindowOpen())
                _inventory.CycleHotbarSlot(player.OwnerId, -direction);
        }));
        _input.Enable();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _input.Disable();
    }
}
