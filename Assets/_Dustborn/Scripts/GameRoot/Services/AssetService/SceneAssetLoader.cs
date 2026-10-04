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
        AssetScope scope = new(_assets);
        _scope = scope;
        try
        {
            Current = await LoadAssetsAsync(scope, token);
        }
        catch
        {
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
        _scope?.Dispose();
        _scope = null;
        Current = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Unload();
    }
}
