using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class UnitInstaller : IInstaller
{
    private readonly Transform _playerTransform;
    private readonly UnitSpawnPoint[] _spawnPoints;

    public UnitInstaller(Transform playerTransform, UnitSpawnPoint[] spawnPoints)
    {
        _playerTransform = playerTransform;
        _spawnPoints = spawnPoints;
    }

    public void Install(IContainerBuilder builder)
    {
        if (_playerTransform == null)
        {
            Debug.LogWarning($"{nameof(UnitInstaller)}: player transform is not assigned on the scope, units are not registered");
            return;
        }

        UnitDatabaseConfig database = Resources.Load<UnitDatabaseConfig>(UnitDatabaseConfig.RESOURCES_PATH);

        if (database == null)
        {
            Debug.LogError($"{nameof(UnitInstaller)}: no {nameof(UnitDatabaseConfig)} at Resources/{UnitDatabaseConfig.RESOURCES_PATH}, units are not registered");
            return;
        }

        builder.RegisterInstance(_playerTransform);
        builder.RegisterInstance(_spawnPoints ?? System.Array.Empty<UnitSpawnPoint>());
        builder.RegisterInstance(database);

        builder.Register<UnitFactory>(Lifetime.Singleton);
        builder.RegisterEntryPoint<UnitSpawnService>();
    }
}
