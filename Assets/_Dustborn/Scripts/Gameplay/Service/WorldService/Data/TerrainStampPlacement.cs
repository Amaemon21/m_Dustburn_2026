using System;
using UnityEngine;

[Serializable]
public struct TerrainStampPlacement
{
    public Vector2 Center;
    public Vector2 Size;
    public float Amplitude;
    public float Rotation;
    public int StampIndex;
    public TerrainStampOperation Operation;
    public string Name;

    public float Radius => 0.5f * Mathf.Sqrt(Size.x * Size.y);

    public Vector2 Local(float worldX, float worldZ)
    {
        float radians = Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        float dx = worldX - Center.x;
        float dz = worldZ - Center.y;

        return new Vector2(dx * cos + dz * sin, -dx * sin + dz * cos);
    }

    public Vector2 World(float localX, float localZ)
    {
        float radians = Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return new Vector2(Center.x + localX * cos - localZ * sin, Center.y + localX * sin + localZ * cos);
    }

    public void Bounds(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 point = World(((corner & 1) - 0.5f) * Size.x, ((corner >> 1) - 0.5f) * Size.y);

            min = new Vector2(Mathf.Min(min.x, point.x), Mathf.Min(min.y, point.y));
            max = new Vector2(Mathf.Max(max.x, point.x), Mathf.Max(max.y, point.y));
        }
    }
}
