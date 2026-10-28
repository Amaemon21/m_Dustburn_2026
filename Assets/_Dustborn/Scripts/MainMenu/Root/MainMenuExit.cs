using System;
using R3;
using UnityEngine;

public sealed class MainMenuExit : IMainMenuExit, IDisposable
{
    private readonly Subject<MainMenuExitParams> _requested = new();

    public Observable<MainMenuExitParams> Requested => _requested;

    public void NewGame(string slotId, string name, string worldId) => Enter(GameplayEnterParams.NewGame(slotId, name, worldId));

    public void LoadGame(string slotId) => Enter(new GameplayEnterParams(slotId));

    public void StartTestScene() => Enter(GameplayEnterParams.ForScene(Scenes.Gameplay_Test));

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Enter(GameplayEnterParams enterParams)
    {
        _requested.OnNext(new MainMenuExitParams(enterParams));
    }

    public void Dispose() => _requested.Dispose();
}
