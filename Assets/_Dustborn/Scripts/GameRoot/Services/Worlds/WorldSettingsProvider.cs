using System;
using System.Threading;
using Cysharp.Threading.Tasks;

public sealed class WorldSettingsProvider
{
    private readonly IAssetService _assets;
    private readonly ProjectAssetReferences _references;

    public WorldSettingsProvider(IAssetService assets, ProjectAssetReferences references)
    {
        _assets = assets;
        _references = references;
    }

    public UniTask<AddressableAsset<WorldBuildSettings>> LoadAsync(CancellationToken token)
    {
        if (_references.WorldBuildSettings == null || !_references.WorldBuildSettings.RuntimeKeyIsValid())
            throw new InvalidOperationException($"Assign {nameof(ProjectAssetReferences.WorldBuildSettings)} on the ProjectLifetimeScope prefab; the world generator needs it");

        return _assets.LoadAsync<WorldBuildSettings>(_references.WorldBuildSettings, token);
    }
}
