using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;

public sealed class GameplayFlow : ISceneFlow
{
    private const float SCENE_PROGRESS = 0.5f;
    private const string ASSET_STATUS = "Загрузка ресурсов игры";

    private readonly SceneTransition _transition;
    private readonly GameplayAssetLoader _assets;
    private readonly ISaveService _saveService;

    public GameplayFlow(SceneTransition transition, GameplayAssetLoader assets, ISaveService saveService)
    {
        _transition = transition;
        _assets = assets;
        _saveService = saveService;
    }

    public bool Handles(Scenes scene) => scene == Scenes.Gameplay || scene == Scenes.Gameplay_Test;

    public SceneEnterParams DirectEnter(Scenes scene) => new GameplayEnterParams(SaveService.DEFAULT_SLOT, scene);

    public async UniTask<SceneEnterParams> Run(SceneEnterParams enterParams, CancellationToken token)
    {
        GameplayEnterParams gameplay = enterParams.As<GameplayEnterParams>();
        float startTime = _transition.BeginLoading();

        _saveService.SetActiveSlot(gameplay.SlotId);
        await _saveService.LoadScopeAsync(SaveScope.Slot, token);

        GameplayEntryPoint entryPoint = await _transition.LoadScene<GameplayEntryPoint, GameplayEnterParams>(
            gameplay, _assets, ASSET_STATUS, SCENE_PROGRESS, token);

        Observable<GameplayExitParams> exit = await entryPoint.Run(_transition.StatusBetween(SCENE_PROGRESS, 1f), token);
        Task<GameplayExitParams> exitSignal = exit.FirstOrDefaultAsync(cancellationToken: token);

        await _transition.EndLoading(startTime, token);

        GameplayExitParams exitParams = await exitSignal;

        return exitParams?.TargetSceneEnterParams;
    }
}
