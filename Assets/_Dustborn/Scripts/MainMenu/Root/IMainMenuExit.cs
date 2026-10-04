using R3;

public interface IMainMenuExit
{
    Observable<MainMenuExitParams> Requested { get; }
    void StartGame();
    void StartTestScene();
}
