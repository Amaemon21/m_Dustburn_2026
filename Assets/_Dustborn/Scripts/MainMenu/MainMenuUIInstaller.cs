using VContainer;
using VContainer.Unity;

public sealed class MainMenuUIInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<MainMenuNavigation>(Lifetime.Singleton);
        builder.Register<GameSlotCatalog>(Lifetime.Singleton);
        builder.Register<WorldSelection>(Lifetime.Singleton);
        builder.Register<ConfirmDialogViewModel>(Lifetime.Singleton);
        builder.Register<WorldPreviewCache>(Lifetime.Singleton);
        builder.Register<MainScreenViewModel>(Lifetime.Singleton).AsSelf().As<MainMenuScreenViewModel>();
        builder.Register<NewGameScreenViewModel>(Lifetime.Singleton).AsSelf().As<MainMenuScreenViewModel>();
        builder.Register<LoadGameScreenViewModel>(Lifetime.Singleton).AsSelf().As<MainMenuScreenViewModel>();
        builder.Register<CreateWorldScreenViewModel>(Lifetime.Singleton).AsSelf().As<MainMenuScreenViewModel>();
        builder.Register<MainMenuBackInput>(Lifetime.Singleton);
    }
}
