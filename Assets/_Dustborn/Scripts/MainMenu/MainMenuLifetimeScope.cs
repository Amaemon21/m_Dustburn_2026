using VContainer;
using VContainer.Unity;

public class MainMenuLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<MainMenuExit>(Lifetime.Singleton).As<IMainMenuExit>();
        new MainMenuUIInstaller().Install(builder);
        builder.Register<MainMenuEntryPoint>(Lifetime.Singleton);
    }
}
