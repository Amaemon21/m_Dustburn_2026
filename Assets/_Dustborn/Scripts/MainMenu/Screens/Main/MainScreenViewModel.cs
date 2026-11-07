using R3;
using Cysharp.Threading.Tasks;

public sealed class MainScreenViewModel : MainMenuScreenViewModel
{
    public ReadOnlyReactiveProperty<string> ContinueDetails { get; }
    public ReactiveCommand<Unit> Continue { get; }
    public ReactiveCommand<Unit> NewGame { get; }
    public ReactiveCommand<Unit> LoadGame { get; }
    public ReactiveCommand<Unit> Settings { get; }
    public ReactiveCommand<Unit> TestScene { get; }
    public ReactiveCommand<Unit> Quit { get; }

    public MainScreenViewModel(MainMenuNavigation navigation, GameSlotCatalog catalog, IMainMenuExit exit,
        MainMenuEnterParams enterParams, GameSlotLauncher launcher) : base(navigation, MainMenuScreen.Main)
    {
        ContinueDetails = catalog.Latest.Select(MainMenuText.SlotSummary).ToReadOnlyReactiveProperty();


        Continue = Command();
        NewGame = NavigationCommand(MainMenuScreen.NewGame);
        LoadGame = NavigationCommand(MainMenuScreen.LoadGame);
        Settings = Command();
        Settings.SetCanExecute(false);
        TestScene = Command();
        Quit = Command();

        Disposables.Add(catalog.Latest.CombineLatest(launcher.Busy, (latest, busy) => latest != null && !busy)
            .Subscribe(Continue.SetCanExecute));
        Disposables.Add(catalog.Slots.CombineLatest(launcher.Busy, (slots, busy) => slots.Count > 0 && !busy)
            .Subscribe(LoadGame.SetCanExecute));
        Disposables.Add(launcher.Busy.Subscribe(busy =>
        {
            NewGame.SetCanExecute(!busy);
            TestScene.SetCanExecute(!busy);
            Quit.SetCanExecute(!busy);
        }));
        Disposables.Add(Continue.Executed.Subscribe(_ => launcher.LoadAsync(catalog.Latest.CurrentValue?.Id).Forget()));
        Disposables.Add(TestScene.Executed.Subscribe(_ => exit.StartTestScene()));
        Disposables.Add(Quit.Executed.Subscribe(_ => exit.Quit()));
        Disposables.Add(Shown.Subscribe(_ => catalog.RefreshAsync().Forget()));
    }
    protected override void OnDisposed()
    {
        base.OnDisposed();
        ContinueDetails.Dispose();
    }
}
