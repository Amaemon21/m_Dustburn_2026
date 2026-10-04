using VContainer;
using VContainer.Unity;

public sealed class InventoryInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register(resolver => new ItemCatalog(resolver.Resolve<InventoryItemDatabase>()), Lifetime.Singleton);
        builder.Register<InventoryRepository>(Lifetime.Singleton);
        builder.Register<InventoryService>(Lifetime.Singleton).AsSelf().As<IInventoryService>();
        builder.Register<PlayerInventoryService>(Lifetime.Singleton);
        builder.Register<LocalPlayerInventory>(Lifetime.Singleton);
    }
}
