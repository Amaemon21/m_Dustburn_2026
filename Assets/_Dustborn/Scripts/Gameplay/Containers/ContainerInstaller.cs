using VContainer;
using VContainer.Unity;

public sealed class ContainerInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<ContainerInventoryService>(Lifetime.Singleton);
        builder.Register<ContainerScreenService>(Lifetime.Singleton);
    }
}
