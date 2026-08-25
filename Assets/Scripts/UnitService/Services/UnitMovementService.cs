using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UnitMovementService
{
    private readonly UnitProperty _property;
    private readonly RandomWalkablePointService _randomWalkablePointService;

    public UnitMovementService(UnitProperty property, UnitFieldOfViewService unitFieldOfViewService)
    {
        _property = property;

        _randomWalkablePointService = new RandomWalkablePointService(property, unitFieldOfViewService);
    }

    public async UniTask Move(CancellationToken token)
    {
        Vector3 target = _randomWalkablePointService.GetWalkablePoint();

        await MoveToDestinationAsync(target, token, true);
    }

    public async UniTask MoveTo(Vector3 target, CancellationToken token)
    {
        await MoveToDestinationAsync(target, token, false);
    }

    private async UniTask MoveToDestinationAsync(Vector3 target, CancellationToken token, bool waitAfterArrival)
    {
        _property.FollowerEntity.destination = target;

        await UniTask.WaitUntil(() => _property.FollowerEntity.reachedEndOfPath, cancellationToken: token);

        if (waitAfterArrival)
        {
            await UniTask.Delay(2000, cancellationToken: token);
        }
    }
}