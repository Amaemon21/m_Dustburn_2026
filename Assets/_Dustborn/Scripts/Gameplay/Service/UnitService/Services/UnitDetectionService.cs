using UnityEngine;

public class UnitDetectionService
{
    private readonly UnitConfig _config;
    private readonly UnitView _view;
    private readonly UnitFieldOfViewService _unitFieldOfViewService;
    private readonly Transform _playerTransform;

    public UnitDetectionService(UnitConfig config, UnitView view,UnitFieldOfViewService unitFieldOfViewService, Transform playerTransform)
    {
        _config = config;
        _view = view;
        _unitFieldOfViewService = unitFieldOfViewService;
        _playerTransform = playerTransform;
    }

    public bool IsPlayerAvailable => _playerTransform != null && _view != null && _view.UnitCenter != null;

    public bool CanSeePlayer()
    {
        if (!IsPlayerAvailable)
            return false;

        if (IsPlayerOutOfRange())
            return false;

        if (!_unitFieldOfViewService.IsInside(_playerTransform.position))
            return false;

        var unitPosition = _view.UnitCenter.position;
        var directionToPlayer = (_playerTransform.position - unitPosition).normalized;

        if (!Physics.Raycast(unitPosition, directionToPlayer, out RaycastHit hit, _config.MaxDistance, _config.DetectionLayerMask))
            return false;

        return hit.collider.TryGetComponent<PlayerController>(out _);
    }

    public bool IsPlayerOutOfRange()
    {
        if (!IsPlayerAvailable)
            return true;

        return !DistanceUtility.WithinRadius(_playerTransform.position, _view.UnitCenter.position, _config.MaxDistance);
    }
}
