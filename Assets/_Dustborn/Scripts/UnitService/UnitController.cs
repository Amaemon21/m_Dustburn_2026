using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public class UnitController : IDisposable
{
    private readonly UnitMovementService _movementService;
    private readonly UnitDetectionService _detectionService;
    private readonly UnitChaseService _chaseService;
    private readonly UnitWanderService _wanderService;
    private readonly UnitHealth _health;
    private readonly Transform _playerTransform;

    private readonly CompositeDisposable _compositeDisposable = new();
    private CancellationTokenSource _behaviourCancellation;

    private readonly ReactiveProperty<UnitState> _state = new(UnitState.Wander);

    public UnitState State => _state.Value;
    public bool CanSeePlayer => _detectionService.CanSeePlayer();

    public UnitController(
        UnitMovementService movementService,
        UnitDetectionService detectionService,
        UnitChaseService chaseService,
        UnitWanderService wanderService,
        UnitHealth health,
        Transform playerTransform)
    {
        _movementService = movementService;
        _detectionService = detectionService;
        _chaseService = chaseService;
        _wanderService = wanderService;
        _health = health;
        _playerTransform = playerTransform;
    }

    public void Initialize()
    {
        _behaviourCancellation = new CancellationTokenSource();

        _compositeDisposable.Add(Observable
            .EveryUpdate()
            .Select(_ => _detectionService.CanSeePlayer())
            .DistinctUntilChanged()
            .Where(canSee => canSee)
            .Subscribe(_ => _state.Value = UnitState.Chase));

        _compositeDisposable.Add(_state.Subscribe(SelectState));

        _compositeDisposable.Add(_chaseService.OnPlayerLost
            .Subscribe(_ => _state.Value = UnitState.Wander));
    }

    private void SelectState(UnitState state)
    {
        CancellationToken token = RestartBehaviourCancellation();

        switch (state)
        {
            case UnitState.Wander:
                _wanderService.Wander(token).Forget();
                break;

            case UnitState.Chase:
                _chaseService.Chase(token).Forget();
                break;
        }
    }

    private CancellationToken RestartBehaviourCancellation()
    {
        _behaviourCancellation?.Cancel();
        _behaviourCancellation?.Dispose();

        _behaviourCancellation = new CancellationTokenSource();

        return _behaviourCancellation.Token;
    }

    public void Dispose()
    {
        _compositeDisposable.Dispose();
        _state.Dispose();

        _behaviourCancellation?.Cancel();
        _behaviourCancellation?.Dispose();
        _behaviourCancellation = null;
    }
}