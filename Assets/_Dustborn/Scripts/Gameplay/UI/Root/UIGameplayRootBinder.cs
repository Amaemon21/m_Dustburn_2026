using UnityEngine;
using VContainer;

public class UIGameplayRootBinder : MonoBehaviour
{
    private const string MAIN_MENU_RESULT = "Left to the main menu";

    private IScreenService _screen;
    private IGameplayExit _exit;

    [Inject]
    public void Construct(IScreenService screen, IGameplayExit exit)
    {
        _screen = screen;
        _exit = exit;
    }

    public void HandleGoToMainMenuButtonClick()
    {
        _screen.CloseAll();
        _exit.ReturnToMainMenu(MAIN_MENU_RESULT);
    }
}
