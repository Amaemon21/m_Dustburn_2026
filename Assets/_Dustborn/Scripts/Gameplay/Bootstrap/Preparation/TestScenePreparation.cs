using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class TestScenePreparation : IGameplayPreparation
{
    private const string FINGERPRINT = "scene/" + nameof(Scenes.Gameplay_Test);

    private static readonly Vector3 FALLBACK_POSITION = new(0f, 3f, 0f);

    private readonly PlayerSpawnService _player;
    private readonly Transform _spawnPoint;
    private readonly WorldSaveRepository _worldSave;
    private readonly PlayerPlacementRepository _placement;
    private readonly UnitNavigation _navigation;

    public TestScenePreparation(PlayerSpawnService player, Transform spawnPoint, WorldSaveRepository worldSave,
        PlayerPlacementRepository placement, UnitNavigation navigation)
    {
        _player = player;
        _spawnPoint = spawnPoint;
        _worldSave = worldSave;
        _placement = placement;
        _navigation = navigation;
    }

    public async UniTask Prepare(IProgress<SceneLoadStatus> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        _worldSave.Claim(FINGERPRINT, 0, 0);
        SpawnPlayer();

        await _navigation.Build(progress, token);
    }

    private void SpawnPlayer()
    {
        if (_placement.TryGet(out Vector3 position, out float yaw))
        {
            _player.Spawn(position, Quaternion.Euler(0f, yaw, 0f));
            return;
        }

        if (_spawnPoint == null)
        {
            Debug.LogWarning($"{nameof(TestScenePreparation)}: no test spawn point on the scope, spawning the player at {FALLBACK_POSITION}");
            _player.Spawn(FALLBACK_POSITION, Quaternion.identity);
            return;
        }

        _player.Spawn(_spawnPoint.position, _spawnPoint.rotation);
    }
}
