using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UnitMovementService
{
    private const int WAIT_AFTER_ARRIVAL_MS = 2000;

    private readonly UnitConfig _config;
    private readonly UnitView _view;
    private readonly RandomWalkablePointService _randomWalkablePointService;

    public UnitMovementService(UnitConfig config, UnitView view, UnitFieldOfViewService unitFieldOfViewService, RandomWalkablePointService randomWalkablePointService)
    {
        _config = config;
        _view = view;

        _randomWalkablePointService = randomWalkablePointService;
    }

    public Vector3 Destination => _view.FollowerEntity.destination;

    public bool ReachedDestination => _view.FollowerEntity.reachedDestination;

    public async UniTask Move(CancellationToken token)
    {
        Vector3 target = _randomWalkablePointService.GetWalkablePoint();

        await MoveToDestinationAsync(target, token, true);
    }

    public async UniTask MoveTo(Vector3 target, CancellationToken token)
    {
        await MoveToDestinationAsync(target, token, false);
    }
    
    public void SetDestination(Vector3 target)
    {
        _view.FollowerEntity.destination = target;
    }

    private async UniTask MoveToDestinationAsync(Vector3 target, CancellationToken token, bool waitAfterArrival)
    {
        _view.FollowerEntity.destination = target;

        await UniTask.WaitUntil(() => _view.FollowerEntity.reachedEndOfPath, cancellationToken: token);

        if (waitAfterArrival)
        {
            await UniTask.Delay(WAIT_AFTER_ARRIVAL_MS, cancellationToken: token);
        }
    }
}
