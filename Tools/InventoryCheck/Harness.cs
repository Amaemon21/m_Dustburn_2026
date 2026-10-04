using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static int _checks;

    private static async Task Main()
    {
        GameplayEnterParams testScene = new("test_slot", Scenes.Gameplay_Test);
        Check(testScene.SceneName == "Gameplay_Test" && testScene.SlotId == "test_slot", "test gameplay route preserved");
        Check(new GameplayEnterParams("player").Scene == Scenes.Gameplay, "default gameplay route unchanged");
        Check(new MainMenuEnterParams(string.Empty).SceneName == "MainMenu", "main menu route unchanged");
        Expect<ArgumentOutOfRangeException>(() => new GameplayEnterParams("player", Scenes.Boot), "non-gameplay scene rejected");

        using InventoryGrid grid = new(new InventoryGridData { OwnerId = "player", Size = new Vector2Int(2, 2) }, new ItemCatalog());
        int addedEvents = 0;
        using IDisposable added = grid.ItemsAdded.Subscribe(value => addedEvents += value.Amount);
        Check(grid.AddItems("wood", 250).ItemsAddedAmount == 250, "stack overflow");
        Check(grid.GetSlots()[0, 0].Amount == 99 && grid.GetSlots()[1, 0].Amount == 52, "slot ordering");
        Check(grid.AddItems("stone", 200).ItemsNotAddedAmount == 101, "partial capacity");
        Check(addedEvents == 349, "actual added notifications");
        Check(!grid.RemoveItems("wood", 251).Success && grid.GetAmount("wood") == 250, "atomic failed removal");
        Check(grid.RemoveItems("wood", 151).Success && grid.GetAmount("wood") == 99, "multi-stack removal");
        Check(grid.AddItems(new Vector2Int(1, 1), "wood", 1).ItemsAddedAmount == 0, "occupied target preserved");
        Expect<ArgumentOutOfRangeException>(() => grid.AddItems("wood", -1), "negative add");
        Expect<ArgumentOutOfRangeException>(() => grid.RemoveItems("wood", 0), "zero removal");
        Expect<ArgumentOutOfRangeException>(() => grid.AddItems(new Vector2Int(3, 0), "wood"), "invalid coordinate");
        Expect<InvalidOperationException>(() => grid.SetSize(new Vector2Int(1, 1)), "occupied shrink");
        grid.SetSize(new Vector2Int(3, 2));
        Check(grid.GetSlots()[1, 1].ItemId == "stone", "resize keeps coordinates");

        MemoryStorage storage = new();
        SaveRegistry registry = new SaveRegistry().Register<InventoryData>("inventory", SaveScope.Slot, SaveInstaller.INVENTORY_FILE);
        SaveService saves = new(registry, storage, ProjectSerializer());
        using (InventoryRepository repository = new(saves))
        using (InventoryService proxy = new(repository, new ItemCatalog()))
        {
            proxy.RegisterInventory(new InventoryGridData { OwnerId = "player", Size = new Vector2Int(2, 2) });
            proxy.RegisterInventory(new InventoryGridData { OwnerId = "chest", Size = new Vector2Int(1, 2) });
            proxy.AddItemsToInventory("player", new Vector2Int(1, 0), "wood", 7);
            proxy.AddItemsToInventory("chest", "stone", 20);
            using InventoryGridViewModel viewModel = new(proxy, "player", new ItemCatalog());
            Check(viewModel.Slots.CurrentValue[2].State.CurrentValue.Amount == 7, "initial view model state");
            proxy.AddItemsToInventory("player", "wood", 2);
            Check(viewModel.Slots.CurrentValue[2].State.CurrentValue.Amount == 9, "reactive view model update");
            proxy.SwitchSlots("player", new Vector2Int(1, 0), new Vector2Int(0, 1));
            Check(viewModel.Slots.CurrentValue[1].State.CurrentValue.Amount == 9, "switch notification");
            IReadOnlyList<InventorySlotViewModel> previous = viewModel.Slots.CurrentValue;
            proxy.SetSize("player", new Vector2Int(3, 2));
            Check(previous[0].IsDisposed && viewModel.Slots.CurrentValue.Count == 6, "resize disposes slot view models");
            await repository.SaveAsync(CancellationToken.None);
            Check(!saves.HasDirtyFiles, "successful save clears dirty");
            InventoryGridProxy entity = proxy.GetProxy("player");
            Check(ReferenceEquals(entity.Origin, saves.Get<InventoryData>().Grids[0]), "save registry keeps actual grid origin");
            entity.Slots[1].Set(entity.Slots[1].ItemId, 10);
            Check(saves.Get<InventoryData>().Grids[0].Slots[1].Amount == 10 && saves.HasDirtyFiles,
                "direct proxy edits update save data and mark file dirty");
            entity.Slots[1].Set(entity.Slots[1].ItemId, 9);
            proxy.AddItemsToInventory("player", "wood", 1);
            storage.OnWrite = () => proxy.AddItemsToInventory("player", "wood", 1);
            await repository.SaveAsync(CancellationToken.None);
            Check(saves.HasDirtyFiles, "changes during write remain dirty");
            await repository.SaveAsync(CancellationToken.None);
        }
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using (InventoryRepository repository = new(saves))
        using (InventoryService restored = new(repository, new ItemCatalog()))
        {
            Check(restored.GetInventory("player").Size == new Vector2Int(3, 2), "saved size restored");
            Check(restored.GetInventory("player").GetSlots()[0, 1].Amount == 11, "saved layout restored");
            Check(restored.GetInventory("chest").GetAmount("stone") == 20, "multiple owners restored");
            Check(!saves.HasDirtyFiles, "load does not dirty data");
            restored.RemoveItemsFromInventory("chest", "stone", 20);
            await repository.SaveAsync(CancellationToken.None);
            saves.SetActiveSlot("slot_1");
        }
        using (InventoryRepository repository = new(saves))
        using (InventoryService fresh = new(repository, new ItemCatalog()))
            Check(!fresh.TryGetInventory("player", out _), "slot isolation");

        VerifyEntityProxies();
        VerifyInventoryTransactions();
        VerifySelectedItemInfo();
        VerifyInventoryDragDrop();
        VerifyInventoryShortcuts();
        VerifyPickupStackPriority();
        VerifyItemStackLimits();
        await VerifyPlayerInventory();
        await VerifyLegacyEquipmentLayout();

        using WindowService windows = new();
        await VerifySaveRecovery();
        await VerifySaveSessions();
        await VerifyFileStorage();
        VerifyWindowTransactions();
        windows.Register("first", new TestWindowPresenter());
        windows.Register("second", new TestWindowPresenter());
        TestWindow first = new();
        TestWindow second = new();
        windows.Open("first", first);
        windows.Open("second", second);
        Check(windows.OpenWindows.Count == 2 && first.IsOpen.CurrentValue, "multiple open windows");
        TestWindow replacement = new();
        windows.Open("first", replacement);
        Check(first.IsDisposed && replacement.IsOpen.CurrentValue, "replacement disposes previous model");
        replacement.Close.Execute(Unit.Default);
        Check(replacement.IsDisposed && windows.OpenWindows.Count == 1, "close command");
        windows.CloseAll();
        Check(second.IsDisposed && windows.OpenWindows.Count == 0, "all windows disposed");
        Console.WriteLine($"PASS: {_checks} inventory, MVVM, proxy and save checks");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Failed: {name}");
        _checks++;
    }

    private static async Task VerifyPlayerInventory()
    {
        ItemCatalog catalog = new();
        catalog.RegisterEquipment("helmet", EquipmentSlot.Headwear);
        catalog.RegisterEquipment("shirt", EquipmentSlot.TorsoBase);
        MemoryStorage storage = new();
        SaveRegistry registry = new SaveRegistry().Register<InventoryData>("inventory", SaveScope.Slot, SaveInstaller.INVENTORY_FILE);
        SaveService saves = new(registry, storage, ProjectSerializer());
        using (InventoryRepository repository = new(saves))
        using (InventoryService inventory = new(repository, catalog))
        using (PlayerInventoryService players = new(repository, inventory))
        {
            PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("player", new Vector2Int(6, 4), 7);
            Expect<ArgumentOutOfRangeException>(() => players.SelectHotbarSlot(player.OwnerId, -1), "negative hotbar index rejected");
            Expect<ArgumentOutOfRangeException>(() => players.SelectHotbarSlot(player.OwnerId, 7), "past-end hotbar index rejected");
            player.Origin.SelectedHotbarSlot = 999;
            using (PlayerInventoryProxy repaired = new(player.Origin, player.Backpack, player.Hotbar, player.Equipment))
                Check(repaired.SelectedHotbarSlot.CurrentValue == 0 && player.Origin.SelectedHotbarSlot == 0,
                    "invalid saved hotbar selection repaired");
            using HudHotbarViewModel hud = new(player, players, catalog);
            Check(hud.Slots.Count == player.Hotbar.Size.x && hud.Slots[0].State.CurrentValue.IsEmpty, "HUD binds actual hotbar cells");
            Check(player.Backpack.Size == new Vector2Int(6, 4) && player.Hotbar.Size == new Vector2Int(7, 1), "player default grids");
            Check(ReferenceEquals(player, players.GetOrCreatePlayerInventory("player", new Vector2Int(6, 4), 7)), "player registration idempotent");
            inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 0), "helmet", 2);
            inventory.AddItemsToInventory(player.Backpack.OwnerId, new Vector2Int(0, 1), "wood", 20);
            Check(!players.Equip(player.OwnerId, new Vector2Int(0, 0), EquipmentSlot.TorsoOuter), "wrong equipment type rejected");
            Check(players.Equip(player.OwnerId, new Vector2Int(0, 0), EquipmentSlot.Headwear), "one item equipped");
            Check(player.Backpack.GetAmount("helmet") == 1 && player.Equipment.GetAmount("helmet") == 1, "equip conserves items");
            Check(!players.Equip(player.OwnerId, new Vector2Int(0, 0), EquipmentSlot.Headwear), "equipment cannot stack");
            Check(!players.Equip(player.OwnerId, new Vector2Int(0, 1), EquipmentSlot.Hands), "unknown equipment rejected");
            Check(!inventory.SwapSlots(player.Backpack.OwnerId, new Vector2Int(0, 1),
                player.Equipment.OwnerId, new Vector2Int((int)EquipmentSlot.Headwear, 0)), "invalid equipment swap rejected");
            Check(player.Backpack.GetAmount("wood") == 20 && player.Equipment.GetAmount("helmet") == 1, "failed swap conserves items");
            Check(players.Unequip(player.OwnerId, EquipmentSlot.Headwear, new Vector2Int(0, 0)), "unequip merges into backpack");
            Check(player.Backpack.GetAmount("helmet") == 2 && player.Equipment.GetAmount("helmet") == 0, "unequip conserves items");
            Check(inventory.MoveItems(player.Backpack.OwnerId, new Vector2Int(0, 1),
                player.Hotbar.OwnerId, new Vector2Int(2, 0), 20), "backpack to hotbar");
            Check(player.Hotbar.GetAmount("wood") == 20 && player.Backpack.GetAmount("wood") == 0, "hotbar owns transferred stack");
            Check(hud.Slots[2].State.CurrentValue.ItemId == "wood" && hud.Slots[2].State.CurrentValue.Amount == 20, "HUD observes mini inventory transfer");
            Check(!inventory.MoveItems(player.Hotbar.OwnerId, new Vector2Int(2, 0),
                player.Backpack.OwnerId, new Vector2Int(0, 0), 20), "occupied backpack transfer rejected");
            Expect<InvalidOperationException>(() => inventory.SetSize(player.Hotbar.OwnerId, new Vector2Int(9, 1)), "hotbar resize rejected");

            using WindowService windows = new();
            windows.Register(PlayerMenuService.PLAYER_MENU_WINDOW, new TestWindowPresenter());
            using PlayerMenuService screen = new(new PlayerMenuViewModelFactory(inventory, players, catalog), windows);
            screen.OpenInventory(player.OwnerId);
            Check(screen.IsPlayerMenuOpen.CurrentValue && windows.TryGet(PlayerMenuService.PLAYER_MENU_WINDOW, out _), "player menu opens");
            windows.TryGet(PlayerMenuService.PLAYER_MENU_WINDOW, out WindowViewModel window);
            PlayerMenuScreenViewModel menu = (PlayerMenuScreenViewModel)window;
            menu.SelectTab.Execute(PlayerMenuTab.Map);
            Check(menu.SelectedTab.CurrentValue == PlayerMenuTab.Map, "placeholder tab switches");
            screen.OpenInventory(player.OwnerId);
            Check(ReferenceEquals(window, GetWindow(windows)) && menu.SelectedTab.CurrentValue == PlayerMenuTab.Inventory, "inventory focuses existing menu");
            players.SelectHotbarSlot(player.OwnerId, 2);
            Check(player.Origin.SelectedHotbarSlot == 2 && ReferenceEquals(player.Origin, saves.Get<InventoryData>().Players[0]),
                "player proxy selection immediately updates saved origin");
            Check(menu.Inventory.Hotbar.Grid.SelectedSlot.CurrentValue == new Vector2Int(2, 0), "hotbar selection reactive");
            Check(hud.Slots[2].IsSelected.CurrentValue && !hud.Slots[0].IsSelected.CurrentValue, "HUD shares selected hotbar slot");
            menu.Inventory.Backpack.Slots.CurrentValue[0].Select.Execute(Unit.Default);
            menu.Inventory.Equipment.Slots.CurrentValue[(int)EquipmentSlot.Headwear].Select.Execute(Unit.Default);
            Check(player.Equipment.GetAmount("helmet") == 0 && !menu.Inventory.ItemInfo.HasItem &&
                !menu.Inventory.Backpack.SelectedSlot.CurrentValue.HasValue,
                "empty equipment click cancels selection without transferring items");
            Check(players.Equip(player.OwnerId, new Vector2Int(0, 0), EquipmentSlot.Headwear),
                "equipment transfer remains available through player service");
            menu.Inventory.Equipment.Slots.CurrentValue[(int)EquipmentSlot.Headwear].Select.Execute(Unit.Default);
            Check(menu.Inventory.Equipment.Slots.CurrentValue[(int)EquipmentSlot.Headwear].IsSelected.CurrentValue,
                "occupied equipment click retains selection frame");
            menu.Inventory.Unequip.Execute(Unit.Default);
            Check(player.Equipment.GetAmount("helmet") == 0, "inventory tab unequip command");
            menu.Inventory.Backpack.Slots.CurrentValue[1].Select.Execute(Unit.Default);
            menu.Inventory.TakeFromHotbar.Execute(Unit.Default);
            Check(player.Backpack.GetAmount("wood") == 20 && player.Hotbar.GetAmount("wood") == 0, "inventory tab takes hotbar stack");
            menu.Inventory.Hotbar.Grid.Slots.CurrentValue[2].Select.Execute(Unit.Default);
            Check(player.Hotbar.GetAmount("wood") == 0 && !menu.Inventory.ItemInfo.HasItem,
                "empty hotbar click cancels item info instead of transferring a stack");
            Vector2Int woodSource = default;
            foreach (InventorySlotViewModel slot in menu.Inventory.Backpack.Slots.CurrentValue)
                if (slot.State.CurrentValue.ItemId == "wood")
                    woodSource = slot.Coordinates;
            Check(inventory.MoveItems(player.Backpack.OwnerId, woodSource, player.Hotbar.OwnerId,
                new Vector2Int(2, 0), 20), "explicit hotbar transfer remains available");
            screen.TogglePlayerMenu(player.OwnerId);
            Check(menu.IsDisposed && !screen.IsPlayerMenuOpen.CurrentValue, "toggle closes and disposes player menu");
            Check(!hud.IsDisposed && hud.Slots[2].State.CurrentValue.Amount == 20, "HUD survives inventory menu close");
            inventory.RemoveItemsFromInventory(player.Hotbar.OwnerId, new Vector2Int(2, 0), "wood", 20);
            Check(hud.Slots[2].State.CurrentValue.IsEmpty, "HUD observes removal while menu is closed");
            inventory.AddItemsToInventory(player.Hotbar.OwnerId, new Vector2Int(2, 0), "wood", 20);
            screen.TogglePlayerMenu(player.OwnerId);
            Check(screen.IsPlayerMenuOpen.CurrentValue, "toggle reopens player menu");
            screen.ClosePlayerMenu();
            await repository.SaveAsync(CancellationToken.None);
            Check(!saves.HasDirtyFiles, "player state saved");
            Check(!inventory.MoveItems(player.Hotbar.OwnerId, new Vector2Int(2, 0),
                player.Equipment.OwnerId, new Vector2Int(0, 0)), "invalid transfer after save rejected");
            Check(!saves.HasDirtyFiles, "failed transfer does not dirty save");
        }
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using InventoryRepository restoredRepository = new(saves);
        using InventoryService restored = new(restoredRepository, catalog);
        using PlayerInventoryService restoredPlayers = new(restoredRepository, restored);
        PlayerInventoryProxy loaded = restoredPlayers.GetPlayerInventory("player");
        using HudHotbarViewModel restoredHud = new(loaded, restoredPlayers, catalog);
        Check(loaded.SelectedHotbarSlot.CurrentValue == 2 && loaded.Hotbar.GetSlots()[2, 0].Amount == 20, "hotbar layout and selection restored");
        Check(restoredHud.Slots[2].State.CurrentValue.Amount == 20 && restoredHud.Slots[2].IsSelected.CurrentValue, "HUD starts from saved mini inventory");
        Check(loaded.Backpack.GetAmount("helmet") == 2, "backpack restored after equip transactions");
        Check(!saves.HasDirtyFiles, "restoring player does not dirty save");
        restoredPlayers.Equip("player", new Vector2Int(0, 0), EquipmentSlot.Headwear);
        await restoredRepository.SaveAsync(CancellationToken.None);
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using InventoryRepository equippedRepository = new(saves);
        using InventoryService equippedInventory = new(equippedRepository, catalog);
        using PlayerInventoryService equippedRestore = new(equippedRepository, equippedInventory);
        Check(equippedRestore.GetPlayerInventory("player").Equipment.GetAmount("helmet") == 1, "equipment restored");
    }

    private static void VerifyEntityProxies()
    {
        InventorySlotData origin = new() { ItemId = "wood", Amount = 2 };
        using InventorySlotProxy slot = new(origin);
        slot.Set(slot.ItemId, 5);
        Check(origin.Amount == 5 && slot.State.CurrentValue.Amount == 5, "slot property immediately updates origin and state");
        slot.Set("stone", slot.Amount);
        Check(origin.ItemId == "stone", "slot item property immediately updates origin");
        Expect<ArgumentException>(() => slot.Set(null, 5), "nonempty slot requires item id");
        Expect<ArgumentException>(() => slot.Set("wood", -1), "negative slot amount rejected");
        Expect<ArgumentException>(() => slot.Set("wood", InventorySlotProxy.MAX_AMOUNT + 1), "overcapacity slot amount rejected");
        Check(origin.ItemId == "stone" && origin.Amount == 5, "failed slot edits preserve origin");
        slot.Set(null, 0);
        Check(origin.Amount == 0 && origin.ItemId == null, "clear immediately updates origin");
        InventoryGridData data = new() { OwnerId = "entity", Size = new Vector2Int(2, 2) };
        using InventoryGridProxy grid = new(data);
        InventorySlotData retained = data.Slots[2];
        InventorySlotProxy retainedProxy = grid.Slots[2];
        retainedProxy.Set("wood", 3);
        Check(ReferenceEquals(retained, retainedProxy.Origin) && retained.Amount == 3, "grid stores actual slot origin references");
        grid.Resize(new Vector2Int(3, 3));
        Check(data.Size == new Vector2Int(3, 3) && data.Slots.Count == 9, "resize immediately updates origin layout");
        Check(ReferenceEquals(retained, data.Slots[3]) && ReferenceEquals(retainedProxy, grid.Slots[3]), "resize retains entities by coordinate");
        grid.Resize(new Vector2Int(2, 2));
        Check(ReferenceEquals(retained, data.Slots[2]), "shrink retains correct entity");
    }

    private static WindowViewModel GetWindow(WindowService windows)
    {
        windows.TryGet(PlayerMenuService.PLAYER_MENU_WINDOW, out WindowViewModel window);
        return window;
    }

    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException($"Failed: {name}");
    }

    private sealed class TestWindow : WindowViewModel { }

    private sealed class MemoryStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new();
        public readonly Dictionary<string, string> Backups = new();
        public readonly Dictionary<string, string> Quarantined = new();
        public readonly Dictionary<string, UniTaskCompletionSource<string>> PendingReads = new();
        public Action OnWrite;
        public string Root => "memory";
        public UniTask<string> ReadAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (PendingReads.TryGetValue(path, out UniTaskCompletionSource<string> pending))
                return pending.Task;
            return UniTask.FromResult(Files.TryGetValue(path, out string text) ? text : null);
        }
        public UniTask<string> ReadBackupAsync(string path, CancellationToken token)
            => UniTask.FromResult(Backups.TryGetValue(path, out string text) ? text : null);
        public UniTask QuarantineAsync(string path, CancellationToken token)
        {
            if (Files.Remove(path, out string text))
                Quarantined[path] = text;
            else if (path.EndsWith(".bak") && Backups.Remove(path.Substring(0, path.Length - 4), out string backup))
                Quarantined[path] = backup;
            return UniTask.CompletedTask;
        }
        public UniTask WriteAsync(string path, string text, CancellationToken token)
        {
            if (Files.TryGetValue(path, out string previous))
                Backups[path] = previous;
            Files[path] = text;
            Action callback = OnWrite;
            OnWrite = null;
            callback?.Invoke();
            return UniTask.CompletedTask;
        }
        public UniTask<bool> ExistsAsync(string path, CancellationToken token) => UniTask.FromResult(Files.ContainsKey(path));
        public UniTask DeleteAsync(string path, CancellationToken token) { Files.Remove(path); return UniTask.CompletedTask; }
        public UniTask DeleteDirectoryAsync(string path, CancellationToken token) => UniTask.CompletedTask;
        public UniTask<string[]> ListDirectoriesAsync(string path, CancellationToken token) => UniTask.FromResult(Array.Empty<string>());
    }
}
