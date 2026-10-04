using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class UIRootService : IDisposable
{
    private readonly AssetScope _scope;
    private readonly ProjectAssetReferences _references;
    private readonly IObjectResolver _resolver;
    public UIRootView Current { get; private set; }

    public UIRootService(IAssetService assets, ProjectAssetReferences references, IObjectResolver resolver)
    {
        _scope = new AssetScope(assets);
        _references = references;
        _resolver = resolver;
    }

    public async UniTask<UIRootView> CreateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Current != null)
            return Current;
        GameObject instance = await _scope.InstantiateAsync(
            _references.UIRootPrefab, Vector3.zero, Quaternion.identity, token);
        _resolver.InjectGameObject(instance);
        UnityEngine.Object.DontDestroyOnLoad(instance);
        Current = instance.GetComponent<UIRootView>();
        return Current;
    }

    public void Dispose()
    {
        _scope.Dispose();
        Current = null;
    }
}
