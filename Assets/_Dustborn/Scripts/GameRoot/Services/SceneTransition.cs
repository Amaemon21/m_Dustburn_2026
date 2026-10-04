using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class SceneTransition
{
    private const float BOOT_PROGRESS = 0.1f;

    private readonly UIRootService _uiRoot;
    private readonly SceneLoader _sceneLoader;
    private readonly IReadOnlyList<ISceneAssetLoader> _sceneAssets;

    public UIRootView UI => _uiRoot.Current;

    public SceneTransition(UIRootService uiRoot, SceneLoader sceneLoader, IReadOnlyList<ISceneAssetLoader> sceneAssets)
    {
        _uiRoot = uiRoot;
        _sceneLoader = sceneLoader;
        _sceneAssets = sceneAssets;
    }

    public float BeginLoading()
    {
        UI.ShowLoadingScreen();
        return Time.realtimeSinceStartup;
    }

    public async UniTask<TEntryPoint> LoadScene<TEntryPoint, TParams>(TParams enterParams, ISceneAssetLoader assets,
        string assetStatus, float progressCeiling, CancellationToken token)
        where TParams : SceneEnterParams
    {
        UI.ClearSceneUI();

        await _sceneLoader.LoadAsync(Scenes.Boot, ProgressBetween(0f, BOOT_PROGRESS), token);

        foreach (ISceneAssetLoader loaded in _sceneAssets)
            loaded.Unload();

        UI.SetStatus(assetStatus);
        await assets.LoadAsync(token);

        IObjectResolver sceneContainer = null;

        using (LifetimeScope.Enqueue(builder =>
               {
                   builder.RegisterInstance(enterParams);
                   builder.RegisterInstance(UI);
                   assets.Register(builder);
                   builder.RegisterBuildCallback(container => sceneContainer = container);
               }))
        {
            await _sceneLoader.LoadAsync(enterParams.Scene, ProgressBetween(BOOT_PROGRESS, progressCeiling), token);
        }

        if (sceneContainer == null)
            throw new InvalidOperationException($"{nameof(SceneTransition)}: scene '{enterParams.SceneName}' did not finish building its container. Check earlier exceptions and the scene LifetimeScope");

        try
        {
            return sceneContainer.Resolve<TEntryPoint>();
        }
        catch (VContainerException exception)
        {
            throw new InvalidOperationException($"{nameof(SceneTransition)}: scene '{enterParams.SceneName}' does not register {typeof(TEntryPoint).Name}", exception);
        }
    }

    public async UniTask EndLoading(float startTime, CancellationToken token)
    {
        UI.SetProgress(1f);
        UI.SetStatus(string.Empty);

        float remaining = UI.MinLoadingScreenTime - (Time.realtimeSinceStartup - startTime);

        if (remaining > 0f)
            await UniTask.WaitForSeconds(remaining, true, cancellationToken: token);

        UI.HideLoadingScreen();
    }

    public IProgress<SceneLoadStatus> StatusBetween(float from, float to)
    {
        return Progress.Create<SceneLoadStatus>(status =>
        {
            UI.SetProgress(Mathf.Lerp(from, to, status.Progress));
            UI.SetStatus(status.Status);
        });
    }

    private IProgress<float> ProgressBetween(float from, float to)
    {
        return Progress.Create<float>(value => UI.SetProgress(Mathf.Lerp(from, to, value)));
    }
}
