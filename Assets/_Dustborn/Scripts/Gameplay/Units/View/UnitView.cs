using Pathfinding;
using UnityEngine;

public class UnitView : MonoBehaviour, IDamageableView
{
    [field: SerializeField] public Transform UnitCenter { get; private set; }
    [field: SerializeField] public FollowerEntity FollowerEntity { get; private set; }
    
    public Transform Root => transform;
    
    public UnitController Controller { get; set; }
    public UnitConfig Config { get; set; }
    public IDamageable Damageable { get; set; }

    private void OnDestroy()
    {
        Controller?.Dispose();
        Controller = null;
    }
}
