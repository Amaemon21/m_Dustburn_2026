using VContainer;
using VContainer.Unity;

public sealed class BlocksInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<BlockRepository>(Lifetime.Singleton);
        builder.Register<BlockService>(Lifetime.Singleton).AsSelf().As<IDecorObjectHook>();
    }
}
