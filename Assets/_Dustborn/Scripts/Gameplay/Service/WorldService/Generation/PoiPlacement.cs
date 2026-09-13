using System;
using UnityEngine;

[Serializable]
public class PoiPlacement
{
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public Vector3 Position { get; private set; }
    [field: SerializeField] public float Rotation { get; private set; }
    [field: SerializeField] public Vector2 Footprint { get; private set; }
    [field: SerializeField] public DistrictType District { get; private set; }
    [field: SerializeField] public Vector2 PivotOffset { get; private set; }

    public PoiPlacement(GameObject prefab, Vector2 position, float rotation, Vector2 footprint, DistrictType district, Vector2 pivotOffset)
    {
        Prefab = prefab;
        Position = new Vector3(position.x, 0f, position.y);
        Rotation = rotation;
        Footprint = footprint;
        District = district;
        PivotOffset = pivotOffset;
    }

    public Vector2 Ground => new(Position.x, Position.z);

    public Vector2 Forward
    {
        get
        {
            float radians = Rotation * Mathf.Deg2Rad;

            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }
    }

    public Vector3 PrefabPosition => Position - Quaternion.Euler(0f, Rotation, 0f) * new Vector3(PivotOffset.x, 0f, PivotOffset.y);

    public void SetHeight(float height)
    {
        Position = new Vector3(Position.x, height, Position.z);
    }
}
