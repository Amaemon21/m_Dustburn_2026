using System;
using UnityEngine;

[Flags]
public enum TilePorts : byte
{
    None = 0,
    East = 1,
    North = 2,
    West = 4,
    South = 8
}

public enum TileShape
{
    Empty,
    Cap,
    Straight,
    Corner,
    Tee,
    Intersection
}

public static class TilePortRules
{
    public static readonly TilePorts[] SIDES = { TilePorts.East, TilePorts.North, TilePorts.West, TilePorts.South };

    public static TilePorts Opposite(TilePorts side)
    {
        return side switch
        {
            TilePorts.East => TilePorts.West,
            TilePorts.North => TilePorts.South,
            TilePorts.West => TilePorts.East,
            TilePorts.South => TilePorts.North,
            _ => TilePorts.None
        };
    }

    public static int StepI(TilePorts side)
    {
        return side == TilePorts.East ? 1 : side == TilePorts.West ? -1 : 0;
    }

    public static int StepJ(TilePorts side)
    {
        return side == TilePorts.North ? 1 : side == TilePorts.South ? -1 : 0;
    }

    public static int Count(TilePorts ports)
    {
        int count = 0;

        foreach (TilePorts side in SIDES)
        {
            if ((ports & side) != 0)
                count++;
        }

        return count;
    }

    public static bool Has(TilePorts ports, TilePorts side)
    {
        return (ports & side) != 0;
    }

    public static TileShape ShapeOf(TilePorts ports)
    {
        switch (Count(ports))
        {
            case 0:
                return TileShape.Empty;
            case 1:
                return TileShape.Cap;
            case 2:
                return Has(ports, TilePorts.East) == Has(ports, TilePorts.West) ? TileShape.Straight : TileShape.Corner;
            case 3:
                return TileShape.Tee;
            default:
                return TileShape.Intersection;
        }
    }

    public static Vector2 Direction(TilePorts side, Vector2 axisU, Vector2 axisV)
    {
        return side switch
        {
            TilePorts.East => axisU,
            TilePorts.North => axisV,
            TilePorts.West => -axisU,
            _ => -axisV
        };
    }

    public static TilePorts Nearest(float localAngle)
    {
        float quarter = Mathf.PI * 0.5f;
        int index = Mathf.RoundToInt(localAngle / quarter) % 4;

        if (index < 0)
            index += 4;

        return SIDES[index];
    }

    public static float Angle(TilePorts side)
    {
        return Index(side) * Mathf.PI * 0.5f;
    }

    public static int Index(TilePorts side)
    {
        return side switch
        {
            TilePorts.East => 0,
            TilePorts.North => 1,
            TilePorts.West => 2,
            _ => 3
        };
    }
}
