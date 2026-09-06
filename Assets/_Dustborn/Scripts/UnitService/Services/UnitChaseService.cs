using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public class UnitChaseService
{
    private readonly UnitConfig _config;
    private readonly UnitMovementService _movementService;
    private readonly UnitDetectionService _detectionService;
    private readonly Transform _playerTransform;
    
    private readonly Subject<Unit> _lostSubject = new();
    
    public Observable<Unit> OnPlayerLost => _lostSubject;

    public UnitChaseService(UnitConfig config, UnitMovementService movementService, UnitDetectionService detectionService, Transform playerTransform)
    {
        _config = config;
        _movementService = movementService;
        _detectionService = detectionService;
        _playerTransform = playerTransform;
    }

    public async UniTask Chase(CancellationToken token)
    {
        if (!_detectionService.IsPlayerAvailable)
            return;

        Vector3 lastTarget = _playerTransform.position;

        _movementService.SetDestination(lastTarget);
        
        float lostElapsed = 0f;

        while (!token.IsCancellationRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);

            if (!_detectionService.IsPlayerAvailable)
                return;

            if (_detectionService.IsPlayerOutOfRange())
            {
                lostElapsed += Time.deltaTime;

                if (lostElapsed >= _config.LostSightTimeout)
                {
                    _lostSubject.OnNext(Unit.Default);
                    return;
                }
            }
            else
            {
                lostElapsed = 0f;
            }

            Vector3 target = _playerTransform.position;

            if ((target - lastTarget).sqrMagnitude < _config.RepathSqrThreshold)
                continue;

            _movementService.SetDestination(target);

            lastTarget = target;
        }
    }
}
