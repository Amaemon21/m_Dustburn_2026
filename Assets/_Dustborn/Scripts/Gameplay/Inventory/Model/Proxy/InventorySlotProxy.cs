using System;
using R3;

public sealed class InventorySlotProxy : IReadOnlyInventorySlot, IDisposable
{
    public const int MAX_AMOUNT = 999;
    private readonly ReactiveProperty<InventorySlotState> _state;
    private readonly Subject<Unit> _changed = new();
    private readonly Func<string, int, bool> _accepts;
    private InventorySlotState _current;
    private int _notificationDepth;

    public InventorySlotData Origin { get; }
    public string ItemId => _current.ItemId;
    public int Amount => _current.Amount;
    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public Observable<Unit> Changed => _changed;
    public bool IsEmpty => Amount == 0;

    public InventorySlotProxy(InventorySlotData origin, Func<string, int, bool> accepts = null)
    {
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        _accepts = accepts;
        Validate(origin.ItemId, origin.Amount);
        _current = new InventorySlotState(origin.Amount == 0 ? null : origin.ItemId, origin.Amount);
        _state = new ReactiveProperty<InventorySlotState>(_current);
    }

    public void Set(string itemId, int amount)
    {
        Validate(itemId, amount);
        _current = new InventorySlotState(amount == 0 ? null : itemId, amount);
        Origin.ItemId = _current.ItemId;
        Origin.Amount = _current.Amount;
        Publish();
    }

    private void Validate(string itemId, int amount)
    {
        if (amount < 0 || amount > MAX_AMOUNT || (amount > 0 && string.IsNullOrWhiteSpace(itemId)))
            throw new ArgumentException("Invalid inventory slot state");
        if (_accepts != null && !_accepts(itemId, amount))
            throw new ArgumentException("Item is not allowed in this slot");
    }

    internal IDisposable DeferNotifications()
    {
        _notificationDepth++;
        return new NotificationScope(this);
    }

    private void Publish()
    {
        if (_notificationDepth > 0 || _state.Value.Equals(_current))
            return;
        _state.Value = _current;
        _changed.OnNext(Unit.Default);
    }

    private sealed class NotificationScope : IDisposable
    {
        private InventorySlotProxy _slot;
        public NotificationScope(InventorySlotProxy slot) => _slot = slot;
        public void Dispose()
        {
            if (_slot == null)
                return;
            InventorySlotProxy slot = _slot;
            _slot = null;
            slot._notificationDepth--;
            slot.Publish();
        }
    }

    public void Dispose()
    {
        _state.Dispose();
        _changed.Dispose();
    }
}
