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
        builder.RegisterInstance(new SceneUnitSpawnSource(_spawnPoints)).As<IUnitSpawnSource>();

        builder.Register<UnitNavigation>(Lifetime.Singleton);
        builder.Register<UnitFactory>(Lifetime.Singleton);
        builder.Register<UnitSpawnService>(Lifetime.Singleton).As<IGameplayActivatable>();
    }
}
