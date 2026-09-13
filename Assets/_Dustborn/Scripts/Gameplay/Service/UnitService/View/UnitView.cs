using Pathfinding;
using UnityEngine;

public class UnitView : MonoBehaviour
{
    [field: SerializeField] public Transform UnitCenter { get; private set; }
    [field: SerializeField] public FollowerEntity FollowerEntity { get; private set; }
    
    public Transform Root => transform;
    
    public UnitController Controller { get; set; }
    public UnitConfig Config { get; set; }

    private void OnDestroy()
    {
        Controller?.Dispose();
        Controller = null;
    }
}
