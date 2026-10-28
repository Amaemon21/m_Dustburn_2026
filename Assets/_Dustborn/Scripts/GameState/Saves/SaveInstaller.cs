using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class SaveInstaller : IInstaller
{
    public const string SAVES_FOLDER = "Saves";

    public const string SETTINGS_FILE = "settings";
    public const string PROGRESS_FILE = "progress";
    public const string INVENTORY_FILE = "inventory";
    public const string INVENTORY_KEY = "inventory";
    public const string WORLD_FILE = "world";
    public const string DROPPED_ITEMS_KEY = "droppedItems";
    public const string BLOCKS_KEY = "blocks";
    public const string GAME_STATE_FILE = "gamestate";
    public const string PLAYER_FILE = "player";
    public const string VITALS_KEY = "vitals";
    public const string PROGRESSION_KEY = "progression";
    public const string PLACEMENT_KEY = "placement";

    public void Install(IContainerBuilder builder)
    {
        SaveRegistry registry = new SaveRegistry()
            .Register<SettingsData>("settings", SaveScope.Global, SETTINGS_FILE)
            .Register<ProgressData>("progress", SaveScope.Slot, PROGRESS_FILE)
            .Register<InventoryData>(INVENTORY_KEY, SaveScope.Slot, WORLD_FILE, INVENTORY_FILE)
            .Register<WorldSaveData>("world", SaveScope.Slot, WORLD_FILE)
            .Register<DroppedItemsData>(DROPPED_ITEMS_KEY, SaveScope.Slot, WORLD_FILE)
            .Register<BlocksData>(BLOCKS_KEY, SaveScope.Slot, WORLD_FILE)
            .Register<PlayerVitalsData>(VITALS_KEY, SaveScope.Slot, PLAYER_FILE)
            .Register<PlayerProgressionData>(PROGRESSION_KEY, SaveScope.Slot, PLAYER_FILE)
            .Register<PlayerPlacementData>(PLACEMENT_KEY, SaveScope.Slot, PLAYER_FILE)
            .Register<GameStateData>("gamestate", SaveScope.Slot, GAME_STATE_FILE);

        string root = Path.Combine(Application.persistentDataPath, SAVES_FOLDER);

        builder.RegisterInstance(registry);
        builder.Register<InventoryEquipmentLayoutMigration>(Lifetime.Singleton).As<ISaveMigration>();
        builder.Register<SaveMigrationService>(Lifetime.Singleton);
        builder.RegisterInstance<ISaveStorage>(new FileSaveStorage(root));
        builder.Register<ISaveSerializer, NewtonsoftSaveSerializer>(Lifetime.Singleton);
        builder.Register<ISaveService, SaveService>(Lifetime.Singleton);
    }
}
