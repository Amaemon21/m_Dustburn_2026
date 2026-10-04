using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class InventoryGridProxy : IDisposable
{
    private readonly List<InventorySlotProxy> _slots = new();
    private readonly Dictionary<InventorySlotProxy, IDisposable> _slotSubscriptions = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ReactiveProperty<Vector2Int> _size;
    private readonly Subject<Unit> _changed = new();
    private readonly Func<Vector2Int, string, int, bool> _accepts;
    private int _notificationDepth;
    private bool _pendingChange;

    public InventoryGridData Origin { get; }
    public IReadOnlyList<InventorySlotProxy> Slots { get; }
    public ReadOnlyReactiveProperty<Vector2Int> Size => _size;
    public Observable<Unit> Changed => _changed;

    public InventoryGridProxy(InventoryGridData origin, Func<Vector2Int, string, int, bool> accepts = null)
    {
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        _accepts = accepts;
        int count = checked(origin.Size.x * origin.Size.y);
        if (origin.Size.x <= 0 || origin.Size.y <= 0)
            throw new ArgumentOutOfRangeException(nameof(origin.Size));
        origin.Slots ??= new List<InventorySlotData>();
        if (origin.Slots.Count == 0)
            for (int i = 0; i < count; i++)
                origin.Slots.Add(new InventorySlotData());
        if (origin.Slots.Count != count)
            throw new ArgumentException("Slot count must match grid size", nameof(origin));

        _size = new ReactiveProperty<Vector2Int>(origin.Size);
        Slots = _slots.AsReadOnly();
        _subscriptions.Add(_size.Skip(1).Subscribe(value => Origin.Size = value));
        try
        {
            for (int i = 0; i < origin.Slots.Count; i++)
                Add(CreateSlot(origin.Slots[i], InventoryGridLayout.CoordsOf(i, origin.Size)));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Add(InventorySlotProxy slot)
    {
        _slots.Add(slot);
        _slotSubscriptions.Add(slot, slot.Changed.Subscribe(_ => PublishChanged()));
    }

    public void Resize(Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        checked { _ = size.x * size.y; }
        Vector2Int previousSize = _size.Value;
        if (size == previousSize)
            return;
        if (Origin.Kind != InventoryGridKind.Backpack)
            throw new InvalidOperationException("Only backpack grids may be resized");
        for (int x = 0; x < previousSize.x; x++)
            for (int y = 0; y < previousSize.y; y++)
                if ((x >= size.x || y >= size.y) && !_slots[InventoryGridLayout.IndexOf(new Vector2Int(x, y), previousSize)].IsEmpty)
                    throw new InvalidOperationException("Cannot remove occupied slots when shrinking");

        List<InventorySlotProxy> next = new();
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                next.Add(x < previousSize.x && y < previousSize.y
                    ? _slots[InventoryGridLayout.IndexOf(new Vector2Int(x, y), previousSize)] : CreateSlot(new InventorySlotData(), new Vector2Int(x, y)));
        HashSet<InventorySlotProxy> retained = new(next);
        List<InventorySlotProxy> removed = new();
        foreach (InventorySlotProxy slot in _slots)
            if (!retained.Contains(slot))
                removed.Add(slot);
        _slots.Clear();
        Origin.Slots.Clear();
        foreach (InventorySlotProxy slot in next)
        {
            if (_slotSubscriptions.ContainsKey(slot))
                _slots.Add(slot);
            else
                Add(slot);
            Origin.Slots.Add(slot.Origin);
        }
        _size.Value = size;
        foreach (InventorySlotProxy slot in removed)
        {
            _slotSubscriptions[slot].Dispose();
            _slotSubscriptions.Remove(slot);
            slot.Dispose();
        }
        PublishChanged();
    }

    private InventorySlotProxy CreateSlot(InventorySlotData data, Vector2Int coords)
        => new(data, _accepts == null ? null : (itemId, amount) => _accepts(coords, itemId, amount));

    internal IDisposable DeferNotifications()
    {
        _notificationDepth++;
        return new NotificationScope(this);
    }

    private void PublishChanged()
    {
        if (_notificationDepth > 0)
        {
            _pendingChange = true;
            return;
        }
        _pendingChange = false;
        _changed.OnNext(Unit.Default);
    }

    private sealed class NotificationScope : IDisposable
    {
        private InventoryGridProxy _grid;
        public NotificationScope(InventoryGridProxy grid) => _grid = grid;
        public void Dispose()
        {
            if (_grid == null)
                return;
            InventoryGridProxy grid = _grid;
            _grid = null;
            grid._notificationDepth--;
            if (grid._notificationDepth == 0 && grid._pendingChange)
                grid.PublishChanged();
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (IDisposable subscription in _slotSubscriptions.Values)
            subscription.Dispose();
        _slotSubscriptions.Clear();
        foreach (InventorySlotProxy slot in _slots)
            slot.Dispose();
        _size.Dispose();
        _changed.Dispose();
    }
}
