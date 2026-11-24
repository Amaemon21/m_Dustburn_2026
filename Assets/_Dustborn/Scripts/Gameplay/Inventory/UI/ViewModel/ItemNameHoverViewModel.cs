using System;
using R3;
using UnityEngine;

public sealed class ItemNameHoverViewModel : ViewModel
{
    public static readonly TimeSpan SHOW_DELAY = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan SWITCH_GRACE = TimeSpan.FromSeconds(0.3);

    private readonly ItemCatalog _catalog;
    private readonly InventoryDragDropViewModel _dragDrop;
    private readonly Func<TimeSpan, Observable<Unit>> _timer;
    private readonly ReactiveProperty<string> _name = new();
    private readonly ReactiveProperty<string> _details = new();
    private readonly ReactiveProperty<Vector3> _anchor = new(Vector3.zero);
    private readonly CompositeDisposable _slotBindings = new();
    private readonly SerialDisposable _delay = new();
    private readonly SerialDisposable _grace = new();
    private InventoryGridViewModel _grid;
    private InventorySlotViewModel _slot;
    private bool _inGrace;

    public ReadOnlyReactiveProperty<string> Name => _name;
    public ReadOnlyReactiveProperty<string> Details => _details;
    public ReadOnlyReactiveProperty<Vector3> Anchor => _anchor;

    public ItemNameHoverViewModel(ItemCatalog catalog, InventoryDragDropViewModel dragDrop, params InventoryGridViewModel[] grids)
        : this(catalog, dragDrop, dueTime => Observable.Timer(dueTime), grids)
    {
    }

    public ItemNameHoverViewModel(ItemCatalog catalog, InventoryDragDropViewModel dragDrop, Func<TimeSpan, Observable<Unit>> timer,
        params InventoryGridViewModel[] grids)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _dragDrop = dragDrop;
        _timer = timer ?? (dueTime => Observable.Timer(dueTime));
        foreach (InventoryGridViewModel grid in grids)
            Watch(grid);
        if (dragDrop != null)
            Disposables.Add(dragDrop.State.Subscribe(_ => Refresh()));
    }

    private void Watch(InventoryGridViewModel grid)
    {
        CompositeDisposable hovers = new();
        Disposables.Add(hovers);
        Disposables.Add(grid.Slots.Subscribe(slots =>
        {
            hovers.Clear();
            if (ReferenceEquals(_grid, grid))
                Release();
            foreach (InventorySlotViewModel slot in slots)
                hovers.Add(slot.Hover.Subscribe(anchor => OnHover(grid, slot, anchor)));
        }));
    }

    private void OnHover(InventoryGridViewModel grid, InventorySlotViewModel slot, Vector3? anchor)
    {
        if (anchor.HasValue)
        {
            Track(grid, slot, anchor.Value);
            return;
        }

        if (ReferenceEquals(_slot, slot))
            Release();
    }

    private void Track(InventoryGridViewModel grid, InventorySlotViewModel slot, Vector3 anchor)
    {
        _slotBindings.Clear();
        _grid = grid;
        _slot = slot;
        _anchor.Value = anchor;
        _slotBindings.Add(slot.State.Subscribe(_ => Refresh()));
    }

    private void Release()
    {
        _slotBindings.Clear();
        _grid = null;
        _slot = null;
        Refresh();
    }

    private void Refresh()
    {
        bool visible = _slot != null && !_slot.IsDisposed && (_dragDrop == null || !_dragDrop.IsDragging);
        InventorySlotState state = visible ? _slot.State.CurrentValue : default;
        InventoryItem item = visible ? _catalog.GetItem(state.ItemId) : null;

        if (item == null || string.IsNullOrEmpty(item.ItemName))
        {
            Hide();
            return;
        }

        if (_name.Value != null || _inGrace)
        {
            Show(item, state);
            return;
        }

        _delay.Disposable = _timer(SHOW_DELAY).Take(1).Subscribe(_ => Show(item, state));
    }

    private void Show(InventoryItem item, InventorySlotState state)
    {
        _delay.Disposable = Disposable.Empty;
        _grace.Disposable = Disposable.Empty;
        _inGrace = false;
        string details = ItemInfoText.Subtitle(item);
        float? durability = _catalog.RemainingDurability(state);

        if (durability.HasValue)
        {
            ItemStat stat = new(ItemStatType.Durability, 0f);
            details += $"  ·  {ItemStatText.Label(stat)} {durability.Value:P0}";
        }
        else if (state.Amount > 1)
        {
            details += $"  ·  ×{state.Amount}";
        }

        _details.Value = details;
        _name.Value = item.ItemName;
    }

    private void Hide()
    {
        _delay.Disposable = Disposable.Empty;
        if (_name.Value == null)
            return;

        _name.Value = null;
        _details.Value = null;
        _inGrace = true;
        _grace.Disposable = _timer(SWITCH_GRACE).Take(1).Subscribe(_ => _inGrace = false);
    }

    protected override void OnDisposed()
    {
        _delay.Dispose();
        _grace.Dispose();
        _slotBindings.Dispose();
        _name.Dispose();
        _details.Dispose();
        _anchor.Dispose();
        base.OnDisposed();
    }
}
