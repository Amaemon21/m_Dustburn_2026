using R3;
using UnityEngine;

public class UIGameplayRootBinder : MonoBehaviour
{
    private Subject<Unit> _exitSceneSignalSubject;

    public void Bind(Subject<Unit> exitSceneSignalSubject)
    {
        _exitSceneSignalSubject = exitSceneSignalSubject;
    }

    public void HandleGoToMainMenuButtonClick()
    {
        _exitSceneSignalSubject.OnNext(Unit.Default);
    }
}
