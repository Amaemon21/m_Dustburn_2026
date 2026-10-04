using System;
using R3;

public sealed class PlayerInventoryProxy : IDisposable
{
    private readonly ReactiveProperty<int> _selected;
    private readonly CompositeDisposable _subscriptions = new();
    public PlayerInventoryData Origin { get; }
    public string OwnerId { get; }
    public IReadOnlyInventoryGrid Backpack { get; }
    public IReadOnlyInventoryGrid Hotbar { get; }
    public IReadOnlyInventoryGrid Equipment { get; }
    public ReadOnlyReactiveProperty<int> SelectedHotbarSlot => _selected;

    public PlayerInventoryProxy(PlayerInventoryData data, IReadOnlyInventoryGrid backpack, IReadOnlyInventoryGrid hotbar, IReadOnlyInventoryGrid equipment)
    {
        Origin = data;
        OwnerId = data.OwnerId;
        Backpack = backpack;
        Hotbar = hotbar;
        Equipment = equipment ;

        if (data.SelectedHotbarSlot < 0 || data.SelectedHotbarSlot >= hotbar.Size.x)
            data.SelectedHotbarSlot = 0;
        _selected = new ReactiveProperty<int>(data.SelectedHotbarSlot);
        _subscriptions.Add(_selected.Skip(1).Subscribe(value => Origin.SelectedHotbarSlot = value));
    }

    internal void SelectHotbarSlot(int index)
    {
        if (index < 0 || index >= Hotbar.Size.x)
            throw new ArgumentOutOfRangeException(nameof(index));
        _selected.Value = index;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _selected.Dispose();
    }
}
