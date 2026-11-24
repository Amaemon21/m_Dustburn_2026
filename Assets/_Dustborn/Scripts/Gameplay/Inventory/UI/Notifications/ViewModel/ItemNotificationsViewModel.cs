using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class ItemNotificationsViewModel : ViewModel
{
    public const int MAX_VISIBLE = 5;

    private const string EXPERIENCE_KEY = "experience";

    private static readonly TimeSpan LIFETIME = TimeSpan.FromSeconds(3);

    private readonly PlayerInventoryProxy _player;
    private readonly ItemCatalog _catalog;
    private readonly List<ItemNotificationViewModel> _active = new();
    private readonly Subject<ItemNotificationViewModel> _added = new();

    public IReadOnlyList<ItemNotificationViewModel> Active => _active;
    public Observable<ItemNotificationViewModel> Added => _added;

    public ItemNotificationsViewModel(PlayerInventoryProxy player, PlayerInventoryService players, ItemCatalog catalog)
    {
        _player = player;
        _catalog = catalog;
        Disposables.Add(players.PickedUp
            .Where(gain => gain.OwnerId == player.OwnerId)
            .Subscribe(gain => Notify(gain.Stack)));
    }

    public void Notify(InventorySlotState stack)
    {
        if (stack.IsEmpty || string.IsNullOrWhiteSpace(stack.ItemId))
            return;

        int total = _player.Backpack.GetAmount(stack.ItemId) + _player.Hotbar.GetAmount(stack.ItemId);
        InventoryItem item = _catalog.GetItem(stack.ItemId);

        if (item != null && item.Icon != null)
            Show(NotificationKind.Item, stack.ItemId, stack.Amount, total, item.Icon);
        else
            Show(NotificationKind.Item, stack.ItemId, stack.Amount, total);
    }

    public void NotifyExperience(int amount)
    {
        if (amount > 0)
            Show(NotificationKind.Experience, EXPERIENCE_KEY, amount, 0);
    }

    private void Show(NotificationKind kind, string key, int amount, int total, Sprite icon = null)
    {
        if (IsDisposed)
            return;

        ItemNotificationViewModel current = Find(kind, key);

        if (current != null)
        {
            current.Add(amount, total);
            return;
        }

        ItemNotificationViewModel entry = icon != null
            ? new(kind, key, amount, total, LIFETIME, Remove, icon)
            : new(kind, key, amount, total, LIFETIME, Remove);
        _active.Add(entry);
        _added.OnNext(entry);

        while (_active.Count > MAX_VISIBLE)
            _active[0].Expire();
    }

    private ItemNotificationViewModel Find(NotificationKind kind, string key)
    {
        foreach (ItemNotificationViewModel entry in _active)
            if (entry.Kind == kind && entry.Key == key)
                return entry;

        return null;
    }

    private void Remove(ItemNotificationViewModel entry)
    {
        _active.Remove(entry);
        entry.Dispose();
    }

    protected override void OnDisposed()
    {
        foreach (ItemNotificationViewModel entry in _active.ToArray())
            entry.Dispose();
        _active.Clear();
        _added.Dispose();
        base.OnDisposed();
    }
}
