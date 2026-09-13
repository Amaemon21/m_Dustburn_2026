using R3;
using UnityEngine;
using VContainer;

public class MainMenuEntryPoint : MonoBehaviour
{
    [SerializeField] private UIMainMenuRootBinder _sceneUIRootPrefab;

    private UIRootView _uiRootView;
    private ISaveService _saveService;

    private Subject<Unit> _exitSceneSignal;

    [Inject]
    public void Construct(UIRootView uiRootView, ISaveService saveService)
    {
        _uiRootView = uiRootView;
        _saveService = saveService;
    }

    public Observable<MainMenuExitParams> Run(MainMenuEnterParams enterParams)
    {
        UIMainMenuRootBinder uiScene = Instantiate(_sceneUIRootPrefab);
        _uiRootView.AttachSceneUI(uiScene.gameObject);

        _exitSceneSignal = new Subject<Unit>();
        uiScene.Bind(_exitSceneSignal);

        Debug.Log($"{nameof(MainMenuEntryPoint)}: result of the previous session: '{enterParams.Result}'");

        GameplayEnterParams gameplayEnterParams = new GameplayEnterParams(_saveService.ActiveSlot);
        MainMenuExitParams exitParams = new MainMenuExitParams(gameplayEnterParams);

        return _exitSceneSignal.Select(_ => exitParams);
    }

    private void OnDestroy()
    {
        _exitSceneSignal?.Dispose();
    }
}
