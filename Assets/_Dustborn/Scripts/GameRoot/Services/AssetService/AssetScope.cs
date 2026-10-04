using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AssetScope : IDisposable
{
    private readonly IAssetService _assets;
    private readonly List<IDisposable> _resources = new();
    private bool _disposed;

    public AssetScope(IAssetService assets) => _assets = assets;

    public async UniTask<T> LoadAsync<T>(object key, CancellationToken token,
        IProgress<float> progress = null) where T : Object
    {
        AddressableAsset<T> resource = await _assets.LoadAsync<T>(key, token, progress);
        Track(resource);
        return resource.Asset;
    }

    public async UniTask<GameObject> InstantiateAsync(object key, Vector3 position, Quaternion rotation,
        CancellationToken token, Transform parent = null, IProgress<float> progress = null)
    {
        AddressableInstance resource = await _assets.InstantiateAsync(key, position, rotation, token, parent, progress);
        Track(resource);
        return resource.Instance;
    }

    private void Track(IDisposable resource)
    {
        if (_disposed)
        {
            resource.Dispose();
            throw new ObjectDisposedException(nameof(AssetScope));
        }
        _resources.Add(resource);
    }

    public void Dispose()
    {
        _disposed = true;
        for (int i = _resources.Count - 1; i >= 0; i--)
            _resources[i].Dispose();
        _resources.Clear();
    }
}
