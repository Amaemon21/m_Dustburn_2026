using R3;

public abstract class MainMenuScreenViewModel : ViewModel
{
    private readonly MainMenuScreen _screen;

    protected MainMenuNavigation Navigation { get; }
    public ReadOnlyReactiveProperty<bool> IsVisible { get; }
    protected Observable<Unit> Shown => IsVisible.Where(visible => visible).AsUnitObservable();

    protected MainMenuScreenViewModel(MainMenuNavigation navigation, MainMenuScreen screen)
    {
        _screen = screen;
        Navigation = navigation;
        IsVisible = navigation.Current.Select(current => current == screen).ToReadOnlyReactiveProperty();
        Disposables.Add(IsVisible);
    }

    public virtual void GoBack()
    {
        if (_screen != MainMenuScreen.Main)
            Navigation.Show(MainMenuScreen.Main);
    }

    protected ReactiveCommand<Unit> NavigationCommand(MainMenuScreen target)
    {
        ReactiveCommand<Unit> command = Command();
        Disposables.Add(command.Executed.Subscribe(_ => Navigation.Show(target)));
        return command;
    }

    protected ReactiveCommand<Unit> Command()
    {
        ReactiveCommand<Unit> command = new();
        Disposables.Add(command);
        return command;
    }
}
