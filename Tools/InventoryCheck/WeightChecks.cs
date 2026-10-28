using System;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyStatModifiers()
    {
        object backpack = new();
        object wounded = new();
        using Stat stat = new(40f, 0f);
        float observed = 0f;
        using IDisposable subscription = stat.Value.Subscribe(value => observed = value);

        stat.AddModifier(new StatModifier(StatModifierType.Flat, 10f, backpack));
        Check(observed == 50f, "flat modifier adds to base");
        stat.AddModifier(new StatModifier(StatModifierType.Percent, 0.2f, backpack));
        stat.AddModifier(new StatModifier(StatModifierType.Percent, 0.3f, backpack));
        Check(Mathf.Approximately(observed, 75f), "percent modifiers sum before scaling");
        stat.AddModifier(new StatModifier(StatModifierType.Multiplier, 0.5f, wounded));
        Check(Mathf.Approximately(observed, 37.5f), "multiplier scales the result");
        Check(stat.RemoveModifiersFrom(backpack) == 3 && Mathf.Approximately(observed, 20f), "removing a source drops all its modifiers");
        Check(stat.RemoveModifier(new StatModifier(StatModifierType.Multiplier, 0.5f, wounded)) && observed == 40f,
            "removing one modifier restores base");
        stat.AddModifier(new StatModifier(StatModifierType.Flat, -100f, wounded));
        Check(observed == 0f, "stat is clamped to its minimum");
        stat.RemoveModifiersFrom(wounded);
        stat.BaseValue = 60f;
        Check(observed == 60f, "base value change recalculates");
        Expect<ArgumentNullException>(() => new StatModifier(StatModifierType.Flat, 1f, null), "modifier requires a source");
    }

    private static void VerifyInventoryWeight()
    {
        ItemCatalog catalog = new();
        catalog.SetWeight("stone", 1.5f);
        catalog.SetWeight("wood", 0.1f);
        Expect<ArgumentOutOfRangeException>(() => catalog.SetWeight("bad", -1f), "negative item weight rejected");

        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        using InventoryWeightService weights = new(catalog);
        using Stat capacity = new(10f, 0f);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("weight", new Vector2Int(3, 2), 3);
        IReadOnlyInventoryWeight weight = weights.Register(player.OwnerId, capacity, player.Backpack, player.Hotbar, player.Equipment);

        Check(weight.HasLimit && weight.Current.CurrentValue == 0f && !weight.IsOverloaded.CurrentValue, "empty inventory weighs nothing");
        inventory.AddItemsToInventory(player.Backpack.OwnerId, "stone", 4);
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, "wood", 10);
        Check(Mathf.Approximately(weight.Current.CurrentValue, 7f), "weight sums every registered grid");
        inventory.AddItemsToInventory(player.Backpack.OwnerId, "stone", 4);
        Check(weight.IsOverloaded.CurrentValue, "weight past capacity overloads");

        object strength = new();
        capacity.AddModifier(new StatModifier(StatModifierType.Flat, 5f, strength));
        Check(!weight.IsOverloaded.CurrentValue, "capacity buff lifts the overload");
        capacity.RemoveModifiersFrom(strength);
        Check(weight.IsOverloaded.CurrentValue, "removing the buff restores the overload");

        inventory.MoveItems(player.Backpack.OwnerId, new Vector2Int(0, 0), player.Hotbar.OwnerId, new Vector2Int(2, 0), 4);
        Check(Mathf.Approximately(weight.Current.CurrentValue, 13f), "moves between registered grids keep the weight");

        Check(weights.TryGet(player.OwnerId, out IReadOnlyInventoryWeight found) && ReferenceEquals(found, weight), "weight found by owner");
        Check(!weights.TryGet("nobody", out _), "unknown owner has no weight");
        Expect<InvalidOperationException>(() => weights.Register(player.OwnerId, capacity, player.Backpack), "weight registered once per owner");

        using InventoryWeightViewModel unlimited = InventoryWeightViewModel.ForGrid(catalog, player.Backpack);
        Check(!unlimited.HasLimit && Mathf.Approximately(unlimited.Current.CurrentValue, 6f) && !unlimited.IsOverloaded.CurrentValue,
            "grid weight without a limit never overloads");
    }

    private static void VerifyHotbarWeighsNothing()
    {
        ItemCatalog catalog = new();
        catalog.SetWeight("stone", 1f);
        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        using InventoryWeightService weights = new(catalog);
        using PlayerStatSheet stats = new PlayerStatSheet().Add(PlayerStatType.CarryCapacity, 10f);
        PlayerInventorySettings settings = FakeAsset<PlayerInventorySettings>();
        SetDeclaredField<InventorySetting>(settings, nameof(InventorySetting.OwnerId), "hotbar-weight");
        SetDeclaredField<InventorySetting>(settings, nameof(InventorySetting.Size), new Vector2Int(3, 2));
        SetDeclaredField<PlayerInventorySettings>(settings, nameof(PlayerInventorySettings.HotbarSize), 3);
        SetDeclaredField<PlayerInventorySettings>(settings, nameof(PlayerInventorySettings.CarryZones), CarryZone.Defaults());
        using LocalPlayerInventory local = new(players, inventory, weights, catalog, stats, settings);
        PlayerInventoryProxy player = local.Proxy;

        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 0), "stone", 4);
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(0, 0), "stone", 6);
        Check(Mathf.Approximately(local.Weight.Current.CurrentValue, 4f), "the player's carried weight leaves the hotbar out");
        inventory.MoveItems(player.Backpack.OwnerId, new Vector2Int(0, 0), player.Hotbar.OwnerId, new Vector2Int(1, 0), 4);
        Check(local.Weight.Current.CurrentValue == 0f, "moving a stack onto the hotbar takes its weight off");
        inventory.MoveItems(player.Hotbar.OwnerId, new Vector2Int(0, 0), player.Backpack.OwnerId, new Vector2Int(2, 0), 6);
        Check(Mathf.Approximately(local.Weight.Current.CurrentValue, 6f), "moving a stack off the hotbar puts its weight back");
    }
}
