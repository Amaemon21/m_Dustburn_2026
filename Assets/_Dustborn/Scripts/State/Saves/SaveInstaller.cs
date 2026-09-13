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
    public const string WORLD_FILE = "world";
    public const string GAME_STATE_FILE = "gamestate";

    public void Install(IContainerBuilder builder)
    {
        SaveRegistry registry = new SaveRegistry()
            .Register<SettingsData>("settings", SaveScope.Global, SETTINGS_FILE)
            .Register<ProgressData>("progress", SaveScope.Slot, PROGRESS_FILE)
            .Register<InventoryData>("inventory", SaveScope.Slot, INVENTORY_FILE)
            .Register<WorldSaveData>("world", SaveScope.Slot, WORLD_FILE)
            .Register<GameStateData>("gamestate", SaveScope.Slot, GAME_STATE_FILE);

        string root = Path.Combine(Application.persistentDataPath, SAVES_FOLDER);

        builder.RegisterInstance(registry);
        builder.RegisterInstance<ISaveStorage>(new FileSaveStorage(root));
        builder.Register<ISaveSerializer, NewtonsoftSaveSerializer>(Lifetime.Singleton);
        builder.Register<ISaveService, SaveService>(Lifetime.Singleton);
    }
}
