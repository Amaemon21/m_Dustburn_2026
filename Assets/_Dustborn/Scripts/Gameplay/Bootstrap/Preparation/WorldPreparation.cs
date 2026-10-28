using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

public sealed class WorldPreparation : IGameplayPreparation
{
    private const float WORLD_WAIT_TIMEOUT = 180f;

    private readonly PlayerSpawnService _player;
    private readonly WorldGenerator _world;
    private readonly GameWorldLoader _worldLoader;
    private readonly IDecorObjectHook _decorHook;
    private readonly IWorldObjectHook _objectHook;
    private readonly WorldSaveRepository _worldSave;
    private readonly PlayerPlacementRepository _placement;
    private readonly UnitNavigation _navigation;

    public WorldPreparation(PlayerSpawnService player, WorldGenerator world, GameWorldLoader worldLoader, IDecorObjectHook decorHook,
        IWorldObjectHook objectHook, WorldSaveRepository worldSave, PlayerPlacementRepository placement, UnitNavigation navigation)
    {
        _navigation = navigation;
        _player = player;
        _world = world;
        _worldLoader = worldLoader;
        _decorHook = decorHook;
        _objectHook = objectHook;
        _worldSave = worldSave;
        _placement = placement;
    }

    public async UniTask Prepare(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        WorldData world = await _worldLoader.LoadAsync(progress, token);

        _worldSave.Claim(world.Fingerprint, world.Config.Seed, world.Config.WorldSize, world.Name);

        Transform player = SpawnPlayer(world);

        _world.LoadWorld(world, player, _decorHook, _objectHook);

        float deadline = Time.realtimeSinceStartup + WORLD_WAIT_TIMEOUT;

        while (!_world.Ready)
        {
            if (_world.Failed)
                throw new InvalidOperationException($"{nameof(WorldPreparation)}: the world failed to load, '{_world.Status}'; the exception is in the log above");

            if (Time.realtimeSinceStartup > deadline)
                throw new TimeoutException($"{nameof(WorldPreparation)}: the world did not report Ready in {WORLD_WAIT_TIMEOUT:0} s, last status '{_world.Status}'");

            progress.Report(new SceneLoadStatus(_world.Progress, _world.Status));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        await _navigation.Build(progress, token);
        _world.GroundChanged += _navigation.Refresh;
    }

    private Transform SpawnPlayer(WorldData world)
    {
        if (_placement.TryGet(out Vector3 position, out float yaw))
            return _player.Spawn(position, Quaternion.Euler(0f, yaw, 0f));

        return _player.Spawn(WorldSpawnPoint.Find(world), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
    }
}
