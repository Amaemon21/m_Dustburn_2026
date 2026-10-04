using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using R3;
using UnityEngine;
using VContainer;

internal static partial class Harness
{
    private const string INVENTORY_PATH = "slot_0/inventory.json";
    private static readonly string FutureVersionJson = $"{{\"version\":{SaveDocument.CURRENT_VERSION + 1},\"sections\":{{}}}}";

    private static NewtonsoftSaveSerializer ProjectSerializer()
        => new(new SaveMigrationService(new ISaveMigration[] { new InventoryEquipmentLayoutMigration() }));

    private static SaveRegistry InventoryRegistry()
        => new SaveRegistry().Register<InventoryData>("inventory", SaveScope.Slot, SaveInstaller.INVENTORY_FILE);

    private static void VerifyPickupStackPriority()
    {
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, new ItemCatalog());
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("pickup", new Vector2Int(3, 1), 2);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 0), "wood", 5);
        Check(players.PickUp(player.OwnerId, "wood", 10) == 10 &&
            player.Backpack.GetSlots()[0, 0].State.CurrentValue.Amount == 15 &&
            player.Backpack.GetSlots()[1, 0].State.CurrentValue.IsEmpty,
            "pickup merges backpack stacks before occupying empty slots");
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(0, 0), "wood", 99);
        Check(players.PickUp(player.OwnerId, "wood", 80) == 80 &&
            player.Backpack.GetSlots()[0, 0].State.CurrentValue.Amount == 95 &&
            player.Hotbar.GetSlots()[1, 0].State.CurrentValue.IsEmpty,
            "full hotbar stack does not create a new stack before filling existing backpack stack");
        Check(players.PickUp(player.OwnerId, "wood", 10) == 10 &&
            player.Backpack.GetSlots()[0, 0].State.CurrentValue.Amount == 99 &&
            player.Hotbar.GetSlots()[1, 0].State.CurrentValue.Amount == 6,
            "pickup fills all existing stacks before using preferred hotbar empty slots");
        Check(player.Backpack.GetAmount("wood") + player.Hotbar.GetAmount("wood") == 204,
            "pickup stack allocation preserves the full item amount");
    }

    private static void VerifyInventoryShortcuts()
    {
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        ItemCatalog catalog = new();
        catalog.RegisterEquipment("helmet", EquipmentSlot.Headwear);
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("shortcuts", new Vector2Int(5, 1), 2);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 0), "helmet", 2);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(1, 0), "wood", 20);
        using InventoryTabViewModel tab = new(inventory, player, players, catalog);
        using HudHotbarViewModel hud = new(player, players, catalog);
        tab.Backpack.Slots.CurrentValue[0].QuickTransfer.Execute(Unit.Default);
        Check(player.Equipment.GetAmount("helmet") == 1 && player.Backpack.GetAmount("helmet") == 1,
            "quick transfer prioritizes equipment and equips only one item");
        tab.Backpack.Slots.CurrentValue[1].QuickTransfer.Execute(Unit.Default);
        Check(player.Hotbar.GetAmount("wood") == 20 && player.Backpack.GetAmount("wood") == 0,
            "quick transfer moves ordinary backpack items to hotbar");
        tab.Hotbar.Grid.Slots.CurrentValue[0].QuickTransfer.Execute(Unit.Default);
        Check(player.Hotbar.GetAmount("wood") == 0 && player.Backpack.GetAmount("wood") == 20,
            "quick transfer returns ordinary hotbar items to backpack");
        Check(players.QuickTransfer(player.OwnerId, player.Equipment.OwnerId,
            new Vector2Int((int)EquipmentSlot.Headwear, 0)) == 1 && player.Equipment.GetAmount("helmet") == 0,
            "quick transfer unequips equipment into backpack");
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(0, 0), "helmet", 1);
        Check(players.QuickTransfer(player.OwnerId, player.Hotbar.OwnerId, new Vector2Int(0, 0)) == 1 &&
            player.Equipment.GetAmount("helmet") == 1, "quick transfer equips directly from hotbar");
        Check(players.QuickTransfer(player.OwnerId, player.Hotbar.OwnerId, new Vector2Int(1, 0)) == 0,
            "quick transfer of empty slots changes nothing");

        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(0, 0), "wood", 90);
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(1, 0), "stone", 99);
        Check(players.PickUp(player.OwnerId, "wood", 20) == 20 && player.Hotbar.GetAmount("wood") == 99 &&
            player.Backpack.GetAmount("wood") == 31, "pickup fills existing hotbar stacks before backpack overflow");
        Check(players.PickUp(player.OwnerId, "iron", 3) == 3 && player.Hotbar.GetAmount("iron") == 0,
            "pickup of new items uses backpack");
        players.SelectHotbarSlot(player.OwnerId, 0);
        players.CycleHotbarSlot(player.OwnerId, -1);
        Check(player.SelectedHotbarSlot.CurrentValue == 1 && hud.Slots[1].IsSelected.CurrentValue &&
            !hud.Slots[0].IsSelected.CurrentValue, "wheel selection wraps backwards and updates HUD");
        players.CycleHotbarSlot(player.OwnerId, 1);
        Check(player.SelectedHotbarSlot.CurrentValue == 0 && hud.Slots[0].IsSelected.CurrentValue,
            "wheel selection wraps forwards and updates HUD");

        InventorySlotViewModel wood = tab.Backpack.Slots.CurrentValue[1];
        InventorySlotViewModel iron = tab.Backpack.Slots.CurrentValue[2];
        InventorySlotViewModel empty = tab.Backpack.Slots.CurrentValue[4];
        Check(wood.State.CurrentValue.ItemId == "wood" && iron.State.CurrentValue.ItemId == "iron",
            "partial drag setup has expected inventory items");
        tab.DragDrop.Begin(wood, default, true);
        Check(tab.DragDrop.State.CurrentValue.Amount == 16 && wood.State.CurrentValue.Amount == 31,
            "shift drag takes rounded-up half of odd stack without removing source items");
        Check(wood.DisplayAmount == 15, "partial drag keeps remaining amount visible in source slot");
        tab.DragDrop.Cancel();
        tab.DragDrop.Begin(iron, default, true);
        Check(tab.DragDrop.State.CurrentValue.Amount == 2, "shift drag rounds half of three items up to two");
        tab.DragDrop.Cancel();
        tab.DragDrop.Begin(wood, default);
        tab.DragDrop.SetAmount(5);
        Check(tab.DragDrop.Drop(empty) && empty.State.CurrentValue.Amount == 5 && wood.State.CurrentValue.Amount == 26,
            "partial drag transfers only chosen amount");
        tab.DragDrop.Begin(wood, default, true);
        Check(tab.DragDrop.State.CurrentValue.Amount == 13, "shift drag takes exact half of even stack");
        tab.DragDrop.Cancel();
        tab.DragDrop.Begin(wood, default);
        tab.DragDrop.SetAmount(0);
        Check(tab.DragDrop.State.CurrentValue.Amount == 1, "drag quantity cannot fall below one");
        tab.DragDrop.ChangeAmount(1000);
        Check(tab.DragDrop.State.CurrentValue.Amount == 26, "drag quantity cannot exceed source stack");
        Check(wood.DisplayAmount == 0, "whole stack drag hides source contents");
        tab.DragDrop.SetAmount(2);
        Check(wood.DisplayAmount == 24, "changing drag quantity updates source remainder display");
        Check(!tab.DragDrop.Drop(iron) && iron.State.CurrentValue.ItemId == "iron" && wood.State.CurrentValue.Amount == 26,
            "partial drag cannot swap entire stacks of different items");
        Check(players.QuickTransfer(player.OwnerId, player.Backpack.OwnerId, new Vector2Int(1, 0)) == 0 &&
            wood.State.CurrentValue.Amount == 26, "full hotbar rejects quick transfer without losing items");
    }

    private static void VerifyInventoryDragDrop()
    {
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        ItemCatalog catalog = new();
        catalog.RegisterEquipment("helmet", EquipmentSlot.Headwear);
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, catalog);
        using PlayerInventoryService players = new(repository, inventory);
        PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("drag", new Vector2Int(5, 1), 3);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 0), "wood", 20);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(1, 0), "helmet", 2);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(2, 0), "stone", 1);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(3, 0), "iron", 5);
        inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(0, 0), "wood", 90);
        using InventoryTabViewModel tab = new(inventory, player, players, catalog);
        InventoryDragDropViewModel drag = tab.DragDrop;
        InventorySlotViewModel wood = tab.Backpack.Slots.CurrentValue[0];
        InventorySlotViewModel helmet = tab.Backpack.Slots.CurrentValue[1];
        InventorySlotViewModel head = tab.Equipment.Slots.CurrentValue[(int)EquipmentSlot.Headwear];
        drag.Begin(tab.Backpack.Slots.CurrentValue[2], default, true);
        Check(drag.State.CurrentValue.Amount == 1, "shift drag keeps a single item draggable");
        drag.Cancel();
        Check(!drag.Begin(tab.Backpack.Slots.CurrentValue[4], default), "empty slots cannot start dragging");
        Check(drag.Begin(wood, new Vector2(10, 20)) && drag.State.CurrentValue.Amount == 20,
            "drag captures source stack without removing it");
        Check(wood.IsDragging.CurrentValue && !helmet.IsDragging.CurrentValue,
            "only source slot switches to hidden drag presentation");
        drag.Move(new Vector2(30, 40));
        Check(drag.Position.CurrentValue == new Vector2(30, 40) && player.Backpack.GetAmount("wood") == 20,
            "drag position changes without inventory mutation");
        Check(drag.Drop(tab.Hotbar.Grid.Slots.CurrentValue[0]) && wood.State.CurrentValue.Amount == 11 &&
            tab.Hotbar.Grid.Slots.CurrentValue[0].State.CurrentValue.Amount == 99 && !drag.IsDragging,
            "drop merges up to stack capacity and leaves remainder in source");
        Check(!wood.IsDragging.CurrentValue, "partial drop restores source slot presentation");
        drag.Begin(wood, default);
        Check(!drag.Drop(wood) && wood.State.CurrentValue.Amount == 11, "drop on source slot changes nothing");
        drag.Begin(wood, default);
        Check(!drag.Drop(head) && wood.State.CurrentValue.Amount == 11 && head.State.CurrentValue.IsEmpty,
            "invalid equipment drop preserves source and target");
        Check(!wood.IsDragging.CurrentValue, "rejected drop restores source slot presentation");
        drag.Begin(helmet, default);
        Check(drag.Drop(head) && helmet.State.CurrentValue.Amount == 1 && head.State.CurrentValue.Amount == 1,
            "equipment drop transfers one item from a stack");
        drag.Begin(helmet, default);
        Check(!drag.Drop(head) && helmet.State.CurrentValue.Amount == 1, "full equipment slot rejects stacking");
        drag.Begin(head, default);
        Check(!drag.Drop(tab.Backpack.Slots.CurrentValue[2]) && head.State.CurrentValue.ItemId == "helmet",
            "swap validates both equipment destinations");
        drag.Begin(wood, default);
        Check(drag.Drop(tab.Backpack.Slots.CurrentValue[2]) && wood.State.CurrentValue.ItemId == "stone" &&
            tab.Backpack.Slots.CurrentValue[2].State.CurrentValue.ItemId == "wood", "different items swap on drop");
        drag.Begin(head, default);
        Check(drag.Drop(tab.Backpack.Slots.CurrentValue[4]) && head.State.CurrentValue.IsEmpty,
            "equipment can be dragged back to backpack");
        drag.Begin(helmet, default);
        drag.Cancel();
        Check(!drag.IsDragging && helmet.State.CurrentValue.Amount == 1, "cancel drag leaves items in place");
        Check(!helmet.IsDragging.CurrentValue, "cancel restores source slot presentation");
        Check(helmet.DisplayAmount == helmet.State.CurrentValue.Amount, "cancel restores full displayed amount");
        drag.Begin(helmet, default);
        inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(1, 0), "helmet", 1);
        Check(!drag.IsDragging, "source mutation cancels stale drag");
        drag.Begin(helmet, default);
        inventory.SetSize(player.Backpack.OwnerId, new Vector2Int(6, 1));
        Check(!drag.IsDragging && helmet.IsDisposed, "grid rebuild cancels drag before disposing slots");
    }

    private static void VerifySelectedItemInfo()
    {
        SaveService saves = new(InventoryRegistry(), new MemoryStorage(), ProjectSerializer());
        using InventoryRepository repository = new(saves);
        using InventoryService inventory = new(repository, new ItemCatalog());
        inventory.RegisterInventory(new InventoryGridData { OwnerId = "info", Size = new Vector2Int(2, 1) });
        inventory.AddItemsToInventory("info", new Vector2Int(0, 0), "wood", 3);
        inventory.AddItemsToInventory("info", new Vector2Int(1, 0), "stone", 2);
        using InventoryGridViewModel grid = new(inventory, "info", new ItemCatalog());
        using InventoryItemInfoViewModel info = new(new ItemCatalog(), grid);
        Check(!info.HasItem, "item info starts without a selection");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        Check(info.HasItem && info.State.CurrentValue.ItemId == "wood" &&
            grid.Slots.CurrentValue[0].IsSelected.CurrentValue, "item info follows selected slot and retains selected frame");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        Check(!info.HasItem && !grid.SelectedSlot.CurrentValue.HasValue,
            "repeated item click clears info and selection frame");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        inventory.RemoveItemsFromInventory("info", new Vector2Int(0, 0), "wood", 1);
        Check(info.State.CurrentValue.Amount == 2, "item info tracks selected stack changes");
        grid.Slots.CurrentValue[1].Select.Execute(Unit.Default);
        inventory.AddItemsToInventory("info", new Vector2Int(0, 0), "wood", 1);
        Check(info.State.CurrentValue.ItemId == "stone" && info.State.CurrentValue.Amount == 2,
            "item info unsubscribes from previous slot");
        inventory.RemoveItemsFromInventory("info", new Vector2Int(1, 0), "stone", 2);
        Check(!info.HasItem, "item info clears when selected slot becomes empty");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        inventory.SetSize("info", new Vector2Int(3, 1));
        Check(!info.HasItem, "item info clears when slot view models are rebuilt");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        grid.Slots.CurrentValue[2].Select.Execute(Unit.Default);
        Check(!info.HasItem && !grid.SelectedSlot.CurrentValue.HasValue,
            "empty slot click clears item info and selection");
        inventory.RegisterInventory(new InventoryGridData { OwnerId = "empty-info", Size = new Vector2Int(1, 1) });
        using InventoryGridViewModel emptyGrid = new(inventory, "empty-info", new ItemCatalog());
        using InventoryItemInfoViewModel combinedInfo = new(new ItemCatalog(), grid, emptyGrid);
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        emptyGrid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        Check(!combinedInfo.HasItem && !grid.SelectedSlot.CurrentValue.HasValue &&
            !grid.Slots.CurrentValue[0].IsSelected.CurrentValue,
            "empty slot in another grid cancels selected item and frame");
        grid.Slots.CurrentValue[0].Select.Execute(Unit.Default);
        info.Clear();
        Check(!info.HasItem && !grid.Slots.CurrentValue[0].IsSelected.CurrentValue,
            "background cancellation clears selected frame");
    }

    private static async Task VerifyLegacyEquipmentLayout()
    {
        InventoryData data = new();
        data.Players.Add(new PlayerInventoryData
        {
            OwnerId = "legacy", BackpackId = "legacy", HotbarId = "legacy/hotbar", EquipmentId = "legacy/equipment"
        });
        InventoryGridData backpack = new() { OwnerId = "legacy", Size = new Vector2Int(1, 2) };
        backpack.Slots.Add(new InventorySlotData { ItemId = "wood", Amount = 99 });
        backpack.Slots.Add(new InventorySlotData { ItemId = "stone", Amount = 50 });
        InventoryGridData equipment = new()
        {
            OwnerId = "legacy/equipment", Size = new Vector2Int(10, 1), Kind = InventoryGridKind.Equipment
        };
        for (int index = 0; index < 10; index++)
            equipment.Slots.Add(new InventorySlotData());
        equipment.Slots[1] = new InventorySlotData { ItemId = "helmet", Amount = 1 };
        equipment.Slots[9] = new InventorySlotData { ItemId = "boots", Amount = 1 };
        data.Grids.Add(backpack);
        data.Grids.Add(equipment);
        data.Grids.Add(new InventoryGridData
        {
            OwnerId = "legacy/hotbar", Size = new Vector2Int(1, 1), Kind = InventoryGridKind.Hotbar
        });
        SaveDocument legacy = new() { Version = 1 };
        legacy.Sections[SaveInstaller.INVENTORY_KEY] = data;

        MemoryStorage storage = new();
        storage.Files[INVENTORY_PATH] = ProjectSerializer().Serialize(legacy);
        SaveMigrationService migrations = new(new ISaveMigration[] { new InventoryEquipmentLayoutMigration() });
        SaveService saves = new(InventoryRegistry(), storage, new NewtonsoftSaveSerializer(migrations));
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);

        using (InventoryRepository repository = new(saves))
        using (InventoryService inventory = new(repository, new ItemCatalog()))
        using (PlayerInventoryService players = new(repository, inventory))
        {
            Check(players.GetPlayerInventory("legacy").Equipment.Size == new Vector2Int(9, 1),
                "legacy equipment creates current player layout");
            Check(inventory.GetInventory("legacy").Size == new Vector2Int(2, 2),
                "full backpack grows for legacy equipment");
            Check(inventory.Has("legacy", "helmet") && inventory.Has("legacy", "boots"),
                "legacy equipment including removed last slot preserved");
            Check(inventory.GetInventory("legacy").GetSlot(new Vector2Int(0, 1)).Amount == 50,
                "migration preserves existing backpack coordinates");
            Check(saves.HasDirtyFiles, "equipment migration scheduled for saving");
            await repository.SaveAsync(CancellationToken.None);
        }
        Check(storage.Files[INVENTORY_PATH].Contains($"\"version\": {SaveDocument.CURRENT_VERSION}"),
            "migrated file is written at the current version");

        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using (InventoryRepository repository = new(saves))
        using (InventoryService inventory = new(repository, new ItemCatalog()))
        {
            Check(inventory.Has("legacy", "helmet") && inventory.Has("legacy", "boots"),
                "migrated equipment survives save reload");
            Check(!saves.HasDirtyFiles, "equipment migration runs only once");
        }

        JObject current = JObject.Parse(ProjectSerializer().Serialize(new SaveDocument()));
        new InventoryEquipmentLayoutMigration().Migrate(current);
        Check(current[NewtonsoftSaveSerializer.SECTIONS_FIELD]?[SaveInstaller.INVENTORY_KEY] == null,
            "equipment migration leaves a file without inventory untouched");
    }

    private static void VerifyItemStackLimits()
    {
        ItemCatalog catalog = new();
        catalog.SetMaxStack("arrow", 300);
        catalog.SetMaxStack("rifle", 1);
        catalog.RegisterEquipment("helmet", EquipmentSlot.Headwear);
        using InventoryGrid backpack = new(new InventoryGridData { OwnerId = "stacks", Size = new Vector2Int(4, 1) }, catalog);
        Check(backpack.AddItems("arrow", 450).ItemsAddedAmount == 450 &&
            backpack.GetSlot(new Vector2Int(0, 0)).Amount == 300 && backpack.GetSlot(new Vector2Int(1, 0)).Amount == 150,
            "item max stack overrides the default stack");
        Check(backpack.AddItems("rifle", 3).ItemsAddedAmount == 2 &&
            backpack.GetSlot(new Vector2Int(2, 0)).Amount == 1 && backpack.GetSlot(new Vector2Int(3, 0)).Amount == 1,
            "single-stack items take one slot each");
        Check(backpack.CapacityFor("arrow") == 300 && backpack.CapacityFor("unknown") == ItemCatalog.DEFAULT_MAX_STACK,
            "unknown items keep the default stack");
        Check(!backpack.MoveTo(new Vector2Int(2, 0), backpack, new Vector2Int(3, 0), 1),
            "moving onto a full single-stack slot is refused");
        InventoryGridData equipmentData = new()
        {
            OwnerId = "stacks/equipment", Size = new Vector2Int(Enum.GetValues(typeof(EquipmentSlot)).Length, 1), Kind = InventoryGridKind.Equipment
        };
        using InventoryGrid equipment = new(equipmentData, catalog);
        Check(equipment.CapacityFor("arrow") == 1, "equipment slots hold one item whatever the stack");
        Expect<ArgumentOutOfRangeException>(() => catalog.SetMaxStack("arrow", InventorySlotProxy.MAX_AMOUNT + 1),
            "max stack above the slot ceiling rejected");
        Expect<ArgumentNullException>(() => new InventoryGrid(new InventoryGridData { OwnerId = "nocatalog", Size = Vector2Int.one }, null),
            "grid refuses a missing catalog");
    }

    private static string InventoryJson(string owner)
    {
        SaveDocument document = new();
        InventoryData data = new();
        data.Grids.Add(new InventoryGridData { OwnerId = owner, Size = new Vector2Int(1, 1) });
        document.Sections["inventory"] = data;
        return ProjectSerializer().Serialize(document);
    }

    private static async Task VerifySaveRecovery()
    {
        MemoryStorage storage = new();
        storage.Files[INVENTORY_PATH] = "{broken";
        storage.Backups[INVENTORY_PATH] = InventoryJson("backup");
        SaveService saves = new(InventoryRegistry(), storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        Check(saves.Get<InventoryData>().Grids[0].OwnerId == "backup", "corrupt JSON restores backup");
        Check(storage.Quarantined[INVENTORY_PATH] == "{broken", "corrupt primary preserved separately");
        Check(saves.HasDirtyFiles, "recovered save scheduled for rewrite");
        saves.MarkDirty<InventoryData>();
        await saves.SaveDirtyAsync(CancellationToken.None);
        Check(storage.Backups[INVENTORY_PATH] == InventoryJson("backup"), "recovery save preserves valid backup");

        storage.Files[INVENTORY_PATH] = "{}";
        await saves.LoadFileAsync("inventory", CancellationToken.None);
        Check(saves.Get<InventoryData>().Grids[0].OwnerId == "backup", "invalid envelope restores backup");
        storage.Files[INVENTORY_PATH] = "";
        await saves.LoadFileAsync("inventory", CancellationToken.None);
        Check(saves.Get<InventoryData>().Grids[0].OwnerId == "backup", "empty primary restores backup");
        storage.Files[INVENTORY_PATH] = "{broken";
        storage.Backups[INVENTORY_PATH] = "{broken backup";
        await saves.LoadFileAsync("inventory", CancellationToken.None);
        Check(saves.Get<InventoryData>().Grids.Count == 0, "defaults used when both files corrupt");
        Check(storage.Quarantined[INVENTORY_PATH + ".bak"] == "{broken backup", "damaged backup also preserved separately");

        storage.Files[INVENTORY_PATH] = FutureVersionJson;
        InventoryData previous = saves.Get<InventoryData>();
        await ExpectAsync<UnsupportedSaveVersionException>(
            () => saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None), "future save version rejected");
        Check(ReferenceEquals(previous, saves.Get<InventoryData>()) && storage.Files.ContainsKey(INVENTORY_PATH),
            "unsupported version preserves live data and source file");

        saves.MarkDirty<InventoryData>();
        await ExpectAsync<InvalidOperationException>(() => saves.SaveDirtyAsync(CancellationToken.None),
            "autosave cannot overwrite unsupported version");
        Check(storage.Files[INVENTORY_PATH].Contains(FutureVersionJson), "unsupported file remains intact after save attempt");

        VContainer.ContainerBuilder builder = new();
        builder.Register<SaveMigrationService>(VContainer.Lifetime.Singleton);
        using (VContainer.IObjectResolver container = builder.Build())
            Check(container.Resolve<SaveMigrationService>() != null, "DI supplies empty migration collection");

        SaveMigrationService migrations = new(new ISaveMigration[] { new TestMigration() });
        NewtonsoftSaveSerializer serializer = new(migrations);
        SaveDocument migrated = serializer.Deserialize($"{{\"version\":{SaveDocument.CURRENT_VERSION - 1},\"sections\":{{}}}}", InventoryRegistry().GetFile("inventory"));
        Check(migrated.Version == SaveDocument.CURRENT_VERSION && ((InventoryData)migrated.Sections["inventory"]).Grids[0].OwnerId == "migrated",
            "registered migration runs before typed deserialization");
    }

    private static async Task VerifySaveSessions()
    {
        MemoryStorage storage = new();
        SaveService saves = new(InventoryRegistry(), storage, ProjectSerializer());
        UniTaskCompletionSource<string> pending = new();
        storage.PendingReads[INVENTORY_PATH] = pending;
        UniTask load = saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        saves.SetActiveSlot("slot_1");
        InventoryData next = saves.Get<InventoryData>();
        pending.TrySetResult(InventoryJson("old"));
        await load;
        Check(ReferenceEquals(next, saves.Get<InventoryData>()) && next.Grids.Count == 0,
            "old slot completion cannot overwrite current slot");

        saves.SetActiveSlot("slot_0");
        pending = new();
        storage.PendingReads[INVENTORY_PATH] = pending;
        load = saves.LoadFileAsync("inventory", CancellationToken.None);
        saves.ResetScope(SaveScope.Slot);
        next = saves.Get<InventoryData>();
        pending.TrySetResult(InventoryJson("old"));
        await load;
        Check(ReferenceEquals(next, saves.Get<InventoryData>()), "reset invalidates pending file load");

        pending = new();
        storage.PendingReads[INVENTORY_PATH] = pending;
        load = saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        next.Grids.Add(new InventoryGridData { OwnerId = "live", Size = new Vector2Int(1, 1) });
        saves.MarkDirty<InventoryData>();
        pending.TrySetResult(InventoryJson("old"));
        await load;
        Check(ReferenceEquals(next, saves.Get<InventoryData>()) && saves.HasDirtyFiles, "load preserves edits made during read");

        pending = new();
        storage.PendingReads[INVENTORY_PATH] = pending;
        using CancellationTokenSource cancellation = new();
        load = saves.LoadScopeAsync(SaveScope.Slot, cancellation.Token);
        cancellation.Cancel();
        pending.TrySetResult(InventoryJson("old"));
        await ExpectAsync<OperationCanceledException>(() => load, "cancelled read cannot apply loaded data");
        Check(ReferenceEquals(next, saves.Get<InventoryData>()), "cancellation preserves current state");
        storage.PendingReads.Clear();
        storage.Files[INVENTORY_PATH] = InventoryJson("latest");
        pending = new();
        storage.PendingReads[INVENTORY_PATH] = pending;
        UniTask firstLoad = saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        UniTask secondLoad = saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        storage.PendingReads.Clear();
        pending.TrySetResult(InventoryJson("obsolete"));
        await firstLoad;
        await secondLoad;
        Check(saves.Get<InventoryData>().Grids[0].OwnerId == "latest", "newer scope load supersedes older request");

        SaveRegistry batchRegistry = new SaveRegistry()
            .Register<InventoryData>("inventory", SaveScope.Slot, "inventory")
            .Register<ProgressData>("progress", SaveScope.Slot, "progress");
        SaveService batch = new(batchRegistry, storage, ProjectSerializer());
        InventoryData batchOrigin = batch.Get<InventoryData>();
        batch.MarkDirty<InventoryData>();
        batch.MarkDirty<ProgressData>();
        storage.OnWrite = () => batch.SetActiveSlot("slot_1");
        await ExpectAsync<InvalidOperationException>(() => batch.SaveDirtyAsync(CancellationToken.None),
            "slot change stops remaining files in save batch");
        Check(!storage.Files.ContainsKey("slot_1/progress.json"), "old save batch cannot write defaults into next slot");
        batch.SetActiveSlot("slot_0");
        batchOrigin = batch.Get<InventoryData>();
        storage.Files[INVENTORY_PATH] = InventoryJson("replacement");
        storage.Files["slot_0/progress.json"] = FutureVersionJson;
        await ExpectAsync<UnsupportedSaveVersionException>(() => batch.LoadScopeAsync(SaveScope.Slot, CancellationToken.None),
            "one unsupported file rejects whole scope load");
        Check(ReferenceEquals(batchOrigin, batch.Get<InventoryData>()), "failed scope load cannot partially replace sections");
        Expect<ArgumentException>(() => saves.SetActiveSlot(".."), "parent directory rejected as slot");
    }

    private static Task VerifyFileStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), "dustborn-save-check-" + Guid.NewGuid().ToString("N"));
        FileSaveStorage storage = new(root);
        try
        {
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            string fullPath = Path.Combine(root, INVENTORY_PATH);
            MethodInfo write = typeof(FileSaveStorage).GetMethod("Write", flags);
            write.Invoke(null, new object[] { fullPath, "first" });
            write.Invoke(null, new object[] { fullPath, "second" });
            Check((string)typeof(FileSaveStorage).GetMethod("Read", flags).Invoke(null, new object[] { fullPath + ".bak" }) == "first",
                "file backup retains previous save");
            typeof(FileSaveStorage).GetMethod("Quarantine", flags).Invoke(null, new object[] { fullPath });
            Check(!File.Exists(Path.Combine(root, INVENTORY_PATH)) &&
                Directory.GetFiles(Path.Combine(root, "slot_0"), "*.corrupt.*").Length == 1, "file quarantine preserves damaged source");
            Expect<ArgumentException>(() => storage.DeleteDirectoryAsync("..", CancellationToken.None), "storage prevents escaping save root");
            Expect<ArgumentException>(() => storage.DeleteDirectoryAsync(".", CancellationToken.None), "storage prevents deleting save root");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    }

    private static void VerifyWindowTransactions()
    {
        using WindowService windows = new();
        using TestWindow missing = new();
        Expect<InvalidOperationException>(() => windows.Open("missing", missing), "window without view cannot open");
        Check(!windows.HasOpenWindows.CurrentValue && !missing.IsOpen.CurrentValue, "failed open leaves gameplay unblocked");
        TestWindowPresenter presenter = new();
        windows.Register("window", presenter);
        TestWindow current = new();
        windows.Open("window", current);
        using TestWindow rejected = new();
        presenter.Rejected = rejected;
        Expect<InvalidOperationException>(() => windows.Open("window", rejected), "binding failure rejects replacement");
        Check(windows.TryGet("window", out WindowViewModel retained) && ReferenceEquals(retained, current) &&
            ReferenceEquals(presenter.Bound, current) && !current.IsDisposed, "failed replacement restores previous window and view");
        windows.Register("other", new TestWindowPresenter());
        TestWindow other = new();
        bool blockedThroughout = true;
        using (windows.HasOpenWindows.Subscribe(open => blockedThroughout &= open))
            windows.Open("other", other);
        Check(current.IsDisposed && !windows.TryGet("window", out _) && other.IsOpen.CurrentValue,
            "opening a window closes the other windows");
        Check(blockedThroughout, "switching windows never reports gameplay unblocked");
        Check(windows.CloseTop() && other.IsDisposed && !windows.CloseTop() && !windows.HasOpenWindows.CurrentValue,
            "only one window is ever open");
        current = new TestWindow();
        windows.Open("window", current);
        windows.Register("reentrant", new TestWindowPresenter());
        TestWindow old = new();
        TestWindow replacement = new();
        windows.Open("reentrant", old);
        using IDisposable closeOnReplacement = windows.Closed.Subscribe(model =>
        {
            if (ReferenceEquals(model, old))
                windows.Close("reentrant");
        });
        windows.Open("reentrant", replacement);
        Check(replacement.IsDisposed && !windows.TryGet("reentrant", out _),
            "replacement close observers can safely close new window");
        windows.Unregister("window", presenter);
        Check(current.IsDisposed && !windows.HasOpenWindows.CurrentValue, "unregister closes and disposes window");
    }

    private static void VerifyInventoryTransactions()
    {
        using InventoryGrid source = new(new InventoryGridData { OwnerId = "source", Size = new Vector2Int(2, 1) }, new ItemCatalog());
        using InventoryGrid target = new(new InventoryGridData { OwnerId = "target", Size = new Vector2Int(2, 1) }, new ItemCatalog());
        source.AddItems("wood", 10);
        bool completeState = true;
        int sourceChanges = 0;
        int targetChanges = 0;
        using IDisposable sourceBinding = source.Changed.Subscribe(_ =>
        {
            sourceChanges++;
            completeState &= source.GetAmount("wood") + target.GetAmount("wood") == 10;
            completeState &= source.GetSlots()[0, 0].State.CurrentValue.Amount + target.GetSlots()[0, 0].State.CurrentValue.Amount == 10;
        });
        using IDisposable targetBinding = target.Changed.Subscribe(_ =>
        {
            targetChanges++;
            completeState &= source.GetSlots()[0, 0].State.CurrentValue.Amount + target.GetSlots()[0, 0].State.CurrentValue.Amount == 10;
        });
        Check(source.MoveTo(new Vector2Int(0, 0), target, new Vector2Int(0, 0), 4), "transaction moves items");
        Check(completeState && sourceChanges == 1 && targetChanges == 1, "grid observers see completed transfer once");
        sourceBinding.Dispose();
        targetBinding.Dispose();

        source.AddItems("stone", 3);
        int removed = 0;
        int added = 0;
        using IDisposable removedBinding = source.ItemsRemoved.Subscribe(value => removed += value.Amount);
        using IDisposable addedBinding = target.ItemsAdded.Subscribe(value => added += value.Amount);
        Check(source.SwapWith(new Vector2Int(1, 0), target, new Vector2Int(0, 0)), "cross-grid swap succeeds");
        Check(removed == 3 && added == 3, "swap emits item movement notifications");

        ItemCatalog catalog = new();
        catalog.RegisterEquipment("helmet", EquipmentSlot.Headwear);
        using InventoryGrid equipment = new(new InventoryGridData
        {
            OwnerId = "equipment",
            Kind = InventoryGridKind.Equipment,
            Size = new Vector2Int(Enum.GetValues(typeof(EquipmentSlot)).Length, 1)
        }, catalog);
        InventorySlotProxy head = equipment.Proxy.Slots[(int)EquipmentSlot.Headwear];
        Expect<ArgumentException>(() => head.Set("wood", 1), "direct proxy edit obeys equipment restrictions");
        Expect<ArgumentException>(() => head.Set("helmet", 2), "direct equipment edit obeys capacity");
        Check(head.IsEmpty, "failed equipment proxy edits preserve slot");
        head.Set("helmet", 1);
        Check(head.Origin.ItemId == "helmet" && head.Origin.Amount == 1, "valid proxy edit synchronizes origin");
    }

    private static async Task ExpectAsync<T>(Func<UniTask> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException($"Failed: {name}");
    }

    private sealed class TestMigration : ISaveMigration
    {
        public int FromVersion => SaveDocument.CURRENT_VERSION - 1;
        public int ToVersion => SaveDocument.CURRENT_VERSION;
        public void Migrate(JObject document)
        {
            document["sections"] = JObject.Parse(InventoryJson("migrated"))["sections"];
        }
    }
}
