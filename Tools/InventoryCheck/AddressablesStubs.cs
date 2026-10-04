#if DUSTBORN_HEADLESS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }

    internal sealed class StubOperation<T>
    {
        public T Result;
        public bool Valid = true;
        public string Key;
        public Exception Exception;
    }

    public struct AsyncOperationHandle<T>
    {
        internal StubOperation<T> Operation;
        public T Result => Operation.Result;
        public bool IsDone => true;
        public float PercentComplete => 1f;
        public AsyncOperationStatus Status => Operation.Exception == null
            ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed;
        public Exception OperationException => Operation.Exception;
        public bool IsValid() => Operation != null && Operation.Valid;
    }
}

namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;

    [Serializable]
    public class AssetReference
    {
        public object RuntimeKey { get; }
        public AssetReference(string guid) => RuntimeKey = guid;
    }

    [Serializable]
    public class AssetReferenceT<T> : AssetReference where T : UnityEngine.Object
    {
        public AssetReferenceT(string guid) : base(guid) { }
    }

    [Serializable]
    public class AssetReferenceGameObject : AssetReferenceT<GameObject>
    {
        public AssetReferenceGameObject(string guid) : base(guid) { }
    }

    public static class Addressables
    {
        public static int Loads;
        public static int Releases;
        public static int InstanceReleases;
        public static bool FailNext;
        public static readonly List<string> ReleaseOrder = new();
        public static object LastKey;

        public static AsyncOperationHandle<T> LoadAssetAsync<T>(object key)
        {
            Loads++;
            LastKey = key;
            StubOperation<T> operation = new() { Key = key?.ToString(),
                Exception = FailNext ? new InvalidOperationException("Stub load failed") : null };
            FailNext = false;
            return new AsyncOperationHandle<T> { Operation = operation };
        }

        public static AsyncOperationHandle<GameObject> InstantiateAsync(object key, Vector3 position,
            Quaternion rotation, Transform parent, bool trackHandle) => LoadAssetAsync<GameObject>(key);

        public static void Release<T>(AsyncOperationHandle<T> handle)
        {
            if (!handle.IsValid())
                throw new InvalidOperationException("Handle released twice");
            handle.Operation.Valid = false;
            Releases++;
            ReleaseOrder.Add(handle.Operation.Key);
        }

        public static bool ReleaseInstance(AsyncOperationHandle<GameObject> handle)
        {
            InstanceReleases++;
            Release(handle);
            return true;
        }
    }
}
#endif
