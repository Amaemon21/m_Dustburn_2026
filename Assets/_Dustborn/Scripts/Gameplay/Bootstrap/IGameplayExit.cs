using R3;

public interface IGameplayExit
{
    Observable<GameplayExitParams> Requested { get; }
    void ReturnToMainMenu(string result);
}
