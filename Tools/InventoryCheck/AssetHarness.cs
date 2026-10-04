using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

internal static class AssetHarness
{
    private static int _checks;

    private static async Task Main()
    {
        AddressableAssetService assets = new();
        AddressableAsset<Sprite> first = await assets.LoadAsync<Sprite>("icon", CancellationToken.None);
        AddressableAsset<Sprite> second = await assets.LoadAsync<Sprite>("icon", CancellationToken.None);
        first.Dispose();
        Check(Addressables.Releases == 1, "first request releases its handle");
        first.Dispose();
        Check(Addressables.Releases == 1, "repeated disposal does not release twice");
        second.Dispose();
        Check(Addressables.Releases == 2, "second request owns a separate handle");

        int releases = Addressables.Releases;
        Addressables.FailNext = true;
        await Expect<InvalidOperationException>(async () => await assets.LoadAsync<Sprite>("bad", CancellationToken.None));
        Check(Addressables.Releases == releases + 1, "failed load releases handle");

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        int loads = Addressables.Loads;
        await Expect<OperationCanceledException>(async () => await assets.LoadAsync<Sprite>("cancel", cancellation.Token));
        Check(Addressables.Loads == loads, "pre-cancelled load does not start an operation");

        using (AddressableAsset<Sprite> reference = await assets.LoadAsync<Sprite>(new AssetReference("guid"), CancellationToken.None))
            Check(Equals(Addressables.LastKey, "guid"), "asset references resolve their runtime key");

        AddressableInstance instance = await assets.InstantiateAsync("prefab", default, default, CancellationToken.None);
        instance.Dispose();
        instance.Dispose();
        Check(Addressables.InstanceReleases == 1, "instances use ReleaseInstance exactly once");
        int instanceReleases = Addressables.InstanceReleases;
        Addressables.FailNext = true;
        await Expect<InvalidOperationException>(async () => await assets.InstantiateAsync("bad-prefab", default, default, CancellationToken.None));
        Check(Addressables.InstanceReleases == instanceReleases, "failed instantiation releases the operation handle");

        Addressables.ReleaseOrder.Clear();
        using (AssetScope scope = new(assets))
        {
            await scope.LoadAsync<Sprite>("sprite", CancellationToken.None);
            await scope.InstantiateAsync("object", default, default, CancellationToken.None);
        }
        Check(Addressables.ReleaseOrder[0] == "object" && Addressables.ReleaseOrder[1] == "sprite",
            "scope releases instances before their preceding dependencies");

        DelayedAssetService delayed = new();
        AssetScope disposedScope = new(delayed);
        UniTask<Sprite> pending = disposedScope.LoadAsync<Sprite>("late", CancellationToken.None);
        disposedScope.Dispose();
        releases = Addressables.Releases;
        delayed.Complete();
        await Expect<ObjectDisposedException>(async () => await pending);
        Check(Addressables.Releases == releases + 1, "late load is released if its owner was disposed");
        await VerifySceneAssets(assets);
        Console.WriteLine($"PASS: {_checks} asset ownership checks with Addressables stubs");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        _checks++;
    }

    private static async Task VerifySceneAssets(IAssetService assets)
    {
        TestSceneLoader loader = new(assets);
        int loads = Addressables.Loads;
        await loader.LoadAsync(CancellationToken.None);
        object first = loader.Current;
        await loader.LoadAsync(CancellationToken.None);
        Check(ReferenceEquals(first, loader.Current) && Addressables.Loads == loads + 1,
            "scene assets are reused during their scene lifetime");
        int releases = Addressables.Releases;
        loader.Unload();
        Check(loader.Current == null && Addressables.Releases == releases + 1,
            "scene unload clears loaded data and releases handles");
        await loader.LoadAsync(CancellationToken.None);
        Check(!ReferenceEquals(first, loader.Current), "returning to a scene creates fresh asset ownership");
        loader.Dispose();
        await Expect<ObjectDisposedException>(async () => await loader.LoadAsync(CancellationToken.None));

        using TestSceneLoader failing = new(assets);
        releases = Addressables.Releases;
        Addressables.FailNext = true;
        await Expect<InvalidOperationException>(async () => await failing.LoadAsync(CancellationToken.None));
        Check(failing.Current == null && Addressables.Releases == releases + 1,
            "scene loading failure leaves no loaded data or retained handles");
    }

    private sealed class TestSceneLoader : SceneAssetLoader<object>
    {
        public TestSceneLoader(IAssetService assets) : base(assets) { }
        protected override async UniTask<object> LoadAssetsAsync(AssetScope scope, CancellationToken token)
        {
            await scope.LoadAsync<Sprite>("scene", token);
            return new object();
        }
    }

    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private sealed class DelayedAssetService : IAssetService
    {
        private readonly UniTaskCompletionSource<bool> _ready = new();
        public void Complete() => _ready.TrySetResult(true);
        public async UniTask<AddressableAsset<T>> LoadAsync<T>(object key, CancellationToken token,
            IProgress<float> progress = null) where T : UnityEngine.Object
        {
            await _ready.Task;
            return new AddressableAsset<T>(Addressables.LoadAssetAsync<T>(key));
        }

        public UniTask<AddressableInstance> InstantiateAsync(object key, Vector3 position,
            Quaternion rotation, CancellationToken token, Transform parent = null, IProgress<float> progress = null)
            => throw new NotSupportedException();
    }
}
