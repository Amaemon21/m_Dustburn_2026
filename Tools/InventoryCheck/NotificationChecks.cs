using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyItemNotifications()
    {
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        ItemCatalog catalog = new();
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("note", new Vector2Int(4, 2), 2);
        PlayerInventoryProxy other = players.GetOrCreatePlayerInventory("other", new Vector2Int(2, 1), 1);
        PlayerInventoryProxy small = players.GetOrCreatePlayerInventory("small", new Vector2Int(1, 1), 1);
        using ItemNotificationsViewModel notes = new(player, players, catalog);
        using ItemNotificationsViewModel smallNotes = new(small, players, catalog);
        List<ItemNotificationViewModel> added = new();
        using IDisposable listening = notes.Added.Subscribe(added.Add);

        players.PickUp(player.OwnerId, "wood", 5);
        Check(added.Count == 1 && notes.Active.Count == 1 && added[0].Kind == NotificationKind.Item &&
            added[0].Text.CurrentValue == "+5(5)", "a pickup reads +amount(total)");
        players.PickUp(player.OwnerId, "wood", 3);
        Check(added.Count == 1 && added[0].Amount == 8 && added[0].Text.CurrentValue == "+8(8)",
            "the same item adds to its live notification");
        notes.NotifyExperience(10);
        notes.NotifyExperience(5);
        Check(added.Count == 2 && added[1].Kind == NotificationKind.Experience && added[1].Text.CurrentValue == "+15",
            "experience reads +amount and adds up like an item");
        notes.NotifyExperience(0);
        Check(added.Count == 2 && added[1].Amount == 15, "no experience, no notification");
        players.PickUp(other.OwnerId, "stone", 2);
        Check(added.Count == 2, "another player's pickups are not shown");
        inventory.AddItemsToInventory(player.Backpack.OwnerId, "stone", 4);
        Check(added.Count == 2, "items moved in without a pickup are not announced");

        foreach (string itemId in new[] { "a", "b", "c", "d" })
            players.PickUp(player.OwnerId, itemId, 1);
        Check(notes.Active.Count == ItemNotificationsViewModel.MAX_VISIBLE && added[0].IsDisposed && !Contains(notes.Active, added[0]),
            "the oldest notification leaves when too many are shown");

        int taken = players.PickUp(small.OwnerId, "sand", 300);
        Check(taken > 0 && taken < 300 && smallNotes.Active.Count == 1 && smallNotes.Active[0].Amount == taken,
            "a partial pickup announces only what fitted");
    }

    private static bool Contains(IReadOnlyList<ItemNotificationViewModel> entries, ItemNotificationViewModel entry)
    {
        foreach (ItemNotificationViewModel candidate in entries)
            if (ReferenceEquals(candidate, entry))
                return true;
        return false;
    }
}
