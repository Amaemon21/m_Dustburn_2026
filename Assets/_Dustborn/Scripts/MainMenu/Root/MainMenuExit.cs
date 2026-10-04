using System;
using R3;

public sealed class MainMenuExit : IMainMenuExit, IDisposable
{
    private readonly ISaveService _saveService;
    private readonly Subject<MainMenuExitParams> _requested = new();

    public Observable<MainMenuExitParams> Requested => _requested;

    public MainMenuExit(ISaveService saveService)
    {
        _saveService = saveService;
    }

    public void StartGame() => Enter(Scenes.Gameplay);

    public void StartTestScene() => Enter(Scenes.Gameplay_Test);

    private void Enter(Scenes scene)
    {
        _requested.OnNext(new MainMenuExitParams(new GameplayEnterParams(_saveService.ActiveSlot, scene)));
    }

    public void Dispose() => _requested.Dispose();
}
