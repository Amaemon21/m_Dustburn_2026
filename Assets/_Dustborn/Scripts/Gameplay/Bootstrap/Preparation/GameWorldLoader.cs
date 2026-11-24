using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameWorldLoader : IDisposable
{
    private static string ReadingStatus => LocalizationService.Text("loading.readWorld");
    private static string AssemblingStatus => LocalizationService.Text("loading.prepareWorld");

    private readonly WorldGenerator _generator;
    private readonly WorldSaveRepository _worldSave;
    private readonly WorldCatalog _catalog;
    private WorldData _world;
    private bool _disposed;

    public GameWorldLoader(WorldGenerator generator, WorldSaveRepository worldSave, WorldCatalog catalog)
    {
        _generator = generator;
        _worldSave = worldSave;
        _catalog = catalog;
    }

    public async UniTask<WorldData> LoadAsync(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        ThrowIfUnavailable(token);
        string id = _worldSave.Origin.WorldId ?? WorldInfo.SCENE_WORLD_ID;

        if (id == WorldInfo.SCENE_WORLD_ID)
            return _world = WorldData.FromBaked(_generator.World);

        WorldInfo info = await _catalog.FindAsync(id, token) ?? throw new WorldMissingException(id);

        ThrowIfUnavailable(token);
        progress.Report(new SceneLoadStatus(0f, ReadingStatus));
        WorldDiskContent content = await UniTask.RunOnThreadPool(() => WorldDiskFormat.Read(info.Directory), cancellationToken: token);

        ThrowIfUnavailable(token);
        progress.Report(new SceneLoadStatus(0f, AssemblingStatus));
        var loader = new WorldDiskLoader(_generator.Settings, GroundTemplate());
        return _world = loader.Assemble(content);
    }

    private void ThrowIfUnavailable(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (_disposed)
            throw new ObjectDisposedException(nameof(GameWorldLoader));
    }

    private Material GroundTemplate()
    {
        GroundLayerSet ground = _generator.Settings.Ground;
        if (ground != null && ground.IsValid)
            return ground.Material;

        return _generator.World == null ? null : _generator.World.Material;
    }

    public void Dispose()
    {
        _disposed = true;
        _world?.Dispose();
        _world = null;
    }
}
