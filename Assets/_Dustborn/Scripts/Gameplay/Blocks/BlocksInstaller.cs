using VContainer;
using VContainer.Unity;

public sealed class BlocksInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<BlockRepository>(Lifetime.Singleton);
        builder.Register<BlockService>(Lifetime.Singleton).AsSelf().As<IDecorObjectHook>();
        builder.Register<BlockSupport>(Lifetime.Singleton);
        builder.Register<BlockPlacementProbe>(Lifetime.Singleton);
        builder.Register<BlockPlacementPreview>(Lifetime.Singleton);
        builder.Register<BlockPlacementService>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
        builder.RegisterEntryPoint<BlockBuildingController>().AsSelf().As<IGameplayActivatable>();
    }
}
