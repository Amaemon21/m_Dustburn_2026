using NaughtyAttributes;
using Pathfinding;
using Pathfinding.RVO;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitConfig", menuName = "Dustborn/Units/Unit Config")]
public class UnitConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Identity"), HorizontalLine(2f, EColor.Blue)] public UnitView UnitPrefab { get; private set; }
    [field: SerializeField, BoxGroup("Identity")] public UnitType Type { get; private set; }
    
    [field: SerializeField, BoxGroup("Detection"), HorizontalLine(2f, EColor.Blue)] public float MaxDistance { get; private set; } = 15f;
    [field: SerializeField, BoxGroup("Detection")] public float ViewAngle { get; private set; } = 90f;
    [field: SerializeField, BoxGroup("Detection")] public LayerMask DetectionLayerMask { get; private set; }

    [field: SerializeField, BoxGroup("Chase"), HorizontalLine(2f, EColor.Blue)] public float RepathSqrThreshold { get; private set; } = 0.25f;
    [field: SerializeField, BoxGroup("Chase")] public float LostSightTimeout { get; private set; } = 10f;

    [field: SerializeField, BoxGroup("Movement"), HorizontalLine(2f, EColor.Blue)] public float MoveSpeed { get; private set; } = 3.5f;
    
    [field: SerializeField, BoxGroup("Health"), HorizontalLine(2f, EColor.Blue)] public int MaxHealth { get; private set; } = 100;
    
    [field: SerializeField, Foldout("Local Avoidance"), HorizontalLine(2f, EColor.Blue)] public bool EnableLocalAvoidance { get; private set; } = true;
    [field: SerializeField, Foldout("Local Avoidance")] public float AgentTimeHorizon { get; private set; } = 1f;
    [field: SerializeField, Foldout("Local Avoidance")] public float ObstacleTimeHorizon { get; private set; } = 0.5f;
    [field: SerializeField, Foldout("Local Avoidance")] public int MaxNeighbours { get; private set; } = 10;
    [field: SerializeField, Foldout("Local Avoidance"), Range(0f, 1f)] public float AvoidancePriority { get; private set; } = 0.5f;
    [field: SerializeField, Foldout("Local Avoidance")] public RVOLayer AvoidanceLayer { get; private set; } = RVOLayer.DefaultAgent;
    [field: SerializeField, Foldout("Local Avoidance"), EnumFlag] public RVOLayer CollidesWith { get; private set; } = (RVOLayer)(-1);
}
