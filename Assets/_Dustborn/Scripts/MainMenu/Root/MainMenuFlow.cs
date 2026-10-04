using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;

public sealed class MainMenuFlow : ISceneFlow
{
    private const string ASSET_STATUS = "Загрузка главного меню";

    private readonly SceneTransition _transition;
    private readonly MainMenuAssetLoader _assets;

    public MainMenuFlow(SceneTransition transition, MainMenuAssetLoader assets)
    {
        _transition = transition;
        _assets = assets;
    }

    public bool Handles(Scenes scene) => scene == Scenes.MainMenu;

    public SceneEnterParams DirectEnter(Scenes scene) => new MainMenuEnterParams(string.Empty);

    public async UniTask<SceneEnterParams> Run(SceneEnterParams enterParams, CancellationToken token)
    {
        float startTime = _transition.BeginLoading();

        MainMenuEntryPoint entryPoint = await _transition.LoadScene<MainMenuEntryPoint, MainMenuEnterParams>(
            enterParams.As<MainMenuEnterParams>(), _assets, ASSET_STATUS, 1f, token);

        Task<MainMenuExitParams> exitSignal = entryPoint.Run().FirstOrDefaultAsync(cancellationToken: token);

        await _transition.EndLoading(startTime, token);

        MainMenuExitParams exitParams = await exitSignal;

        return exitParams?.TargetSceneEnterParams;
    }
}
