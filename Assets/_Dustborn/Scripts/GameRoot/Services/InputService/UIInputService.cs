using R3;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class UIInputService : InputService
{
    private readonly Subject<Unit> _playerMenuPressed = new();
    private readonly Subject<Unit> _closePressed = new();
    private readonly Subject<int> _hotbarSlotPressed = new();
    private readonly Subject<int> _scrolled = new();
    private readonly Subject<Unit> _takeAllPressed = new();
    private readonly Subject<Unit> _interactClosePressed = new();
    private readonly Subject<Unit> _useItemPressed = new();
    private readonly Subject<Unit> _dropItemPressed = new();
    private readonly InputAction _quickTransfer;
    private readonly InputAction _useItem;
    private readonly InputAction _dropItem;

    public Observable<Unit> PlayerMenuPressed => _playerMenuPressed;
    public Observable<Unit> ClosePressed => _closePressed;
    public Observable<int> HotbarSlotPressed => _hotbarSlotPressed;
    public Observable<int> Scrolled => _scrolled;
    public Observable<Unit> TakeAllPressed => _takeAllPressed;
    public Observable<Unit> InteractClosePressed => _interactClosePressed;
    public Observable<Unit> UseItemPressed => _useItemPressed;
    public Observable<Unit> DropItemPressed => _dropItemPressed;
    public string UseItemBinding => BindingOf(_useItem);
    public string DropItemBinding => BindingOf(_dropItem);
    public bool IsQuickTransferPressed => _quickTransfer != null && _quickTransfer.IsPressed();

    public UIInputService(UIInputSettings settings)
    {
        if (settings.QuickTransfer != null)
            _quickTransfer = RegisterAction(settings.QuickTransfer);
        if (settings.Scroll != null)
            Bindings.Add(ObservePerformed(settings.Scroll).Subscribe(context =>
            {
                int direction = Math.Sign(context.ReadValue<Vector2>().y);
                if (direction != 0)
                    _scrolled.OnNext(direction);
            }));
        if (settings.TakeAll != null)
            Bindings.Add(ObservePerformed(settings.TakeAll).Subscribe(_ => _takeAllPressed.OnNext(Unit.Default)));
        if (settings.InteractClose != null)
            Bindings.Add(ObservePerformed(settings.InteractClose).Subscribe(_ => _interactClosePressed.OnNext(Unit.Default)));
        if (settings.UseItem != null)
        {
            _useItem = settings.UseItem.action;
            Bindings.Add(ObservePerformed(settings.UseItem).Subscribe(_ => _useItemPressed.OnNext(Unit.Default)));
        }
        if (settings.DropItem != null)
        {
            _dropItem = settings.DropItem.action;
            Bindings.Add(ObservePerformed(settings.DropItem).Subscribe(_ => _dropItemPressed.OnNext(Unit.Default)));
        }
        Bindings.Add(ObservePerformed(settings.TogglePlayerMenu).Subscribe(_ => _playerMenuPressed.OnNext(Unit.Default)));
        Bindings.Add(ObservePerformed(settings.CloseWindow).Subscribe(_ => _closePressed.OnNext(Unit.Default)));
        
        for (int i = 0; i < settings.HotbarSlots.Count; i++)
        {
            int index = i;
            Bindings.Add(ObservePerformed(settings.HotbarSlots[i]).Subscribe(_ => _hotbarSlotPressed.OnNext(index)));
        }
    }

    private static string BindingOf(InputAction action) => BindingDisplay.Of(action);

    protected override void OnDisposed()
    {
        _useItemPressed.Dispose();
        _dropItemPressed.Dispose();
        _playerMenuPressed.Dispose();
        _closePressed.Dispose();
        _hotbarSlotPressed.Dispose();
        _scrolled.Dispose();
        _takeAllPressed.Dispose();
        _interactClosePressed.Dispose();
        base.OnDisposed();
    }
}
