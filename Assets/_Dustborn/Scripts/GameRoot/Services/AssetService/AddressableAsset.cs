using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

public sealed class AddressableAsset<T> : IDisposable where T : Object
{
    private AsyncOperationHandle<T> _handle;
    public T Asset => _handle.Result;

    internal AddressableAsset(AsyncOperationHandle<T> handle) => _handle = handle;

    public void Dispose()
    {
        if (!_handle.IsValid())
            return;
        Addressables.Release(_handle);
        _handle = default;
    }
}
