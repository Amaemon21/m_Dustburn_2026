using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UnitChaseService
{
    private readonly UnitMovementService _movementService;
    private readonly UnitDetectionService _detectionService;
    private readonly Transform _playerTransform;

    public UnitChaseService(UnitMovementService movementService, UnitDetectionService detectionService, Transform playerTransform)
    {
        _movementService = movementService;
        _detectionService = detectionService;
        _playerTransform = playerTransform;
    }

    public async UniTask Chase(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Vector3 target = _playerTransform.position;

            await _movementService.MoveTo(target, token);
        }
    }
}