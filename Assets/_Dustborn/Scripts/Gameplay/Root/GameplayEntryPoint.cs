using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;

public class GameplayEntryPoint : MonoBehaviour
{
    private const float WORLD_WAIT_TIMEOUT = 180f;

    [SerializeField] private UIGameplayRootBinder _sceneUIRootPrefab;

    private UIRootView _uiRootView;
    private ISaveService _saveService;
    private WorldGenerator _world;
    private PlayerSpawnService _playerSpawn;

    private Subject<Unit> _exitSceneSignal;

    [Inject]
    public void Construct(UIRootView uiRootView, ISaveService saveService, WorldGenerator world, PlayerSpawnService playerSpawn)
    {
        _uiRootView = uiRootView;
        _saveService = saveService;
        _world = world;
        _playerSpawn = playerSpawn;
    }

    public async UniTask<Observable<GameplayExitParams>> Run(
        GameplayEnterParams enterParams, IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        await BuildWorld(progress, token);

        UIGameplayRootBinder uiScene = Instantiate(_sceneUIRootPrefab);
        _uiRootView.AttachSceneUI(uiScene.gameObject);

        _exitSceneSignal = new Subject<Unit>();
        uiScene.Bind(_exitSceneSignal);

        MainMenuEnterParams mainMenuEnterParams = new MainMenuEnterParams("Fatality");
        GameplayExitParams exitParams = new GameplayExitParams(mainMenuEnterParams);

        return _exitSceneSignal.Select(_ => exitParams);
    }

    private async UniTask BuildWorld(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        Transform player = _playerSpawn.Spawn();

        _world.SetViewer(player);
        _world.LoadWorld();

        float deadline = Time.realtimeSinceStartup + WORLD_WAIT_TIMEOUT;

        while (!_world.Ready)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError($"{nameof(GameplayEntryPoint)}: the world never reported Ready, entering the scene anyway", _world);
                break;
            }

            progress.Report(new SceneLoadStatus(_world.Progress, _world.Status));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        progress.Report(new SceneLoadStatus(1f, string.Empty));

        _playerSpawn.SetControlEnabled(true);
    }

    private void OnDestroy()
    {
        _exitSceneSignal?.Dispose();
    }
}
