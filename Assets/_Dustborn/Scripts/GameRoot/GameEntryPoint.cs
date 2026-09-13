using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public class GameEntryPoint : IAsyncStartable
{
    private const float BOOT_PROGRESS = 0.1f;
    private const float SCENE_PROGRESS = 0.5f;

    private readonly UIRootView _uiRoot;
    private readonly SceneLoader _sceneLoader;
    private readonly ISaveService _saveService;

    public GameEntryPoint(UIRootView uiRoot, SceneLoader sceneLoader, ISaveService saveService)
    {
        _uiRoot = uiRoot;
        _sceneLoader = sceneLoader;
        _saveService = saveService;
    }

    public async UniTask StartAsync(CancellationToken token)
    {
        try
        {
            await _saveService.LoadScopeAsync(SaveScope.Global, token);

            SceneEnterParams next = FirstScene();

            while (next != null)
                next = await EnterScene(next, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private SceneEnterParams FirstScene()
    {
#if UNITY_EDITOR
        string activeScene = SceneManager.GetActiveScene().name;

        if (activeScene == Scenes.GAMEPLAY)
            return new GameplayEnterParams(SaveService.DEFAULT_SLOT);

        if (activeScene == Scenes.MAINMENU)
            return new MainMenuEnterParams(string.Empty);

        if (activeScene != Scenes.BOOT)
        {
            Debug.Log($"{nameof(GameEntryPoint)}: scene '{activeScene}' is outside the game flow, autostart skipped");
            return null;
        }
#endif
        return new MainMenuEnterParams(string.Empty);
    }

    private async UniTask<SceneEnterParams> EnterScene(SceneEnterParams enterParams, CancellationToken token)
    {
        if (enterParams.SceneName == Scenes.MAINMENU)
            return await RunMainMenu(enterParams.As<MainMenuEnterParams>(), token);

        if (enterParams.SceneName == Scenes.GAMEPLAY)
            return await RunGameplay(enterParams.As<GameplayEnterParams>(), token);

        Debug.LogError($"{nameof(GameEntryPoint)}: no flow registered for scene '{enterParams.SceneName}'");
        return null;
    }

    private async UniTask<SceneEnterParams> RunMainMenu(MainMenuEnterParams enterParams, CancellationToken token)
    {
        float startTime = Time.realtimeSinceStartup;
        _uiRoot.ShowLoadingScreen();

        MainMenuEntryPoint entryPoint = await LoadScene<MainMenuEntryPoint, MainMenuEnterParams>(Scenes.MAINMENU, enterParams, 1f, token);

        if (entryPoint == null)
            return null;

        Task<MainMenuExitParams> exitSignal = entryPoint.Run(enterParams).FirstOrDefaultAsync(cancellationToken: token);

        await HideLoadingScreen(startTime, token);

        MainMenuExitParams exitParams = await exitSignal;

        return exitParams?.TargetSceneEnterParams;
    }

    private async UniTask<SceneEnterParams> RunGameplay(GameplayEnterParams enterParams, CancellationToken token)
    {
        float startTime = Time.realtimeSinceStartup;
        _uiRoot.ShowLoadingScreen();

        _saveService.SetActiveSlot(enterParams.SlotId);
        await _saveService.LoadScopeAsync(SaveScope.Slot, token);

        GameplayEntryPoint entryPoint = await LoadScene<GameplayEntryPoint, GameplayEnterParams>(Scenes.GAMEPLAY, enterParams, SCENE_PROGRESS, token);

        if (entryPoint == null)
            return null;

        Observable<GameplayExitParams> exit = await entryPoint.Run(enterParams, WorldProgress(), token);
        Task<GameplayExitParams> exitSignal = exit.FirstOrDefaultAsync(cancellationToken: token);

        await HideLoadingScreen(startTime, token);

        GameplayExitParams exitParams = await exitSignal;

        return exitParams?.TargetSceneEnterParams;
    }

    private async UniTask<TEntryPoint> LoadScene<TEntryPoint, TParams>(
        string sceneName, TParams enterParams, float progressCeiling, CancellationToken token)
        where TEntryPoint : MonoBehaviour
        where TParams : SceneEnterParams
    {
        _uiRoot.ClearSceneUI();

        await _sceneLoader.LoadAsync(Scenes.BOOT, ProgressRange(0f, BOOT_PROGRESS), token);

        IObjectResolver sceneContainer = null;

        using (LifetimeScope.Enqueue(builder =>
               {
                   builder.RegisterInstance(enterParams);
                   builder.RegisterBuildCallback(container => sceneContainer = container);
               }))
        {
            await _sceneLoader.LoadAsync(sceneName, ProgressRange(BOOT_PROGRESS, progressCeiling), token);
        }

        if (sceneContainer == null)
        {
            Debug.LogError($"{nameof(GameEntryPoint)}: scene '{sceneName}' has no {nameof(LifetimeScope)}, so nothing was injected");
            return null;
        }

        try
        {
            return sceneContainer.Resolve<TEntryPoint>();
        }
        catch (VContainerException exception)
        {
            Debug.LogError($"{nameof(GameEntryPoint)}: scene '{sceneName}' does not register {typeof(TEntryPoint).Name}: {exception.Message}");
            return null;
        }
    }

    private async UniTask HideLoadingScreen(float startTime, CancellationToken token)
    {
        _uiRoot.SetProgress(1f);
        _uiRoot.SetStatus(string.Empty);

        float remaining = _uiRoot.MinLoadingScreenTime - (Time.realtimeSinceStartup - startTime);

        if (remaining > 0f)
            await UniTask.WaitForSeconds(remaining, true, cancellationToken: token);

        _uiRoot.HideLoadingScreen();
    }

    private IProgress<SceneLoadStatus> WorldProgress()
    {
        return Progress.Create<SceneLoadStatus>(status =>
        {
            _uiRoot.SetProgress(Mathf.Lerp(SCENE_PROGRESS, 1f, status.Progress));
            _uiRoot.SetStatus(status.Status);
        });
    }

    private IProgress<float> ProgressRange(float from, float to)
    {
        return Progress.Create<float>(value => _uiRoot.SetProgress(Mathf.Lerp(from, to, value)));
    }
}
