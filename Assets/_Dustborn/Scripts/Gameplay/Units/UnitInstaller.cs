using VContainer;
using VContainer.Unity;

public sealed class UnitInstaller : IInstaller
{
    private readonly UnitSpawnPoint[] _spawnPoints;

    public UnitInstaller(UnitSpawnPoint[] spawnPoints)
    {
        _spawnPoints = spawnPoints;
    }

    public void Install(IContainerBuilder builder)
    {
        builder.RegisterInstance(_spawnPoints ?? System.Array.Empty<UnitSpawnPoint>());

        builder.Register<UnitFactory>(Lifetime.Singleton);
        builder.Register<UnitSpawnService>(Lifetime.Singleton).As<IGameplayActivatable>();
    }
}
