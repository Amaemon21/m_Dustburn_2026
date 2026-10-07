using System;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class PlayerSpawnService : IPlayerProvider, IDisposable
{
    private readonly GameObject _prefab;
    private readonly PlayerHandsFactory _hands;

    private GameObject _player;

    public Transform Player => _player == null ? null : _player.transform;
    public PlayerCharacter Character { get; private set; }

    public PlayerSpawnService(GameplayAssets assets, PlayerHandsFactory hands)
    {
        _prefab = assets.PlayerPrefab;
        _hands = hands;
    }

    public Transform Spawn(Vector3 position, Quaternion rotation)
    {
        if (_player != null)
            throw new InvalidOperationException($"{nameof(PlayerSpawnService)}: the player is already spawned");

        _player = Object.Instantiate(_prefab, position, rotation);
        _player.name = _prefab.name;

        PlayerView view = _player.GetComponentInChildren<PlayerView>(true);
        if (view == null)
            throw new InvalidOperationException($"{nameof(PlayerSpawnService)}: player prefab '{_prefab.name}' has no {nameof(PlayerView)}");

        Character = new PlayerCharacter(view, _hands.Create(view));

        Debug.Log($"{nameof(PlayerSpawnService)}: player spawned at {position.x:0}, {position.y:0}, {position.z:0}");

        return _player.transform;
    }

    public void Dispose()
    {
        Character?.Dispose();
        Character = null;

        if (_player == null)
            return;

        Object.Destroy(_player);
        _player = null;
    }
}
