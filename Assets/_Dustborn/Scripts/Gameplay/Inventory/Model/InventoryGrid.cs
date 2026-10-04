using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class InventoryGrid : IReadOnlyInventoryGrid, IDisposable
{
    private const int EQUIPMENT_CAPACITY = 1;

    private readonly IReadOnlyList<InventorySlotProxy> _slots;
    private readonly Subject<(string ItemId, int Amount)> _itemsAdded = new();
    private readonly Subject<(string ItemId, int Amount)> _itemsRemoved = new();
    private readonly ItemCatalog _catalog;
    public InventoryGridKind Kind { get; }

    public string OwnerId { get; }
    public InventoryGridProxy Proxy { get; }
    public Vector2Int Size => Proxy.Size.CurrentValue;
    public Observable<Vector2Int> SizeChanged => Proxy.Size.Skip(1);
    public Observable<(string ItemId, int Amount)> ItemsAdded => _itemsAdded;
    public Observable<(string ItemId, int Amount)> ItemsRemoved => _itemsRemoved;
    public Observable<Unit> Changed => Proxy.Changed;

    public InventoryGrid(InventoryGridData data, ItemCatalog catalog)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.OwnerId))
            throw new ArgumentException("Inventory owner is required", nameof(data));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ValidateSize(data.Size);
        Kind = data.Kind;
        ValidateKindSize(data.Size);
        OwnerId = data.OwnerId;
        Proxy = new InventoryGridProxy(data, (coords, itemId, amount) =>
            amount <= CapacityFor(itemId) && (amount == 0 || Accepts(coords, itemId)));
        _slots = Proxy.Slots;
    }

    public int CapacityFor(string itemId) => Kind == InventoryGridKind.Equipment ? EQUIPMENT_CAPACITY : _catalog.MaxStack(itemId);

    public AddItemsToInventoryGridResult AddItems(string itemId, int amount = 1)
    {
        using IDisposable update = Proxy.DeferNotifications();
        ValidateItems(itemId, amount);
        int remaining = Fill(itemId, amount, false);
        remaining = Fill(itemId, remaining, true);
        return CompleteAdd(itemId, amount, amount - remaining);
    }

    public AddItemsToInventoryGridResult AddItemsToExistingStacks(string itemId, int amount)
    {
        using IDisposable update = Proxy.DeferNotifications();
        ValidateItems(itemId, amount);
        int remaining = Fill(itemId, amount, false);
        return CompleteAdd(itemId, amount, amount - remaining);
    }

    public AddItemsToInventoryGridResult AddItems(Vector2Int coords, string itemId, int amount = 1)
    {
        using IDisposable update = Proxy.DeferNotifications();
        ValidateItems(itemId, amount);
        InventorySlotProxy slot = Slot(coords);
        if (!Accepts(coords, itemId) || (!slot.IsEmpty && slot.ItemId != itemId))
            return new AddItemsToInventoryGridResult(OwnerId, amount, 0);

        int added = Math.Min(amount, CapacityFor(itemId) - slot.Amount);
        if (added > 0)
            slot.Set(itemId, slot.Amount + added);
        int remaining = Fill(itemId, amount - added, false);
        remaining = Fill(itemId, remaining, true);
        return CompleteAdd(itemId, amount, amount - remaining);
    }

    public RemoveItemsFromInventoryGridResult RemoveItems(string itemId, int amount = 1)
    {
        using IDisposable update = Proxy.DeferNotifications();
        ValidateItems(itemId, amount);
        if (!Has(itemId, amount))
            return new RemoveItemsFromInventoryGridResult(OwnerId, amount, false);
        int remaining = amount;
        foreach (InventorySlotProxy slot in _slots)
        {
            if (slot.ItemId != itemId)
                continue;
            int removed = Math.Min(remaining, slot.Amount);
            slot.Set(itemId, slot.Amount - removed);
            remaining -= removed;
            if (remaining == 0)
                break;
        }
        return CompleteRemove(itemId, amount);
    }

    public RemoveItemsFromInventoryGridResult RemoveItems(Vector2Int coords, string itemId, int amount = 1)
    {
        using IDisposable update = Proxy.DeferNotifications();
        ValidateItems(itemId, amount);
        InventorySlotProxy slot = Slot(coords);
        if (slot.ItemId != itemId || slot.Amount < amount)
            return new RemoveItemsFromInventoryGridResult(OwnerId, amount, false);
        slot.Set(itemId, slot.Amount - amount);
        return CompleteRemove(itemId, amount);
    }

    public int GetAmount(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;
        int amount = 0;
        foreach (InventorySlotProxy slot in _slots)
            if (slot.ItemId == itemId)
                amount = checked(amount + slot.Amount);
        return amount;
    }

    public bool Has(string itemId, int amount = 1)
    {
        ValidateItems(itemId, amount);
        return GetAmount(itemId) >= amount;
    }

    public void SwitchSlots(Vector2Int first, Vector2Int second)
    {
        SwapWith(first, this, second);
    }

    public bool MoveTo(Vector2Int source, InventoryGrid targetGrid, Vector2Int target, int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        InventorySlotProxy from = Slot(source);
        InventorySlotProxy to = targetGrid.Slot(target);
        if (ReferenceEquals(from, to) || from.IsEmpty || from.Amount < amount ||
            !targetGrid.Accepts(target, from.ItemId) || (!to.IsEmpty && to.ItemId != from.ItemId) ||
            amount > targetGrid.CapacityFor(from.ItemId) - to.Amount)
            return false;

        string itemId = from.ItemId;
        using IDisposable sourceUpdate = Proxy.DeferNotifications();
        using IDisposable targetUpdate = targetGrid.Proxy.DeferNotifications();
        using (from.DeferNotifications())
        using (to.DeferNotifications())
        {
            from.Set(itemId, from.Amount - amount);
            to.Set(itemId, to.Amount + amount);
        }
        if (!ReferenceEquals(this, targetGrid))
        {
            _itemsRemoved.OnNext((itemId, amount));
            targetGrid._itemsAdded.OnNext((itemId, amount));
        }
        return true;
    }

    public bool SwapWith(Vector2Int first, InventoryGrid secondGrid, Vector2Int second)
    {
        InventorySlotProxy a = Slot(first);
        InventorySlotProxy b = secondGrid.Slot(second);
        if (ReferenceEquals(a, b))
            return true;
        if ((!b.IsEmpty && (!Accepts(first, b.ItemId) || b.Amount > CapacityFor(b.ItemId))) ||
            (!a.IsEmpty && (!secondGrid.Accepts(second, a.ItemId) || a.Amount > secondGrid.CapacityFor(a.ItemId))))
            return false;
        if (a.State.CurrentValue.Equals(b.State.CurrentValue))
            return true;

        InventorySlotState state = a.State.CurrentValue;
        InventorySlotState other = b.State.CurrentValue;
        using IDisposable firstUpdate = Proxy.DeferNotifications();
        using IDisposable secondUpdate = secondGrid.Proxy.DeferNotifications();
        using (a.DeferNotifications())
        using (b.DeferNotifications())
        {
            a.Set(other.ItemId, other.Amount);
            b.Set(state.ItemId, state.Amount);
        }
        if (!ReferenceEquals(this, secondGrid))
        {
            if (!state.IsEmpty)
            {
                _itemsRemoved.OnNext((state.ItemId, state.Amount));
                secondGrid._itemsAdded.OnNext((state.ItemId, state.Amount));
            }
            if (!other.IsEmpty)
            {
                secondGrid._itemsRemoved.OnNext((other.ItemId, other.Amount));
                _itemsAdded.OnNext((other.ItemId, other.Amount));
            }
        }
        return true;
    }

    private bool Accepts(Vector2Int coords, string itemId)
        => Kind != InventoryGridKind.Equipment || _catalog.CanEquip(itemId, (EquipmentSlot)coords.x);

    private void ValidateKindSize(Vector2Int size)
    {
        if (!Enum.IsDefined(typeof(InventoryGridKind), Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind == InventoryGridKind.Hotbar && size.y != 1)
            throw new ArgumentException("Hotbar must have one row", nameof(size));
        if (Kind == InventoryGridKind.Equipment && (size.x != Enum.GetValues(typeof(EquipmentSlot)).Length || size.y != 1))
            throw new ArgumentException("Equipment grid must match the equipment slot enum", nameof(size));
    }

    public void SetSize(Vector2Int size)
    {
        ValidateSize(size);
        ValidateKindSize(size);
        Proxy.Resize(size);
    }

    public IReadOnlyInventorySlot GetSlot(Vector2Int coords) => Slot(coords);

    public IReadOnlyInventorySlot[,] GetSlots()
    {
        IReadOnlyInventorySlot[,] result = new IReadOnlyInventorySlot[Size.x, Size.y];
        for (int x = 0; x < Size.x; x++)
            for (int y = 0; y < Size.y; y++)
                result[x, y] = Slot(new Vector2Int(x, y));
        return result;
    }

    private InventorySlotProxy Slot(Vector2Int coords)
    {
        if (!InventoryGridLayout.Contains(coords, Size))
            throw new ArgumentOutOfRangeException(nameof(coords));
        return _slots[InventoryGridLayout.IndexOf(coords, Size)];
    }

    private int Fill(string itemId, int remaining, bool empty)
    {
        int capacity = CapacityFor(itemId);
        for (int i = 0; i < _slots.Count; i++)
        {
            InventorySlotProxy slot = _slots[i];
            if (remaining == 0)
                break;
            if (!Accepts(InventoryGridLayout.CoordsOf(i, Size), itemId) ||
                slot.IsEmpty != empty || (!empty && slot.ItemId != itemId))
                continue;
            int added = Math.Min(remaining, capacity - slot.Amount);
            if (added <= 0)
                continue;
            slot.Set(itemId, slot.Amount + added);
            remaining -= added;
        }
        return remaining;
    }

    private AddItemsToInventoryGridResult CompleteAdd(string itemId, int requested, int added)
    {
        if (added > 0)
        {
            _itemsAdded.OnNext((itemId, added));
        }
        return new AddItemsToInventoryGridResult(OwnerId, requested, added);
    }

    private RemoveItemsFromInventoryGridResult CompleteRemove(string itemId, int amount)
    {
        _itemsRemoved.OnNext((itemId, amount));
        return new RemoveItemsFromInventoryGridResult(OwnerId, amount, true);
    }

    private static void ValidateItems(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item id is required", nameof(itemId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
    }

    private static void ValidateSize(Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
    }

    public void Dispose()
    {
        Proxy.Dispose();
        _itemsAdded.Dispose();
        _itemsRemoved.Dispose();
    }
}
