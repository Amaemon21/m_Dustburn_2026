using System;
using UnityEngine;

[Serializable]
public class PropPlacement
{
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public Vector3 Position { get; private set; }
    [field: SerializeField] public float Yaw { get; private set; }
    [field: SerializeField] public float GroundOffset { get; private set; }
    [field: SerializeField] public bool TiltToGround { get; private set; }
    [field: SerializeField] public PropContext Context { get; private set; }

    public PropPlacement(PropDefinition definition, PropContext context, Vector3 position, float yaw)
    {
        Prefab = definition.Prefab;
        Position = position;
        Yaw = yaw;
        GroundOffset = definition.GroundOffset;
        TiltToGround = definition.TiltToGround;
        Context = context;
    }

    public PropPlacement(GameObject prefab, PropContext context, Vector3 position, float yaw, float groundOffset, bool tiltToGround)
    {
        Prefab = prefab;
        Position = position;
        Yaw = yaw;
        GroundOffset = groundOffset;
        TiltToGround = tiltToGround;
        Context = context;
    }
}
