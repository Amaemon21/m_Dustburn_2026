using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

public class GameEntryPoint : IAsyncStartable
{
    private readonly UIRootService _uiRoot;
    private readonly ISaveService _saveService;
    private readonly IReadOnlyList<ISceneFlow> _flows;

    public GameEntryPoint(UIRootService uiRoot, ISaveService saveService, IReadOnlyList<ISceneFlow> flows)
    {
        _uiRoot = uiRoot;
        _saveService = saveService;
        _flows = flows;
    }

    public async UniTask StartAsync(CancellationToken token)
    {
        try
        {
            await _uiRoot.CreateAsync(token);
            await _saveService.LoadScopeAsync(SaveScope.Global, token);

            SceneEnterParams next = FirstScene();

            while (next != null)
                next = await EnterSceneSafely(next, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private SceneEnterParams FirstScene()
    {
#if UNITY_EDITOR
        if (!PlayModeStartScene.TryGet(out Scenes requested))
        {
            Debug.Log($"{nameof(GameEntryPoint)}: scene '{SceneManager.GetActiveScene().name}' is outside the game flow, autostart skipped");
            return null;
        }

        if (requested != Scenes.Boot)
            return FlowFor(requested).DirectEnter(requested);
#endif
        return FlowFor(Scenes.MainMenu).DirectEnter(Scenes.MainMenu);
    }

    private async UniTask<SceneEnterParams> EnterSceneSafely(SceneEnterParams enterParams, CancellationToken token)
    {
        try
        {
            return await FlowFor(enterParams.Scene).Run(enterParams, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return Recover(enterParams, exception);
        }
    }

    private SceneEnterParams Recover(SceneEnterParams failed, Exception exception)
    {
        if (failed.Scene == Scenes.MainMenu)
        {
            Debug.LogError($"{nameof(GameEntryPoint)}: the main menu failed to start, the game flow stops here");
            _uiRoot.Current.SetStatus("The main menu could not be loaded. See the log for details.");
            return null;
        }

        Debug.LogError($"{nameof(GameEntryPoint)}: scene '{failed.SceneName}' failed to start, returning to the main menu");
        return new MainMenuEnterParams($"Failed to enter {failed.SceneName}: {exception.Message}");
    }

    private ISceneFlow FlowFor(Scenes scene)
    {
        foreach (ISceneFlow flow in _flows)
        {
            if (flow.Handles(scene))
                return flow;
        }

        throw new InvalidOperationException($"{nameof(GameEntryPoint)}: no {nameof(ISceneFlow)} handles scene '{scene}'");
    }
}
