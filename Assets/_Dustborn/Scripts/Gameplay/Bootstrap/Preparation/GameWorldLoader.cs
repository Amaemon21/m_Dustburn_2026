using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameWorldLoader : IDisposable
{
    private const string READING_STATUS = "Чтение мира с диска";
    private const string ASSEMBLING_STATUS = "Подготовка мира";

    private readonly WorldGenerator _generator;
    private readonly WorldSaveRepository _worldSave;
    private readonly WorldCatalog _catalog;
    private WorldData _world;

    public GameWorldLoader(WorldGenerator generator, WorldSaveRepository worldSave, WorldCatalog catalog)
    {
        _generator = generator;
        _worldSave = worldSave;
        _catalog = catalog;
    }

    public async UniTask<WorldData> LoadAsync(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        string id = _worldSave.Origin.WorldId ?? WorldInfo.SCENE_WORLD_ID;

        if (id == WorldInfo.SCENE_WORLD_ID)
            return _world = WorldData.FromBaked(_generator.World);

        WorldInfo info = await _catalog.FindAsync(id, token) ?? throw new WorldMissingException(id);

        progress.Report(new SceneLoadStatus(0f, READING_STATUS));
        WorldDiskContent content = await UniTask.RunOnThreadPool(() => WorldDiskFormat.Read(info.Directory), cancellationToken: token);

        progress.Report(new SceneLoadStatus(0f, ASSEMBLING_STATUS));
        var loader = new WorldDiskLoader(_generator.Settings, GroundTemplate());
        return _world = loader.Assemble(content);
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
        _world?.Dispose();
        _world = null;
    }
}
