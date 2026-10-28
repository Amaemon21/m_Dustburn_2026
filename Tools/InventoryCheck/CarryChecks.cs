using System;
using System.Reflection;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyCarryZones()
    {
        CarryZone[] zones = CarryZone.Defaults();
        CarryPenalty light = CarryPenalty.At(zones, 0.2f);
        Check(light.Zone == zones[0] && light.WalkSpeed == 1f && light.StaminaCost == 1f && light.CanSprint, "light load has no penalty");
        CarryPenalty loaded = CarryPenalty.At(zones, 0.75f);
        Check(loaded.Zone == zones[1] && Mathf.Approximately(loaded.StaminaCost, 1.125f), "penalty ramps inside a zone toward the next");
        CarryPenalty overloaded = CarryPenalty.At(zones, 1.375f);
        Check(overloaded.Zone == zones[3] && Mathf.Approximately(overloaded.WalkSpeed, 0.7f) && Mathf.Approximately(overloaded.SprintSpeed, 0.65f)
              && Mathf.Approximately(overloaded.StaminaRegen, 0.875f), "red zone ramps speed and regen");
        CarryPenalty crushed = CarryPenalty.At(zones, 3f);
        Check(crushed.Zone == zones[4] && crushed.WalkSpeed == 0.5f && !crushed.CanSprint && !crushed.CanJump, "last zone holds its values");
        Check(CarryPenalty.At(null, 2f).Zone == null && CarryPenalty.At(null, 2f).WalkSpeed == 1f, "no zones means no penalty");

        ItemCatalog catalog = new();
        catalog.SetWeight("stone", 1f);
        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        using InventoryWeightService weights = new(catalog);
        using PlayerStatSheet stats = new PlayerStatSheet()
            .Add(PlayerStatType.CarryCapacity, 10f)
            .Add(PlayerStatType.StaminaCost, 1f)
            .Add(PlayerStatType.StaminaRegen, 10f);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("carry", new Vector2Int(3, 2), 2);
        IReadOnlyInventoryWeight weight = weights.Register(player.OwnerId, stats.Get(PlayerStatType.CarryCapacity), zones,
            player.Backpack, player.Hotbar, player.Equipment);
        PlayerMovementModifiers movement = new();
        using PlayerEncumbrance encumbrance = new(weight, zones, stats, movement);
        encumbrance.Activate();

        Check(weight.Zone.CurrentValue == zones[0] && weight.Load.CurrentValue == 0f, "empty player is in the first zone");
        inventory.AddItemsToInventory(player.Backpack.OwnerId, "stone", 12);
        Check(Mathf.Approximately(weight.Load.CurrentValue, 1.2f) && weight.Zone.CurrentValue == zones[2], "load is weight over capacity");
        Check(Mathf.Approximately(stats.Get(PlayerStatType.StaminaCost).Value.CurrentValue, 1.45f), "heavy load raises stamina cost");

        inventory.AddItemsToInventory(player.Backpack.OwnerId, "stone", 8);
        PlayerMovementRequest request = PlayerMovementRequest.Idle;
        request.Sprint = true;
        request.Jump = true;
        movement.Modify(ref request);
        Check(!request.Sprint && !request.Jump && Mathf.Approximately(request.SpeedMultiplier, 0.5f), "crushing load stops sprint and jump");
        Check(Mathf.Approximately(stats.Get(PlayerStatType.StaminaRegen).Value.CurrentValue, 7.5f), "crushing load slows stamina regen");

        inventory.RemoveItemsFromInventory(player.Backpack.OwnerId, "stone", 20);
        Check(stats.Get(PlayerStatType.StaminaCost).Value.CurrentValue == 1f && stats.Get(PlayerStatType.StaminaRegen).Value.CurrentValue == 10f,
            "dropping the load removes the penalty");
    }

    private static void VerifyBackpack()
    {
        BackpackInventoryItem pack = FakeAsset<BackpackInventoryItem>();
        SetDeclaredField<InventoryItem>(pack, nameof(InventoryItem.ItemId), "pack");
        SetDeclaredField<InventoryItem>(pack, nameof(InventoryItem.MaxStack), 1);
        SetDeclaredField<InventoryItem>(pack, nameof(InventoryItem.Weight), 2f);
        SetDeclaredField<BackpackInventoryItem>(pack, nameof(BackpackInventoryItem.ExtraRows), 2);
        SetDeclaredField<BackpackInventoryItem>(pack, nameof(BackpackInventoryItem.CarryBonus), 15f);
        ResourceInventoryItem stone = FakeItem("stone");
        SetDeclaredField<InventoryItem>(stone, nameof(InventoryItem.MaxStack), 50);
        InventoryItemDatabase database = FakeAsset<InventoryItemDatabase>();
        typeof(InventoryItemDatabase).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(database, new InventoryItem[] { pack, stone });
        ItemCatalog catalog = new(database);
        Check(catalog.CanEquip("pack", EquipmentSlot.Backpack) && !catalog.CanEquip("pack", EquipmentSlot.Headwear), "backpack equips only on the back");

        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        using Stat capacity = new(20f, 0f);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("pack", new Vector2Int(3, 2), 2);
        using PlayerBackpack backpack = new(inventory, catalog, player, capacity, new Vector2Int(3, 2));
        string owner = player.OwnerId;
        string grid = player.Backpack.OwnerId;
        string equipment = player.Equipment.OwnerId;

        Check(player.Backpack.Size == new Vector2Int(3, 2) && capacity.Value.CurrentValue == 20f, "no backpack keeps the base grid");
        inventory.AddItemsToInventory(grid, "pack");
        Check(players.Equip(owner, new Vector2Int(0, 0), EquipmentSlot.Backpack), "backpack equips");
        Check(player.Backpack.Size == new Vector2Int(3, 4) && capacity.Value.CurrentValue == 35f, "backpack adds rows and carry weight");

        inventory.AddItemsToInventory(grid, "stone", 50 * 6);
        Check(!backpack.HoldsExtraItems(), "pickups fill the base rows first");
        inventory.AddItemsToInventory(grid, "stone", 50);
        Check(backpack.HoldsExtraItems() && !player.Backpack.GetSlot(new Vector2Int(0, 2)).IsEmpty, "overflow goes into the backpack rows");

        Check(!players.Unequip(owner, EquipmentSlot.Backpack, new Vector2Int(1, 3)), "full backpack cannot be moved off");
        Check(!inventory.SwapSlots(equipment, PlayerBackpack.SLOT, grid, new Vector2Int(0, 2)), "full backpack cannot be swapped off");
        Check(!inventory.RemoveItemsFromInventory(equipment, PlayerBackpack.SLOT, "pack").Success, "full backpack cannot be dropped");

        inventory.RemoveItemsFromInventory(grid, new Vector2Int(0, 2), "stone", 50);
        Check(!players.Unequip(owner, EquipmentSlot.Backpack, new Vector2Int(1, 3)), "a backpack never lands in its own rows");
        inventory.RemoveItemsFromInventory(grid, new Vector2Int(0, 0), "stone", 50);
        Check(players.Unequip(owner, EquipmentSlot.Backpack, new Vector2Int(0, 0)), "empty backpack comes off");
        Check(player.Backpack.Size == new Vector2Int(3, 2) && capacity.Value.CurrentValue == 20f, "taking it off restores the base grid");
    }

    private static void VerifyDragSwing()
    {
        DragSwing swing = new(new DragSwingSettings());
        swing.Grab();
        float peak = 1f;
        for (int frame = 0; frame < 30; frame++)
        {
            swing.Tick(Vector2.zero, 1f / 60f);
            peak = Mathf.Max(peak, swing.Scale);
        }
        Check(peak > 1.12f, "grabbing pops the item past its held scale");
        for (int frame = 0; frame < 240; frame++)
            swing.Tick(Vector2.zero, 1f / 60f);
        Check(swing.IsResting && Mathf.Abs(swing.Angle) < 0.01f, "a still cursor lets the item come to rest");

        Vector2 cursor = Vector2.zero;
        for (int frame = 0; frame < 20; frame++)
        {
            cursor.x += 15f;
            swing.Tick(cursor, 1f / 60f);
        }
        Check(swing.Angle < -2f && !swing.IsResting, "moving right swings the item back like a hanging joint");
        float swingBack = 0f;
        for (int frame = 0; frame < 60; frame++)
        {
            swing.Tick(cursor, 1f / 60f);
            swingBack = Mathf.Max(swingBack, swing.Angle);
        }
        Check(swingBack > 0f, "stopping overshoots past vertical");
        for (int frame = 0; frame < 480; frame++)
            swing.Tick(cursor, 1f / 60f);
        Check(swing.IsResting, "the swing settles once the cursor stops");
    }
}
