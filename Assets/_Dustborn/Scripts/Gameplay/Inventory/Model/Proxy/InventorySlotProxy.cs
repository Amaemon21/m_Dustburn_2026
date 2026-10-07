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
    public int Wear => _current.Wear;
    public ReadOnlyReactiveProperty<InventorySlotState> State => _state;
    public Observable<Unit> Changed => _changed;
    public bool IsEmpty => Amount == 0;

    public InventorySlotProxy(InventorySlotData origin, Func<string, int, bool> accepts = null)
    {
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        _accepts = accepts;
        Validate(origin.ItemId, origin.Amount, origin.Wear);
        _current = Normalize(origin.ItemId, origin.Amount, origin.Wear);
        _state = new ReactiveProperty<InventorySlotState>(_current);
    }

    public void Set(string itemId, int amount, int wear)
    {
        Validate(itemId, amount, wear);
        _current = Normalize(itemId, amount, wear);
        Origin.ItemId = _current.ItemId;
        Origin.Amount = _current.Amount;
        Origin.Wear = _current.Wear;
        Publish();
    }

    private static InventorySlotState Normalize(string itemId, int amount, int wear)
        => amount == 0 ? new InventorySlotState(null, 0) : new InventorySlotState(itemId, amount, wear);

    private void Validate(string itemId, int amount, int wear)
    {
        if (amount < 0 || amount > MAX_AMOUNT || wear < 0 || (amount > 0 && string.IsNullOrWhiteSpace(itemId)))
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
