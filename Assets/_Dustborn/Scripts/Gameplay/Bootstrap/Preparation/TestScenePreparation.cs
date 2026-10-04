using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class TestScenePreparation : IGameplayPreparation
{
    private static readonly Vector3 FALLBACK_POSITION = new(0f, 3f, 0f);

    private readonly PlayerSpawnService _player;
    private readonly Transform _spawnPoint;

    public TestScenePreparation(PlayerSpawnService player, Transform spawnPoint)
    {
        _player = player;
        _spawnPoint = spawnPoint;
    }

    public UniTask Prepare(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (_spawnPoint == null)
        {
            Debug.LogWarning($"{nameof(TestScenePreparation)}: no test spawn point on the scope, spawning the player at {FALLBACK_POSITION}");
            _player.Spawn(FALLBACK_POSITION, Quaternion.identity);
            return UniTask.CompletedTask;
        }

        _player.Spawn(_spawnPoint.position, _spawnPoint.rotation);
        return UniTask.CompletedTask;
    }
}
