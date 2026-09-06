using UnityEngine;

public class UnitFieldOfViewService
{
    private readonly UnitConfig _config;
    private readonly Transform _unitCenter;

    public UnitFieldOfViewService(UnitConfig config, Transform unitCener)
    {
        _config = config;
        _unitCenter = unitCener;
    }
    
    public bool IsInside(Vector3 target)
    {
        Vector3 direction = (target - _unitCenter.position).normalized;

        float angle = Vector3.Angle(_unitCenter.transform.forward, direction);

        return angle <= _config.ViewAngle / 2f;
    }
}