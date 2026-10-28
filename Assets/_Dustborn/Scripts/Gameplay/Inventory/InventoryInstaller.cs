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
        builder.Register<InventoryWeightService>(Lifetime.Singleton);
        builder.Register<LocalPlayerInventory>(Lifetime.Singleton);
        builder.Register(resolver => new PlayerEncumbrance(
                resolver.Resolve<LocalPlayerInventory>().Weight,
                resolver.Resolve<PlayerInventorySettings>().CarryZones,
                resolver.Resolve<PlayerStatSheet>(),
                resolver.Resolve<PlayerMovementModifiers>()), Lifetime.Singleton)
            .AsSelf().As<IGameplayActivatable>();
    }
}
