using VContainer;
using VContainer.Unity;

public sealed class GameplayUIInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<WindowService>(Lifetime.Singleton);
        builder.Register<ScreenService>(Lifetime.Singleton).AsSelf().As<IScreenService>();
        builder.Register<WindowBindingServiceFactory>(Lifetime.Singleton);
        builder.Register<PlayerMenuViewModelFactory>(Lifetime.Singleton);
        builder.Register<PlayerMenuService>(Lifetime.Singleton);
        builder.Register<PauseScreenService>(Lifetime.Singleton);
        builder.Register<PlayerStatusViewModel>(Lifetime.Singleton);
        builder.Register<HudPlayerEffectsViewModel>(Lifetime.Singleton);
        builder.Register(container => new HudHotbarViewModel(container.Resolve<LocalPlayerInventory>().Proxy,
            container.Resolve<PlayerInventoryService>(), container.Resolve<ItemCatalog>()), Lifetime.Singleton);
        builder.Register(container => new ItemNotificationsViewModel(container.Resolve<LocalPlayerInventory>().Proxy,
            container.Resolve<PlayerInventoryService>(), container.Resolve<ItemCatalog>()), Lifetime.Singleton);
    }
}
