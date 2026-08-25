using UnityEditor;
using UnityEngine;

public class UnitGizmosDraw : MonoBehaviour
{
    [SerializeField] private UnitBehaviour _unitBehaviour;
    
    private void OnDrawGizmosSelected()
    {
        var center = _unitBehaviour.Property.FollowerEntity.position;
        var radius = _unitBehaviour.Property.Radius;
        var fov = _unitBehaviour.Property.FieldOfViewAngle;
        var forward = transform.forward;

        Handles.color = new Color(0f, 0f, 1f, 0.15f);
        Handles.DrawSolidDisc(center, Vector3.up, radius);

        Handles.color = new Color(0f, 0.5f, 1f, 0.8f);
        Handles.DrawWireDisc(center, Vector3.up, radius);
        
        var leftDirection = Quaternion.AngleAxis(-fov / 2f, Vector3.up) * forward;
        var rightDirection = Quaternion.AngleAxis(fov / 2f, Vector3.up) * forward;

        Handles.color = new Color(1f, 0.8f, 0f, 0.15f);
        Handles.DrawSolidArc(center, Vector3.up, leftDirection, fov, radius);
        
        Handles.color = new Color(1f, 0.8f, 0f, 0.8f);

        Handles.DrawLine(center, center + leftDirection * radius);

        Handles.DrawLine(center, center + rightDirection * radius);
        
        Handles.color = Color.white;

        Handles.DrawLine(center, center + forward * radius);
    }
}