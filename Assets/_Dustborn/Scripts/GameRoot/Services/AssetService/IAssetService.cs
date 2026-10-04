using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public interface IAssetService
{
    UniTask<AddressableAsset<T>> LoadAsync<T>(object key, CancellationToken token,
        IProgress<float> progress = null) where T : Object;
    UniTask<AddressableInstance> InstantiateAsync(object key, Vector3 position, Quaternion rotation,
        CancellationToken token, Transform parent = null, IProgress<float> progress = null);
}
