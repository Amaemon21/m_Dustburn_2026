using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class MainMenuEntryPoint
{
    private readonly MainMenuEnterParams _enterParams;
    private readonly MainMenuAssets _assets;
    private readonly UIRootView _uiRoot;
    private readonly IMainMenuExit _exit;
    private readonly GameSlotCatalog _slots;
    private readonly MainMenuBackInput _backInput;
    private readonly IObjectResolver _resolver;

    public MainMenuEntryPoint(MainMenuEnterParams enterParams, MainMenuAssets assets, UIRootView uiRoot,
        IMainMenuExit exit, GameSlotCatalog slots, MainMenuBackInput backInput, IObjectResolver resolver)
    {
        _enterParams = enterParams;
        _assets = assets;
        _uiRoot = uiRoot;
        _exit = exit;
        _slots = slots;
        _backInput = backInput;
        _resolver = resolver;
    }

    public Observable<MainMenuExitParams> Run()
    {
        UIMainMenuRootBinder ui = _resolver.Instantiate(_assets.UIPrefab);
        _uiRoot.AttachSceneUI(ui.gameObject);
        _slots.RefreshAsync().Forget();
        _backInput.Activate();

        Debug.Log($"{nameof(MainMenuEntryPoint)}: result of the previous session: '{_enterParams.Result}'");

        return _exit.Requested;
    }
}
