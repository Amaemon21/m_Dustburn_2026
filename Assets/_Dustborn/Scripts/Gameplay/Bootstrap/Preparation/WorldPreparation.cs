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

    public WorldPreparation(PlayerSpawnService player, WorldGenerator world)
    {
        _player = player;
        _world = world;
    }

    public async UniTask Prepare(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        BakedWorld world = _world.World;

        if (world == null || !world.IsValid)
            throw new InvalidOperationException($"{nameof(WorldPreparation)}: the scene has no valid baked world, so there is nowhere to put the player");

        Vector3 position = WorldSpawnPoint.Find(world);
        Transform player = _player.Spawn(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        _world.SetViewer(player);
        _world.LoadWorld();

        float deadline = Time.realtimeSinceStartup + WORLD_WAIT_TIMEOUT;

        while (!_world.Ready)
        {
            if (Time.realtimeSinceStartup > deadline)
                throw new TimeoutException($"{nameof(WorldPreparation)}: the world did not report Ready in {WORLD_WAIT_TIMEOUT:0} s, last status '{_world.Status}'");

            progress.Report(new SceneLoadStatus(_world.Progress, _world.Status));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }
}
