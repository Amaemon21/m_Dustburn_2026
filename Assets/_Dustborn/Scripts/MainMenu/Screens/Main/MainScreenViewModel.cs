using R3;

public sealed class MainScreenViewModel : MainMenuScreenViewModel
{
    public string Notice { get; }
    public ReadOnlyReactiveProperty<string> ContinueDetails { get; }
    public ReactiveCommand<Unit> Continue { get; }
    public ReactiveCommand<Unit> NewGame { get; }
    public ReactiveCommand<Unit> LoadGame { get; }
    public ReactiveCommand<Unit> Settings { get; }
    public ReactiveCommand<Unit> TestScene { get; }
    public ReactiveCommand<Unit> Quit { get; }

    public MainScreenViewModel(MainMenuNavigation navigation, GameSlotCatalog catalog, IMainMenuExit exit,
        MainMenuEnterParams enterParams) : base(navigation, MainMenuScreen.Main)
    {
        Notice = enterParams.Notice;
        ContinueDetails = catalog.Latest.Select(MainMenuText.SlotSummary).ToReadOnlyReactiveProperty();
        Disposables.Add(ContinueDetails);

        Continue = Command();
        NewGame = NavigationCommand(MainMenuScreen.NewGame);
        LoadGame = NavigationCommand(MainMenuScreen.LoadGame);
        Settings = Command();
        Settings.SetCanExecute(false);
        TestScene = Command();
        Quit = Command();

        Disposables.Add(catalog.Latest.Subscribe(latest => Continue.SetCanExecute(latest != null)));
        Disposables.Add(catalog.Slots.Subscribe(slots => LoadGame.SetCanExecute(slots.Count > 0)));
        Disposables.Add(Continue.Executed.Subscribe(_ => exit.LoadGame(catalog.Latest.CurrentValue.Id)));
        Disposables.Add(TestScene.Executed.Subscribe(_ => exit.StartTestScene()));
        Disposables.Add(Quit.Executed.Subscribe(_ => exit.Quit()));
    }
}
