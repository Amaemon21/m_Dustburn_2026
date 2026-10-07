using System;
using UnityEngine;

[Serializable]
public struct WaterStampPlacement
{
    public int StampIndex;
    public string Name;
    public WaterStampKind Kind;
    public WaterStampCategory Category;
    public Vector2 Origin;
    public Vector2 Size;
    public float Scale;
    public float Rotation;
    public bool Mirror;
    public int River;
    public int Body;
    public int First;
    public int Last;

    public Vector2 World(Vector2 stamp)
    {
        float radians = Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        float x = stamp.x * Scale;
        float z = (Mirror ? -stamp.y : stamp.y) * Scale;

        return new Vector2(Origin.x + x * cos - z * sin, Origin.y + x * sin + z * cos);
    }

    public Vector2 Stamp(Vector2 world)
    {
        float radians = Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        float dx = world.x - Origin.x;
        float dz = world.y - Origin.y;
        float x = (dx * cos + dz * sin) / Scale;
        float z = (-dx * sin + dz * cos) / Scale;

        return new Vector2(x, Mirror ? -z : z);
    }

    public Vector2 StampDirection(Vector2 world)
    {
        float radians = Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        float x = world.x * cos + world.y * sin;
        float z = -world.x * sin + world.y * cos;

        return new Vector2(x, Mirror ? -z : z);
    }

    public Vector2 Uv(Vector2 stamp)
    {
        return new Vector2(stamp.x / Size.x, stamp.y / Size.y);
    }

    public Vector2 Center => World(0.5f * Size);

    public void Bounds(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 point = World(new Vector2((corner & 1) * Size.x, (corner >> 1) * Size.y));

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
    }

    public static WaterStampPlacement Fit(int stampIndex, WaterStampDefinition definition, bool mirror, float scale, Vector2 fromStamp, Vector2 toStamp, Vector2 fromWorld, Vector2 toWorld)
    {
        var placement = new WaterStampPlacement
        {
            StampIndex = stampIndex,
            Name = definition.Name,
            Kind = definition.Kind,
            Category = definition.Category,
            Size = definition.RecommendedSize,
            Scale = scale,
            Mirror = mirror,
            River = -1,
            Body = -1
        };

        Vector2 local = toStamp - fromStamp;

        if (mirror)
            local.y = -local.y;

        Vector2 world = toWorld - fromWorld;
        placement.Rotation = (Mathf.Atan2(world.y, world.x) - Mathf.Atan2(local.y, local.x)) * Mathf.Rad2Deg;
        placement.Origin = Vector2.zero;
        placement.Origin = fromWorld - placement.World(fromStamp);

        return placement;
    }

    public static WaterStampPlacement Around(int stampIndex, WaterStampDefinition definition, bool mirror, float scale, float rotation, Vector2 pivotStamp, Vector2 pivotWorld)
    {
        var placement = new WaterStampPlacement
        {
            StampIndex = stampIndex,
            Name = definition.Name,
            Kind = definition.Kind,
            Category = definition.Category,
            Size = definition.RecommendedSize,
            Scale = scale,
            Rotation = rotation,
            Mirror = mirror,
            River = -1,
            Body = -1
        };

        placement.Origin = pivotWorld - placement.World(pivotStamp);

        return placement;
    }
}

[Serializable]
public struct WaterStampRejection
{
    public Vector2 Position;
    public string Name;
    public string Reason;
}
