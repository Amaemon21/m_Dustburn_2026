using Pathfinding;
using Pathfinding.RVO;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitConfig", menuName = "Gameplay/Units/Unit Config")]
public class UnitConfig : ScriptableObject
{
    [field: SerializeField] public UnitView UnitPrefab { get; private set; }
    [field: SerializeField] public UnitType Type { get; private set; }
    
    [Header("Detection")]
    [field: SerializeField] public float MaxDistance { get; private set; } = 15f;
    [field: SerializeField] public float ViewAngle { get; private set; } = 90f;
    [field: SerializeField] public LayerMask DetectionLayerMask { get; private set; }

    [Header("Chase")]
    [field: SerializeField] public float RepathSqrThreshold { get; private set; } = 0.25f;
    [field: SerializeField] public float LostSightTimeout { get; private set; } = 10f;

    [Header("Movement")]
    [field: SerializeField] public float MoveSpeed { get; private set; } = 3.5f;
    
    [Header("Health")]
    [field: SerializeField] public int MaxHealth { get; private set; } = 100;
    
    [Header("Local Avoidance")]
    [field: SerializeField] public bool EnableLocalAvoidance { get; private set; } = true;
    [field: SerializeField] public float AgentTimeHorizon { get; private set; } = 1f;
    [field: SerializeField] public float ObstacleTimeHorizon { get; private set; } = 0.5f;
    [field: SerializeField] public int MaxNeighbours { get; private set; } = 10;
    [field: SerializeField, Range(0f, 1f)] public float AvoidancePriority { get; private set; } = 0.5f;
    [field: SerializeField] public RVOLayer AvoidanceLayer { get; private set; } = RVOLayer.DefaultAgent;
    [field: SerializeField, EnumFlag] public RVOLayer CollidesWith { get; private set; } = (RVOLayer)(-1);
}