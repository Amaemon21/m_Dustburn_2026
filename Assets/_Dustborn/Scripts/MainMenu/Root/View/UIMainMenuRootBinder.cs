using UnityEngine;
using VContainer;

public class UIMainMenuRootBinder : MonoBehaviour
{
    private IMainMenuExit _exit;

    [Inject]
    public void Construct(IMainMenuExit exit)
    {
        _exit = exit;
    }

    public void HandleGoToGameplayButtonClick()
    {
        _exit.StartGame();
    }

    public void HandleGoToTestSceneButtonClick()
    {
        _exit.StartTestScene();
    }
}
