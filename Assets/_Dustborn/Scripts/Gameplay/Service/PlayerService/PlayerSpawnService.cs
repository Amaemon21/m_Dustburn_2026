using System;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class PlayerSpawnService : IDisposable
{
    public const string RESOURCES_PATH = "Player";

    private const int SPAWN_ATTEMPTS = 64;
    private const float EDGE_MARGIN = 256f;
    private const float SHORE_MARGIN = 4f;
    private const float GROUND_CLEARANCE = 3f;

    private readonly WorldGenerator _world;

    private GameObject _player;
    private PlayerController _control;

    public Transform Player => _player == null ? null : _player.transform;

    public PlayerSpawnService(WorldGenerator world)
    {
        _world = world;
    }

    public Transform Spawn()
    {
        if (_player != null)
            return _player.transform;

        BakedWorld world = _world.World;

        if (world == null || !world.IsValid)
            throw new InvalidOperationException($"{nameof(PlayerSpawnService)}: the scene has no valid baked world, so there is nowhere to put the player");

        GameObject prefab = Resources.Load<GameObject>(RESOURCES_PATH);

        if (prefab == null)
            throw new InvalidOperationException($"{nameof(PlayerSpawnService)}: no player prefab at Resources/{RESOURCES_PATH}");

        Vector3 position = FindGround(world);

        _player = Object.Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        _player.name = prefab.name;
        _control = _player.GetComponentInChildren<PlayerController>(true);

        SetControlEnabled(false);

        Debug.Log($"{nameof(PlayerSpawnService)}: player spawned at {position.x:0}, {position.y:0}, {position.z:0}");

        return _player.transform;
    }

    public void SetControlEnabled(bool value)
    {
        if (_control != null)
            _control.enabled = value;
    }

    public void Dispose()
    {
        if (_player == null)
            return;

        Object.Destroy(_player);
        _player = null;
        _control = null;
    }

    private static Vector3 FindGround(BakedWorld world)
    {
        WorldGenerationConfig config = world.Config;

        int resolution = config.HeightMapResolution;
        float cellSize = config.WorldSize / (float)(resolution - 1);
        float dryGround = config.SeaLevel + SHORE_MARGIN;

        float low = Mathf.Min(EDGE_MARGIN, config.WorldSize * 0.25f);
        float high = config.WorldSize - low;

        NativeArray<byte> raw = world.HeightMap.GetData<byte>();

        Vector3 highest = Vector3.zero;
        float highestGround = float.NegativeInfinity;

        for (int attempt = 0; attempt < SPAWN_ATTEMPTS; attempt++)
        {
            float x = Random.Range(low, high);
            float z = Random.Range(low, high);
            float ground = Sample(raw, resolution, cellSize, config.MaxHeight, x, z);
            var candidate = new Vector3(x, ground + GROUND_CLEARANCE, z);

            if (ground >= dryGround)
                return candidate;

            if (ground <= highestGround)
                continue;

            highestGround = ground;
            highest = candidate;
        }

        Debug.LogWarning($"{nameof(PlayerSpawnService)}: no dry ground in {SPAWN_ATTEMPTS} tries, spawning at the highest one, {highestGround:0.0} m against a sea level of {config.SeaLevel:0.0} m");

        return highest;
    }

    private static float Sample(NativeArray<byte> raw, int resolution, float cellSize, float maxHeight, float worldX, float worldZ)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(worldX / cellSize), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(worldZ / cellSize), 0, resolution - 1);
        int index = (y * resolution + x) * 2;

        if (index < 0 || index + 1 >= raw.Length)
            return 0f;

        int value = raw[index] | (raw[index + 1] << 8);

        return value / (float)ushort.MaxValue * maxHeight;
    }
}
