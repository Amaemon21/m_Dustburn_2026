using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;

public abstract class SceneAssetLoader<T> : ISceneAssetLoader, IDisposable where T : class
{
    private readonly IAssetService _assets;
    private AssetScope _scope;
    private bool _disposed;
    public T Current { get; private set; }

    protected SceneAssetLoader(IAssetService assets) => _assets = assets;

    public async UniTask LoadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Current != null)
            return;

        if (_scope != null)
            throw new InvalidOperationException($"{GetType().Name}: assets are already loading");

        AssetScope scope = new(_assets);
        _scope = scope;

        try
        {
            T loaded = await LoadAssetsAsync(scope, token);
            token.ThrowIfCancellationRequested();

            if (!ReferenceEquals(_scope, scope))
                throw new OperationCanceledException("The scene asset load was unloaded");

            Current = loaded ?? throw new InvalidOperationException($"{GetType().Name}: asset load returned no data");
        }
        catch
        {
            if (ReferenceEquals(_scope, scope))
                Unload();

            throw;
        }
    }

    protected abstract UniTask<T> LoadAssetsAsync(AssetScope scope, CancellationToken token);

    public void Register(IContainerBuilder builder)
    {
        if (Current == null)
            throw new InvalidOperationException($"{GetType().Name}: assets are registered before they are loaded");

        builder.RegisterInstance(Current);
    }

    public void Unload()
    {
        AssetScope scope = _scope;
        _scope = null;
        Current = null;
        scope?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        Unload();
    }
}
