using System;
using R3;

public sealed class GameplayExit : IGameplayExit, IDisposable
{
    private readonly Subject<GameplayExitParams> _requested = new();

    public Observable<GameplayExitParams> Requested => _requested;

    public void ReturnToMainMenu(string result)
    {
        _requested.OnNext(new GameplayExitParams(new MainMenuEnterParams(result)));
    }

    public void Dispose() => _requested.Dispose();
}
