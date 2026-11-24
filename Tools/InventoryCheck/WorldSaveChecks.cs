using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static partial class Harness
{
    private static SaveRegistry WorldIdentityRegistry() => new SaveRegistry()
        .Register<WorldSaveData>("world", SaveScope.Slot, SaveInstaller.WORLD_FILE)
        .Register<PlayerPlacementData>(SaveInstaller.PLACEMENT_KEY, SaveScope.Slot, SaveInstaller.PLAYER_FILE);

    private static async Task VerifyWorldSave()
    {
        Check(GameplayEnterParams.ForScene(Scenes.Gameplay).SlotId == SaveService.DEFAULT_SLOT, "world scene uses the default slot");
        Check(GameplayEnterParams.ForScene(Scenes.Gameplay_Test).SlotId == GameplayEnterParams.TEST_SLOT, "test scene has its own slot");
        Check(GameplayEnterParams.TEST_SLOT != SaveService.DEFAULT_SLOT, "test and world slots differ");

        MemoryStorage storage = new();
        SaveService saves = new(WorldIdentityRegistry(), storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);

        WorldSaveRepository world = new(saves);
        world.Claim("v5/1337/8192/AAAA/BBBB/700", 1337, 8192);
        Check(saves.HasDirtyFiles && world.Origin.Seed == 1337, "a fresh slot adopts the world it is played on");
        world.Claim("v5/1337/8192/AAAA/BBBB/700", 1337, 8192);
        Expect<WorldSaveMismatchException>(() => world.Claim("v5/1337/8192/CCCC/BBBB/700", 1337, 8192), "a slot refuses another world");

        PlayerPlacementRepository placement = new(saves);
        Check(!placement.TryGet(out _, out _), "no placement before the player moved");
        placement.Write(new Vector3(10f, 20f, 30f), 90f);
        await saves.SaveDirtyAsync(CancellationToken.None);

        SaveService reloaded = new(WorldIdentityRegistry(), storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        Expect<WorldSaveMismatchException>(() => new WorldSaveRepository(reloaded).Claim("other", 0, 0), "the claim survives a reload");
        Check(new PlayerPlacementRepository(reloaded).TryGet(out Vector3 position, out float yaw)
            && position == new Vector3(10f, 20f, 30f) && Near(yaw, 90f),
            "player placement is restored");

        await VerifyLegacyInventoryFile();
        await VerifyItemActivation();
    }

    private static async Task VerifyItemActivation()
    {
        SaveRegistry registry = InventoryRegistry()
            .Register<WorldSaveData>("world", SaveScope.Slot, SaveInstaller.WORLD_FILE);
        MemoryStorage storage = new();
        SaveService saves = new(registry, storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        saves.Get<WorldSaveData>().ActivatedObjects = null;
        ItemCatalog catalog = WearCatalog();

        using (InventoryRepository repository = new(saves))
        using (InventoryService inventory = new(repository, catalog))
        using (PlayerInventoryService players = new(repository, inventory))
        {
            PlayerInventoryProxy player = players.GetOrCreatePlayerInventory("player", new Vector2Int(2, 2), 3);
            ItemActivationService activation = new(saves, inventory);
            Check(!activation.IsActivated("device") && !activation.TryActivate("device", player, "axe"),
                "missing saved activation data and a missing item leave the device inactive");

            inventory.AddItemsToInventory(player.Hotbar.OwnerId, Vector2Int.zero, "axe");
            Check(activation.HasRequiredItem(player, "axe") && !activation.TryActivate("", player, "axe"),
                "a hotbar item satisfies the requirement and an empty activation ID is rejected");
            Check(activation.TryActivate("device", player, "axe") && !inventory.Has(player.Hotbar.OwnerId, "axe"),
                "activation consumes one item from the hotbar");
            Check(activation.IsActivated("device") && !activation.IsActivated("other") && saves.IsDirty(SaveInstaller.WORLD_FILE),
                "activation marks the world file and applies only to the requested ID");

            inventory.AddItemsToInventory(player.Backpack.OwnerId, Vector2Int.zero, "axe");
            Check(!activation.TryActivate("device", player, "axe") && inventory.Has(player.Backpack.OwnerId, "axe"),
                "repeating activation leaves the next item intact");
            await saves.SaveDirtyAsync(CancellationToken.None);
        }

        SaveService reloaded = new(registry, storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        using InventoryRepository restoredRepository = new(reloaded);
        using InventoryService restoredInventory = new(restoredRepository, catalog);
        using PlayerInventoryService restoredPlayers = new(restoredRepository, restoredInventory);
        PlayerInventoryProxy restoredPlayer = restoredPlayers.GetOrCreatePlayerInventory("player", new Vector2Int(2, 2), 3);
        ItemActivationService restored = new(reloaded, restoredInventory);
        Check(restored.IsActivated("device") && !restoredInventory.Has(restoredPlayer.Hotbar.OwnerId, "axe"),
            "loaded device state and consumed item are restored together");
        Check(restored.TryActivate("other", restoredPlayer, "axe") && !restoredInventory.Has(restoredPlayer.Backpack.OwnerId, "axe"),
            "a different device can consume the item from the backpack");
    }

    private static async Task VerifyLegacyInventoryFile()
    {
        MemoryStorage storage = new();
        SaveService old = new(new SaveRegistry()
            .Register<InventoryData>(SaveInstaller.INVENTORY_KEY, SaveScope.Slot, SaveInstaller.INVENTORY_FILE), storage, ProjectSerializer());
        await old.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        old.Get<InventoryData>().Grids.Add(new InventoryGridData { OwnerId = "legacy", Size = new Vector2Int(2, 2) });
        old.MarkDirty<InventoryData>();
        await old.SaveDirtyAsync(CancellationToken.None);

        SaveRegistry current = new SaveRegistry()
            .Register<InventoryData>(SaveInstaller.INVENTORY_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE, SaveInstaller.INVENTORY_FILE)
            .Register<DroppedItemsData>(SaveInstaller.DROPPED_ITEMS_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE);
        SaveService moved = new(current, storage, ProjectSerializer());
        await moved.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        Check(moved.Get<InventoryData>().Grids.Exists(grid => grid.OwnerId == "legacy"), "inventory is read from its legacy file");
        Check(moved.IsDirty(SaveInstaller.WORLD_FILE), "an adopted section is written to its new file on the next save");

        await moved.SaveDirtyAsync(CancellationToken.None);
        SaveService reloaded = new(current, storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        Check(reloaded.Get<InventoryData>().Grids.Exists(grid => grid.OwnerId == "legacy") && !reloaded.HasDirtyFiles,
            "inventory lives in the world file after the move");
    }
}
