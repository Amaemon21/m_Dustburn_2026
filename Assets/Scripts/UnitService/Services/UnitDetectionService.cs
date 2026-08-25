using UnityEngine;

public class UnitDetectionService
{
    private readonly UnitProperty _property;
    private readonly UnitFieldOfViewService _unitFieldOfViewService;
    private readonly Transform _playerTransform;

    public UnitDetectionService(UnitProperty property, UnitFieldOfViewService unitFieldOfViewService, Transform playerTransform)
    {
        _property = property;
        _unitFieldOfViewService = unitFieldOfViewService;
        _playerTransform = playerTransform;
    }
    
    public bool CanSeePlayer()
    {
        var playerPosition = _playerTransform.position;
        var unitPosition = _property.UnitCenter.position;
        
        float distanceToPlayer = Vector3.Distance(playerPosition, unitPosition);
        Vector3 directionToPlayer = (playerPosition - unitPosition).normalized;
        
        if (distanceToPlayer > _property.MaxDistance)
            return false;
        
        if (!_unitFieldOfViewService.IsInside(_playerTransform.position))
            return false;

        if (!Physics.Raycast(unitPosition, directionToPlayer, out RaycastHit hit, _property.MaxDistance, _property.LayerMask))
            return false;

        if (!hit.collider.TryGetComponent<PlayerController>(out _))
            return false;
        
        return true;
    }
}