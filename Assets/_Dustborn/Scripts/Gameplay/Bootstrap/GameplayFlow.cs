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
    private readonly GameSlotService _slots;

    public GameplayFlow(SceneTransition transition, GameplayAssetLoader assets, ISaveService saveService, GameSlotService slots)
    {
        _transition = transition;
        _assets = assets;
        _saveService = saveService;
        _slots = slots;
    }

    public bool Handles(Scenes scene) => scene == Scenes.Gameplay || scene == Scenes.Gameplay_Test;

    public SceneEnterParams DirectEnter(Scenes scene) => GameplayEnterParams.ForScene(scene);

    public async UniTask<SceneEnterParams> Run(SceneEnterParams enterParams, CancellationToken token)
    {
        GameplayEnterParams gameplay = enterParams.As<GameplayEnterParams>();
        float startTime = _transition.BeginLoading();

        _saveService.SetActiveSlot(gameplay.SlotId);
        await _saveService.LoadScopeAsync(SaveScope.Slot, token);

        if (gameplay.IsNewGame)
            _slots.BeginNewGame(gameplay.NewGameName, gameplay.NewGameWorldId);

        GameplayEntryPoint entryPoint = await _transition.LoadScene<GameplayEntryPoint, GameplayEnterParams>(
            gameplay, _assets, ASSET_STATUS, SCENE_PROGRESS, token);

        Observable<GameplayExitParams> exit = await entryPoint.Run(_transition.StatusBetween(SCENE_PROGRESS, 1f), token);
        Task<GameplayExitParams> exitSignal = exit.FirstOrDefaultAsync(cancellationToken: token);

        await _transition.EndLoading(startTime, token);

        GameplayExitParams exitParams = await exitSignal;

        return exitParams?.TargetSceneEnterParams;
    }
}
