using System.Threading;
using Cysharp.Threading.Tasks;

public class UnitWanderService
{
    private readonly UnitMovementService _movementService;

    public UnitWanderService(UnitMovementService movementService)
    {
        _movementService = movementService;
    }

    public async UniTask Wander(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await _movementService.Move(token);
        }
    }
}