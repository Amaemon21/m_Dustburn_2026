using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Object = UnityEngine.Object;

public class UnitSpawnService : IGameplayActivatable, IDisposable
{
    private const float CORPSE_SECONDS = 5f;

    private readonly UnitFactory _factory;
    private readonly UnitDatabaseConfig _unitDatabaseConfig;
    private readonly IReadOnlyList<IUnitSpawnSource> _sources;

    private readonly List<UnitView> _spawned = new();
    private readonly CompositeDisposable _deaths = new();

    private CancellationTokenSource _spawnCancellation;

    public UnitSpawnService(UnitFactory factory, UnitDatabaseConfig unitDatabaseConfig, IReadOnlyList<IUnitSpawnSource> sources)
    {
        _factory = factory;
        _unitDatabaseConfig = unitDatabaseConfig;
        _sources = sources;
    }

    public void Activate()
    {
        StopSpawning();
        _spawnCancellation = new CancellationTokenSource();

        foreach (IUnitSpawnSource source in _sources)
            foreach (UnitSpawnRequest request in source.Requests())
                RunSpawnRequest(request, _spawnCancellation.Token).Forget();
    }

    public void Dispose()
    {
        StopSpawning();
        _deaths.Dispose();
        DespawnAll();
    }

    private void StopSpawning()
    {
        _spawnCancellation?.Cancel();
        _spawnCancellation?.Dispose();
        _spawnCancellation = null;
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

    private async UniTask RunSpawnRequest(UnitSpawnRequest request, CancellationToken token)
    {
        UnitConfig config = _unitDatabaseConfig.GetUnitConfigByType(request.UnitType);

        if (config == null)
        {
            Debug.LogError($"No UnitConfig for {request.UnitType} at {request.Position}");
            return;
        }

        for (int spawned = 0; spawned < request.MaxUnits; spawned++)
        {
            UnitView view = _factory.Create(request.Position, request.Rotation, config);
            _spawned.Add(view);
            _deaths.Add(view.Controller.Died.Subscribe(_ => RemoveCorpse(view, token).Forget()));

            await UniTask.WaitForSeconds(request.SpawnDelay, cancellationToken: token);
        }
    }

    private async UniTask RemoveCorpse(UnitView view, CancellationToken token)
    {
        await UniTask.WaitForSeconds(CORPSE_SECONDS, cancellationToken: token);

        _spawned.Remove(view);
        if (view != null)
            Object.Destroy(view.gameObject);
    }
}
