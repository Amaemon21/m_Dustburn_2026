using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using VContainer;
using VContainer.Unity;

public sealed class GameplayEntryPoint
{
    private readonly IGameplayPreparation _preparation;
    private readonly IReadOnlyList<IGameplayActivatable> _activatables;
    private readonly IGameplayExit _exit;
    private readonly GameplayAssets _assets;
    private readonly UIRootView _uiRoot;
    private readonly IObjectResolver _resolver;

    public GameplayEntryPoint(IGameplayPreparation preparation, IReadOnlyList<IGameplayActivatable> activatables,
        IGameplayExit exit, GameplayAssets assets, UIRootView uiRoot, IObjectResolver resolver)
    {
        _preparation = preparation;
        _activatables = activatables;
        _exit = exit;
        _assets = assets;
        _uiRoot = uiRoot;
        _resolver = resolver;
    }

    public async UniTask<Observable<GameplayExitParams>> Run(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        await _preparation.Prepare(progress, token);

        token.ThrowIfCancellationRequested();

        UIGameplayRootBinder ui = _resolver.Instantiate(_assets.UIPrefab);
        _uiRoot.AttachSceneUI(ui.gameObject);

        foreach (IGameplayActivatable activatable in _activatables)
            activatable.Activate();

        progress.Report(new SceneLoadStatus(1f, string.Empty));

        return _exit.Requested;
    }
}
