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
    }
}
