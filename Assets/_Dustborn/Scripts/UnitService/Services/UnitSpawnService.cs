using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

public class UnitSpawnService : IStartable, IDisposable
{
    private readonly UnitFactory _factory;
    private readonly UnitDatabaseConfig _unitDatabaseConfig;
    private readonly UnitSpawnPoint[] _spawnPoints;

    private readonly List<UnitView> _spawned = new();

    private CancellationTokenSource _spawnCancellation;

    public UnitSpawnService(UnitFactory factory, UnitDatabaseConfig unitDatabaseConfig, UnitSpawnPoint[] spawnPoints)
    {
        _factory = factory;
        _unitDatabaseConfig = unitDatabaseConfig;
        _spawnPoints = spawnPoints;
    }

    public void Start()
    {
        _spawnCancellation = new CancellationTokenSource();

        foreach (UnitSpawnPoint spawnPoint in _spawnPoints)
        {
            if (spawnPoint == null)
                continue;

            RunSpawnPoint(spawnPoint, _spawnCancellation.Token).Forget();
        }
    }

    public void Dispose()
    {
        _spawnCancellation?.Cancel();
        _spawnCancellation?.Dispose();
        _spawnCancellation = null;

        DespawnAll();
    }

    public void DespawnAll()
    {
        foreach (UnitView view in _spawned)
        {
            if (view == null)
                continue;

            Object.Destroy(view.gameObject);
        }

        _spawned.Clear();
    }

    private async UniTask RunSpawnPoint(UnitSpawnPoint spawnPoint, CancellationToken token)
    {
        UnitConfig config = _unitDatabaseConfig.GetUnitConfigByType(spawnPoint.UnitType);

        if (config == null)
        {
            Debug.LogError($"No UnitConfig for {spawnPoint.UnitType}", spawnPoint);
            return;
        }

        for (int spawned = 0; spawned < spawnPoint.MaxUnits; spawned++)
        {
            if (spawnPoint == null)
                return;

            UnitView view = _factory.Create(spawnPoint.transform, config);
            _spawned.Add(view);

            await UniTask.WaitForSeconds(spawnPoint.SpawnDelay, cancellationToken: token);
        }
    }
}
