using UnityEngine;

public class UnitFieldOfViewService
{
    private readonly UnitProperty _property;

    public UnitFieldOfViewService(UnitProperty property)
    {
        _property = property;
    }
    
    public bool IsInside(Vector3 target)
    {
        Vector3 direction = (target - _property.UnitCenter.position).normalized;

        float angle = Vector3.Angle(_property.UnitCenter.transform.forward, direction);

        return angle <= _property.FieldOfViewAngle / 2f;
    }
}