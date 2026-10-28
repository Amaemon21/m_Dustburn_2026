using R3;

public interface IMainMenuExit
{
    Observable<MainMenuExitParams> Requested { get; }
    void NewGame(string slotId, string name, string worldId);
    void LoadGame(string slotId);
    void StartTestScene();
    void Quit();
}
