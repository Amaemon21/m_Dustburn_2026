using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

public sealed class AddressableAssetService : IAssetService
{
    public async UniTask<AddressableAsset<T>> LoadAsync<T>(object key, CancellationToken token,
        IProgress<float> progress = null) where T : Object
    {
        token.ThrowIfCancellationRequested();
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(RuntimeKey(key));
        try
        {
            await AwaitAsync(handle, token, progress);
            return new AddressableAsset<T>(handle);
        }
        catch
        {
            Addressables.Release(handle);
            throw;
        }
    }

    public async UniTask<AddressableInstance> InstantiateAsync(object key, Vector3 position,
        Quaternion rotation, CancellationToken token, Transform parent = null, IProgress<float> progress = null)
    {
        token.ThrowIfCancellationRequested();
        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
            RuntimeKey(key), position, rotation, parent, true);
        try
        {
            await AwaitAsync(handle, token, progress);
            return new AddressableInstance(handle);
        }
        catch
        {
            ReleaseInstanceAsync(handle, CancellationToken.None).Forget();
            throw;
        }
    }

    private static object RuntimeKey(object key) => key is AssetReference reference ? reference.RuntimeKey : key;

    private static async UniTask AwaitAsync<T>(AsyncOperationHandle<T> handle, CancellationToken token,
        IProgress<float> progress)
    {
        token.ThrowIfCancellationRequested();
        while (!handle.IsDone)
        {
            progress?.Report(handle.PercentComplete);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        token.ThrowIfCancellationRequested();
        if (handle.Status != AsyncOperationStatus.Succeeded)
            throw handle.OperationException ?? new InvalidOperationException("Addressable operation failed");
        progress?.Report(1f);
    }

    private static async UniTask ReleaseInstanceAsync(AsyncOperationHandle<GameObject> handle, CancellationToken token)
    {
        while (!handle.IsDone)
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        if (handle.Status == AsyncOperationStatus.Succeeded)
            Addressables.ReleaseInstance(handle);
        else
            Addressables.Release(handle);
    }
}
