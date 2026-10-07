using VContainer;
using VContainer.Unity;

public sealed class DroppedItemsInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<DroppedItemRepository>(Lifetime.Singleton);
        builder.Register<DroppedItemService>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
        builder.Register<IItemDropService, ItemDropService>(Lifetime.Singleton);
        builder.Register<IItemRewards, PlayerItemRewards>(Lifetime.Singleton);
    }
}
