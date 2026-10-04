using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public sealed class AddressableInstance : IDisposable
{
    private AsyncOperationHandle<GameObject> _handle;
    public GameObject Instance => _handle.Result;

    internal AddressableInstance(AsyncOperationHandle<GameObject> handle) => _handle = handle;

    public void Dispose()
    {
        if (!_handle.IsValid())
            return;
        Addressables.ReleaseInstance(_handle);
        _handle = default;
    }
}
