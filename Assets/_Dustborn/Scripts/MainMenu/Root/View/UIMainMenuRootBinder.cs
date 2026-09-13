using R3;
using UnityEngine;

public class UIMainMenuRootBinder : MonoBehaviour
{
    private Subject<Unit> _exitSceneSignalSubject;
        
    public void Bind(Subject<Unit> exitSceneSignalSubject)
    {
        _exitSceneSignalSubject = exitSceneSignalSubject;
    }
        
    public void HandleGoToGameplayButtonClick()
    {
        _exitSceneSignalSubject.OnNext(Unit.Default);
    }
}